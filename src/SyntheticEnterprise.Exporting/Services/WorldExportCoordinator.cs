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

        if (_entityTableProvider is IExportRequestAware entityRequestAware)
        {
            entityRequestAware.ApplyRequest(request);
        }

        if (_linkTableProvider is IExportRequestAware linkRequestAware)
        {
            linkRequestAware.ApplyRequest(request);
        }

        var outputRoot = _pathResolver.ResolveRoot(request.OutputPath, request.ArtifactPrefix, request.ExportedAtUtc);

        var entityDescriptors = _entityTableProvider.GetDescriptors();
        var linkDescriptors = _linkTableProvider.GetDescriptors();

        PrepareOutputRoot(outputRoot, request, entityDescriptors, linkDescriptors);
        Directory.CreateDirectory(outputRoot);

        var artifacts = new List<ExportArtifactDescriptor>();

        foreach (dynamic descriptor in entityDescriptors)
        {
            var rows = ((IEnumerable<IReadOnlyDictionary<string, object?>>)MaterializeRows(generationResult, descriptor)).ToList();
            artifacts.Add(_artifactWriter.Write(outputRoot, descriptor.RelativePathStem, descriptor.Columns, rows, ExportArtifactKind.EntityTable));
        }

        foreach (dynamic descriptor in linkDescriptors)
        {
            var rows = ((IEnumerable<IReadOnlyDictionary<string, object?>>)MaterializeRows(generationResult, descriptor)).ToList();
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

        var manifest = _manifestBuilder.Build(new ExportRequest
        {
            Format = request.Format,
            Profile = request.Profile,
            OutputPath = outputRoot,
            ArtifactPrefix = request.ArtifactPrefix,
            IncludeManifest = request.IncludeManifest,
            IncludeSummary = request.IncludeSummary,
            Overwrite = request.Overwrite,
            CredentialExportMode = request.CredentialExportMode,
            ExportedAtUtc = request.ExportedAtUtc
        }, artifacts);

        if (request.IncludeManifest)
        {
            var portableManifest = CreatePortableManifest(manifest);
            var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(portableManifest, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllBytes(Path.Combine(outputRoot, "manifest.json"), manifestBytes);
        }

        return manifest;
    }

    /// <summary>
    /// Decides what happens when the resolved export root already holds content.
    /// </summary>
    /// <remarks>
    /// Because the derived directory name is a function of the export timestamp rather than the wall clock, a
    /// repeated run with identical inputs now resolves to the directory the previous run wrote. Merging the two
    /// exports into one directory would be worse than the old behaviour: it would leave artifacts from the earlier
    /// run in place wherever the later run wrote fewer of them, producing a tree that matches neither export. So a
    /// non-empty root is refused unless the caller asked for <see cref="ExportRequest.Overwrite"/>, and an overwrite
    /// replaces the directory outright rather than writing over part of it. An overwrite is additionally refused
    /// when the root holds anything DataGen did not put there, so that pointing <c>-OutputPath</c> at an occupied
    /// directory cannot silently destroy unrelated files.
    /// </remarks>
    private static void PrepareOutputRoot(
        string outputRoot,
        ExportRequest request,
        IReadOnlyList<object> entityDescriptors,
        IReadOnlyList<object> linkDescriptors)
    {
        if (!Directory.Exists(outputRoot))
        {
            return;
        }

        var existingEntries = Directory.GetFileSystemEntries(outputRoot);
        if (existingEntries.Length == 0)
        {
            return;
        }

        if (!request.Overwrite)
        {
            throw new IOException(
                $"Export root '{outputRoot}' already exists and is not empty. Export directory names are derived from " +
                "the export timestamp, so repeating a run with the same inputs resolves to the same directory. Re-run " +
                "with -Overwrite to replace the previous export, or supply a distinct -ArtifactPrefix or -OutputPath. " +
                "DataGen will not merge two exports into one directory.");
        }

        var ownedNames = GetOwnedTopLevelNames(entityDescriptors, linkDescriptors);
        var foreignNames = existingEntries
            .Select(entry => Path.GetFileName(Path.TrimEndingDirectorySeparator(entry)))
            .Where(name => !ownedNames.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        if (foreignNames.Count > 0)
        {
            throw new IOException(
                $"Refusing to overwrite export root '{outputRoot}': it contains entries DataGen did not export " +
                $"({string.Join(", ", foreignNames)}). Point -OutputPath at a directory used only for exports, or " +
                "remove the unrelated content first.");
        }

        Directory.Delete(outputRoot, recursive: true);
    }

    private static HashSet<string> GetOwnedTopLevelNames(
        IReadOnlyList<object> entityDescriptors,
        IReadOnlyList<object> linkDescriptors)
    {
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "manifest.json",
            "export_summary.json"
        };

        foreach (dynamic descriptor in entityDescriptors)
        {
            AddOwnedTopLevelNames(owned, (string)descriptor.RelativePathStem);
        }

        foreach (dynamic descriptor in linkDescriptors)
        {
            AddOwnedTopLevelNames(owned, (string)descriptor.RelativePathStem);
        }

        return owned;
    }

    private static void AddOwnedTopLevelNames(HashSet<string> owned, string relativePathStem)
    {
        var separatorIndex = relativePathStem.IndexOfAny(['/', '\\']);
        if (separatorIndex >= 0)
        {
            // A nested stem such as "entities/companies" owns the "entities" directory at the root.
            owned.Add(relativePathStem[..separatorIndex]);
            return;
        }

        // A flat stem writes a single file at the root; the writer appends the format extension when absent.
        owned.Add(relativePathStem);
        owned.Add(relativePathStem + ".json");
        owned.Add(relativePathStem + ".csv");
    }

    private static IEnumerable<IReadOnlyDictionary<string, object?>> MaterializeRows(dynamic generationResult, dynamic descriptor)
    {
        var records = descriptor.RecordAccessor(generationResult);
        var sorted = System.Linq.Enumerable.OrderBy(records, descriptor.SortKeySelector, StringComparer.Ordinal);
        foreach (var record in sorted)
        {
            yield return descriptor.RowProjector(record);
        }
    }

    private static ExportManifestV2 CreatePortableManifest(ExportManifestV2 manifest)
        => new()
        {
            ExportId = manifest.ExportId,
            SchemaVersion = manifest.SchemaVersion,
            Format = manifest.Format,
            Profile = manifest.Profile,
            ExportedAtUtc = manifest.ExportedAtUtc,
            OutputPath = ".",
            Artifacts = manifest.Artifacts
        };
}
