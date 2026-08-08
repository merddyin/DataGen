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

    [Fact]
    public void Build_AssignsTheSameExportIdForEquivalentPayloadsAtDifferentOutputPaths()
    {
        var builder = new ExportManifestBuilder();
        var artifacts = new List<ExportArtifactDescriptor>
        {
            new()
            {
                LogicalName = "people",
                RelativePath = "people.json",
                ArtifactKind = ExportArtifactKind.EntityTable,
                MediaType = "application/json",
                RowCount = 10,
                Sha256 = "abc",
                SizeBytes = 100,
                Columns = new[] { "person_id" }
            }
        };
        var first = builder.Build(new ExportRequest
        {
            Format = ExportSerializationFormat.Json,
            Profile = ExportProfileKind.Normalized,
            OutputPath = "first",
            ExportedAtUtc = DateTimeOffset.Parse("2026-07-21T19:00:00-05:00")
        }, artifacts);
        var second = builder.Build(new ExportRequest
        {
            Format = ExportSerializationFormat.Json,
            Profile = ExportProfileKind.Normalized,
            OutputPath = "second",
            ExportedAtUtc = DateTimeOffset.Parse("2026-07-21T19:00:00-05:00")
        }, artifacts);

        Assert.Equal(first.ExportId, second.ExportId);
    }

    [Fact]
    public void Build_UsesUnambiguousFramingForArtifactColumns()
    {
        var builder = new ExportManifestBuilder();
        var request = new ExportRequest
        {
            Format = ExportSerializationFormat.Json,
            Profile = ExportProfileKind.Normalized,
            OutputPath = ".",
            ExportedAtUtc = DateTimeOffset.Parse("2026-07-22T00:00:00Z")
        };

        var singleColumn = builder.Build(request, [CreateArtifact(["a,b"])]);
        var twoColumns = builder.Build(request, [CreateArtifact(["a", "b"])]);

        Assert.NotEqual(singleColumn.ExportId, twoColumns.ExportId);
    }

    private static ExportArtifactDescriptor CreateArtifact(IReadOnlyList<string> columns)
        => new()
        {
            LogicalName = "people",
            RelativePath = "people.json",
            ArtifactKind = ExportArtifactKind.EntityTable,
            MediaType = "application/json",
            RowCount = 10,
            Sha256 = "abc",
            SizeBytes = 100,
            Columns = columns
        };
}
