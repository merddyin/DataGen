using Microsoft.Extensions.DependencyInjection;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Plugins;
using SyntheticEnterprise.Core.Abstractions;
using SyntheticEnterprise.Core.DependencyInjection;

namespace SyntheticEnterprise.Core.Tests;

public sealed class PolicySettingPluginBoundaryTests
{
    [Fact]
    public void WorldGenerator_CompletesAndValidatesPolicyTimestampsBeforeExternalPluginExecution()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"datagen-policy-plugin-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            File.WriteAllText(Path.Combine(tempRoot, "policytimestampaudit.generator.json"), """
                {
                  "capability": "PolicyTimestampAudit",
                  "displayName": "Policy Timestamp Audit Plugin",
                  "executionMode": "PowerShellScript",
                  "entryPoint": "policytimestampaudit.plugin.ps1",
                  "security": {
                    "dataOnly": true,
                    "requestedCapabilities": [ "GenerateData" ]
                  }
                }
                """);
            File.WriteAllText(Path.Combine(tempRoot, "policytimestampaudit.plugin.ps1"), """
                $settings = @($InputWorld.PolicySettings)
                $invalid = @($settings | Where-Object {
                  $null -eq $_.WhenCreated -or
                  $null -eq $_.WhenModified -or
                  $null -eq $_.ObservedAtUtc -or
                  $null -eq $_.RetrievedAtUtc -or
                  $_.WhenCreated -gt $_.WhenModified -or
                  $_.WhenModified -gt $_.ObservedAtUtc -or
                  $_.ObservedAtUtc -gt $_.RetrievedAtUtc
                })

                New-PluginResult -Records @(
                  (New-PluginRecord -RecordType 'PolicyTimestampAudit' -AssociatedEntityType 'Company' -AssociatedEntityId $InputWorld.Companies[0].Id -Properties @{
                    SettingCount = [string]$settings.Count
                    CompletedAndOrdered = [string]($invalid.Count -eq 0)
                  })
                ) -Warnings @()
                """);

            using var services = new ServiceCollection()
                .AddSyntheticEnterpriseCore()
                .BuildServiceProvider();
            var generator = services.GetRequiredService<IWorldGenerator>();
            var result = generator.Generate(
                new GenerationContext
                {
                    Scenario = new ScenarioDefinition
                    {
                        Name = "Policy Timestamp Plugin Co",
                        Companies = new()
                        {
                            new ScenarioCompanyDefinition
                            {
                                Name = "Policy Timestamp Plugin Co",
                                Industry = "Technology",
                                EmployeeCount = 2,
                                OfficeCount = 1,
                                Countries = new() { "United States" },
                            },
                        },
                    },
                    Seed = 1130,
                    GeneratedAt = DateTimeOffset.Parse("2026-07-22T00:00:00Z"),
                    ExternalPlugins = new ExternalPluginExecutionSettings
                    {
                        Enabled = true,
                        PluginRootPaths = new() { tempRoot },
                        EnabledCapabilities = new() { "PolicyTimestampAudit" },
                    },
                },
                new CatalogSet());

            var record = Assert.Single(result.World.PluginRecords, record =>
                record.PluginCapability == "PolicyTimestampAudit");
            Assert.NotEqual("0", record.Properties["SettingCount"]);
            Assert.Equal("True", record.Properties["CompletedAndOrdered"]);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}
