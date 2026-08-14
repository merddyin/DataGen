using System.Globalization;
using System.Text.Json;
using Microsoft.VisualBasic.FileIO;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Exporting.Contracts;
using SyntheticEnterprise.Exporting.Profiles;
using SyntheticEnterprise.Exporting.Services;
using SyntheticEnterprise.Exporting.Writers;
using Xunit;

namespace SyntheticEnterprise.Exporting.Tests;

public sealed class PolicySettingTimestampExportTests
{
    private const string ExpectedSchemaVersion = "2.1.0";

    [Theory]
    [InlineData(ExportSerializationFormat.Json)]
    [InlineData(ExportSerializationFormat.Csv)]
    public void Export_ParsesPolicyTimestampsAndManifestSchemaVersion(ExportSerializationFormat format)
    {
        var temp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(temp);

        try
        {
            var coordinator = new WorldExportCoordinator(
                new NormalizedEntityTableProvider(),
                new NormalizedLinkTableProvider(),
                format == ExportSerializationFormat.Json ? new JsonArtifactWriter() : new CsvArtifactWriter(),
                new ExportManifestBuilder(),
                new ExportSummaryBuilder(),
                new ExportPathResolver());
            var expected = new DateTimeOffset[]
            {
                DateTimeOffset.Parse("2025-01-10T08:00:00Z"),
                DateTimeOffset.Parse("2025-06-15T09:30:00Z"),
                DateTimeOffset.Parse("2026-07-20T14:00:00Z"),
                DateTimeOffset.Parse("2026-07-20T14:05:00Z"),
            };
            var world = new SyntheticEnterpriseWorld();
            world.PolicySettings.Add(new PolicySettingRecord
            {
                Id = "PST-001",
                CompanyId = "CO-001",
                PolicyId = "POL-001",
                SettingName = "RequireEncryption",
                WhenCreated = expected[0],
                WhenModified = expected[1],
                ObservedAtUtc = expected[2],
                RetrievedAtUtc = expected[3],
            });

            var manifest = coordinator.Export(
                new GenerationResult { World = world, Statistics = new GenerationStatistics() },
                new ExportRequest
                {
                    Format = format,
                    OutputPath = temp,
                    IncludeManifest = true,
                });

            Assert.Equal(ExpectedSchemaVersion, manifest.SchemaVersion);
            using var manifestJson = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(manifest.OutputPath, "manifest.json")));
            Assert.Equal(ExpectedSchemaVersion, manifestJson.RootElement.GetProperty("SchemaVersion").GetString());

            var actual = format == ExportSerializationFormat.Json
                ? ReadJsonTimestamps(Path.Combine(manifest.OutputPath, "entities", "policy_settings.json"))
                : ReadCsvTimestamps(Path.Combine(manifest.OutputPath, "entities", "policy_settings.csv"));
            Assert.Equal(expected, actual);
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    private static DateTimeOffset[] ReadJsonTimestamps(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var row = document.RootElement.EnumerateArray().Single();
        return TimestampColumns
            .Select(column => DateTimeOffset.Parse(row.GetProperty(column).GetString()!, CultureInfo.InvariantCulture))
            .ToArray();
    }

    private static DateTimeOffset[] ReadCsvTimestamps(string path)
    {
        using var parser = new TextFieldParser(path);
        parser.SetDelimiters(",");
        parser.HasFieldsEnclosedInQuotes = true;
        var headers = parser.ReadFields()!;
        var fields = parser.ReadFields()!;
        var row = headers.Zip(fields).ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.Ordinal);
        Assert.True(parser.EndOfData);
        return TimestampColumns
            .Select(column => DateTimeOffset.Parse(row[column], CultureInfo.InvariantCulture))
            .ToArray();
    }

    private static readonly string[] TimestampColumns =
    [
        "when_created",
        "when_modified",
        "observed_at",
        "retrieved_at",
    ];
}
