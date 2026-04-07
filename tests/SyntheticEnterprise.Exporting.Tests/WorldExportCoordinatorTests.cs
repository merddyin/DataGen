using System.Collections.Generic;
using System.IO;
using SyntheticEnterprise.Exporting.Contracts;
using SyntheticEnterprise.Exporting.Services;
using SyntheticEnterprise.Exporting.Writers;
using Xunit;

namespace SyntheticEnterprise.Exporting.Tests;

public sealed class WorldExportCoordinatorTests
{
    [Fact]
    public void Export_Writes_Summary_And_Returns_Manifest()
    {
        var temp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(temp);

        try
        {
            var coordinator = new WorldExportCoordinator(
                new EmptyEntityTableProvider(),
                new EmptyLinkTableProvider(),
                new JsonArtifactWriter(),
                new ExportManifestBuilder(),
                new ExportSummaryBuilder(),
                new ExportPathResolver());

            var manifest = coordinator.Export(new { }, new ExportRequest
            {
                Format = ExportSerializationFormat.Json,
                OutputPath = temp,
                IncludeManifest = true,
                IncludeSummary = true
            });

            Assert.NotNull(manifest);
            Assert.Contains(manifest.Artifacts, a => a.ArtifactKind == ExportArtifactKind.Summary);
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    private sealed class EmptyEntityTableProvider : IEntityTableProvider
    {
        public IReadOnlyList<object> GetDescriptors() => [];
    }

    private sealed class EmptyLinkTableProvider : ILinkTableProvider
    {
        public IReadOnlyList<object> GetDescriptors() => [];
    }
}
