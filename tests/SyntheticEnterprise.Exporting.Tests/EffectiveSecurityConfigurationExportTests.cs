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

/// <summary>
/// The per-endpoint effective security configuration reaches a consumer only if the
/// endpoint linkage on the policy object and the canonical key on the setting survive the
/// normalized export. Both columns already exist, so this fixes that they do and that no
/// schema version bump is involved.
/// </summary>
public sealed class EffectiveSecurityConfigurationExportTests
{
    private const string ExpectedSchemaVersion = "2.1.0";

    [Theory]
    [InlineData(ExportSerializationFormat.Json)]
    [InlineData(ExportSerializationFormat.Csv)]
    public void Export_Carries_Endpoint_Linkage_And_Canonical_Key(ExportSerializationFormat format)
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

            var world = new SyntheticEnterpriseWorld();
            world.Policies.Add(new PolicyRecord
            {
                Id = "POL-900",
                CompanyId = "CO-001",
                Name = "WS-EXPORT-001 Local Security Policy",
                PolicyType = "LocalSecurityPolicy",
                Platform = "Windows",
                Category = "EffectiveConfiguration",
                SourceEntityType = "ManagedDevice",
                SourceEntityId = "DEV-001",
            });
            world.PolicySettings.Add(new PolicySettingRecord
            {
                Id = "PST-900",
                CompanyId = "CO-001",
                PolicyId = "POL-900",
                SettingName = "SeTcbPrivilege",
                SettingCategory = "UserRightsAssignment",
                PolicyPath = "UserRight:SeTcbPrivilege",
                ValueType = "String",
                ConfiguredValue = @"BUILTIN\Administrators",
                Source = "SecTemplate",
                Behavior = "RedDot",
                SourceReference = "ExpandedPrincipals",
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

            var policyRow = ReadSingleRow(manifest.OutputPath, "policies", format);
            Assert.Equal("LocalSecurityPolicy", policyRow["policy_type"]);
            Assert.Equal("ManagedDevice", policyRow["source_entity_type"]);
            Assert.Equal("DEV-001", policyRow["source_entity_id"]);

            var settingRow = ReadSingleRow(manifest.OutputPath, "policy_settings", format);
            Assert.Equal("UserRight:SeTcbPrivilege", settingRow["policy_path"]);
            Assert.Equal("POL-900", settingRow["policy_id"]);
            Assert.Equal("SecTemplate", settingRow["source"]);
            Assert.Equal("ExpandedPrincipals", settingRow["source_reference"]);
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    private static Dictionary<string, string?> ReadSingleRow(
        string outputPath,
        string table,
        ExportSerializationFormat format)
    {
        if (format == ExportSerializationFormat.Json)
        {
            using var document = JsonDocument.Parse(
                File.ReadAllBytes(Path.Combine(outputPath, "entities", table + ".json")));
            var row = document.RootElement.EnumerateArray().Single();
            return row.EnumerateObject().ToDictionary(
                property => property.Name,
                property => property.Value.ValueKind switch
                {
                    JsonValueKind.Null => null,
                    JsonValueKind.String => property.Value.GetString(),
                    _ => property.Value.ToString(),
                },
                StringComparer.Ordinal);
        }

        using var parser = new TextFieldParser(Path.Combine(outputPath, "entities", table + ".csv"));
        parser.SetDelimiters(",");
        parser.HasFieldsEnclosedInQuotes = true;
        var headers = parser.ReadFields()!;
        var fields = parser.ReadFields()!;
        Assert.True(parser.EndOfData);
        return headers
            .Zip(fields)
            .ToDictionary(pair => pair.First, pair => (string?)pair.Second, StringComparer.Ordinal);
    }
}
