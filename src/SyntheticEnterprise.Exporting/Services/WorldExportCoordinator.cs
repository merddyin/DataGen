using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SyntheticEnterprise.Exporting.Contracts;

namespace SyntheticEnterprise.Exporting.Services;

public sealed class WorldExportCoordinator : IWorldExportCoordinator
{
    private readonly IEntityTableProvider _entityTableProvider;
    private readonly ILinkTableProvider _linkTableProvider;
    private readonly IArtifactWriter _artifactWriter;
    private readonly IExportManifestBuilder _manifestBuilder;
    private readonly IExportSummaryBuilder _summaryBuilder;
    private readonly IExportPathResolver _pathResolver;

    public WorldExportCoordinator(
        IEntityTableProvider entityTableProvider,
        ILinkTableProvider linkTableProvider,
        IArtifactWriter artifactWriter,
        IExportManifestBuilder manifestBuilder,
        IExportSummaryBuilder summaryBuilder,
        IExportPathResolver pathResolver)
    {
        _entityTableProvider = entityTableProvider;
        _linkTableProvider = linkTableProvider;
        _artifactWriter = artifactWriter;
        _manifestBuilder = manifestBuilder;
        _summaryBuilder = summaryBuilder;
        _pathResolver = pathResolver;
    }

    public ExportManifestV2 Export(object generationResult, ExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(generationResult);
        ArgumentNullException.ThrowIfNull(request);

        var outputRoot = _pathResolver.ResolveRoot(request.OutputPath, request.ArtifactPrefix);
        Directory.CreateDirectory(outputRoot);

        var artifacts = new List<ExportArtifactDescriptor>();

        foreach (dynamic descriptor in _entityTableProvider.GetDescriptors())
        {
            var rows = MaterializeRows(generationResult, descriptor).ToList();
            artifacts.Add(_artifactWriter.Write(outputRoot, descriptor.RelativePathStem, descriptor.Columns, rows, ExportArtifactKind.EntityTable));
        }

        foreach (dynamic descriptor in _linkTableProvider.GetDescriptors())
        {
            var rows = MaterializeRows(generationResult, descriptor).ToList();
            artifacts.Add(_artifactWriter.Write(outputRoot, descriptor.RelativePathStem, descriptor.Columns, rows, ExportArtifactKind.LinkTable));
        }

        if (request.IncludeSummary)
        {
            var summary = _summaryBuilder.Build(generationResult, artifacts.Count);
            var summaryBytes = JsonSerializer.SerializeToUtf8Bytes(summary, new JsonSerializerOptions { WriteIndented = true });
            var summaryPath = Path.Combine(outputRoot, "export_summary.json");
            File.WriteAllBytes(summaryPath, summaryBytes);

            artifacts.Add(new ExportArtifactDescriptor
            {
                LogicalName = "export_summary",
                RelativePath = "export_summary.json",
                ArtifactKind = ExportArtifactKind.Summary,
                MediaType = "application/json",
                RowCount = 1,
                Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(summaryBytes)).ToLowerInvariant(),
                SizeBytes = summaryBytes.LongLength,
                Columns = []
            });
        }

        var manifest = _manifestBuilder.Build(request, artifacts);

        if (request.IncludeManifest)
        {
            var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllBytes(Path.Combine(outputRoot, "manifest.json"), manifestBytes);
        }

        return manifest;
    }

    private static IEnumerable<IReadOnlyDictionary<string, object?>> MaterializeRows(dynamic generationResult, dynamic descriptor)
    {
        var records = descriptor.RecordAccessor(generationResult);
        var sorted = System.Linq.Enumerable.OrderBy(records, descriptor.SortKeySelector);
        foreach (var record in sorted)
        {
            yield return descriptor.RowProjector(record);
        }
    }
}
