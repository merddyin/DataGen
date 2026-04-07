using System.Collections.Generic;
using SyntheticEnterprise.Exporting.Contracts;
using SyntheticEnterprise.Exporting.Services;
using Xunit;

namespace SyntheticEnterprise.Exporting.Tests;

public sealed class ExportManifestBuilderTests
{
    [Fact]
    public void Build_Assigns_Format_Profile_And_Artifacts()
    {
        var builder = new ExportManifestBuilder();
        var request = new ExportRequest
        {
            Format = ExportSerializationFormat.Csv,
            Profile = ExportProfileKind.Normalized,
            OutputPath = "out"
        };

        var artifacts = new List<ExportArtifactDescriptor>
        {
            new()
            {
                LogicalName = "people",
                RelativePath = "people.csv",
                ArtifactKind = ExportArtifactKind.EntityTable,
                MediaType = "text/csv",
                RowCount = 10,
                Sha256 = "abc",
                SizeBytes = 100,
                Columns = new[] { "person_id", "display_name" }
            }
        };

        var manifest = builder.Build(request, artifacts);

        Assert.Equal(ExportSerializationFormat.Csv, manifest.Format);
        Assert.Equal(ExportProfileKind.Normalized, manifest.Profile);
        Assert.Single(manifest.Artifacts);
    }
}
