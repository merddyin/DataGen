using System.Management.Automation;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Plugins;
using SyntheticEnterprise.Contracts.Scenarios;
using SyntheticEnterprise.PowerShell.Cmdlets;

namespace SyntheticEnterprise.Tests;

public sealed class ScenarioAuthoringCmdletIntegrationTests
{
    [Fact]
    public void GetSEScenarioTemplate_Returns_Plugin_Authoring_Hints()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            File.WriteAllText(Path.Combine(tempRoot, "tax.generator.json"), """
                {
                  "capability": "TaxIdentifiers",
                  "displayName": "Tax Identifiers",
                  "executionMode": "PowerShellScript",
                  "entryPoint": "tax.plugin.ps1",
                  "parameters": [
                    {
                      "name": "Region",
                      "typeName": "System.String",
                      "required": true,
                      "defaultValue": "US"
                    }
                  ]
                }
                """);
            File.WriteAllText(Path.Combine(tempRoot, "tax.plugin.ps1"), "param()");

            using var powershell = System.Management.Automation.PowerShell.Create();
            powershell.AddCommand("Import-Module")
                .AddParameter("Name", typeof(NewSEEnterpriseWorldCommand).Assembly.Location);
            powershell.Invoke();
            Assert.False(powershell.HadErrors);

            powershell.Commands.Clear();
            powershell.AddCommand("Get-SEScenarioTemplate")
                .AddParameter("PluginRootPath", tempRoot)
                .AddParameter("EnablePluginCapability", "TaxIdentifiers");

            var results = powershell.Invoke<ScenarioTemplateDescriptor>();

            Assert.False(powershell.HadErrors);
            var template = Assert.Single(results, item => item.Kind == ScenarioTemplateKind.RegionalManufacturer);
            var hint = Assert.Single(template.PluginAuthoringHints, item => item.Capability == "TaxIdentifiers");
            Assert.Equal("US", hint.SuggestedSettings["Region"]);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void NewSEScenarioFromTemplate_Hydrates_Plugin_Defaults_Into_Envelope()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            File.WriteAllText(Path.Combine(tempRoot, "tax.generator.json"), """
                {
                  "capability": "TaxIdentifiers",
                  "displayName": "Tax Identifiers",
                  "executionMode": "PowerShellScript",
                  "entryPoint": "tax.plugin.ps1",
                  "parameters": [
                    {
                      "name": "Region",
                      "typeName": "System.String",
                      "required": true,
                      "defaultValue": "US"
                    }
                  ]
                }
                """);
            File.WriteAllText(Path.Combine(tempRoot, "tax.plugin.ps1"), "param()");

            using var powershell = System.Management.Automation.PowerShell.Create();
            powershell.AddCommand("Import-Module")
                .AddParameter("Name", typeof(NewSEEnterpriseWorldCommand).Assembly.Location);
            powershell.Invoke();
            Assert.False(powershell.HadErrors);

            powershell.Commands.Clear();
            powershell.AddCommand("New-SEScenarioFromTemplate")
                .AddParameter("Template", ScenarioTemplateKind.RegionalManufacturer)
                .AddParameter("PluginRootPath", tempRoot)
                .AddParameter("EnablePluginCapability", "TaxIdentifiers");

            var result = Assert.Single(powershell.Invoke<ScenarioEnvelope>());

            Assert.False(powershell.HadErrors);
            var configuration = Assert.Single(result.ExternalPlugins!.CapabilityConfigurations);
            Assert.Equal("TaxIdentifiers", configuration.Capability);
            Assert.Equal("US", configuration.Settings["Region"]);
            Assert.NotNull(result.Applications);
            Assert.NotNull(result.Cmdb);
            Assert.NotNull(result.ObservedData);
            Assert.Equal(ScenarioDeviationProfiles.Realistic, result.DeviationProfile);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void ResolveSEScenario_Preserves_Expanded_Generation_Profiles()
    {
        using var powershell = System.Management.Automation.PowerShell.Create();
        powershell.AddCommand("Import-Module")
            .AddParameter("Name", typeof(NewSEEnterpriseWorldCommand).Assembly.Location);
        powershell.Invoke();
        Assert.False(powershell.HadErrors);

        powershell.Commands.Clear();
        powershell.AddCommand("Resolve-SEScenario")
            .AddParameter("Scenario", new ScenarioEnvelope
            {
                Name = "Expanded Scenario",
                Applications = new ApplicationProfile
                {
                    IncludeApplications = true,
                    BaseApplicationCount = 10,
                    IncludeLineOfBusinessApplications = true,
                    IncludeSaaSApplications = false
                },
                Cmdb = new CmdbProfile
                {
                    IncludeConfigurationManagement = true,
                    IncludeBusinessServices = true,
                    IncludeCloudServices = false,
                    IncludeAutoDiscoveryRecords = true,
                    IncludeServiceCatalogRecords = false,
                    IncludeSpreadsheetImportRecords = true,
                    DeviationProfile = ScenarioDeviationProfiles.Clean
                },
                ObservedData = new ObservedDataProfile
                {
                    IncludeObservedViews = true,
                    CoverageRatio = 0.91
                }
            });

        var result = Assert.Single(powershell.Invoke<ScenarioDefinition>());

        Assert.False(powershell.HadErrors);
        Assert.Equal(10, result.Applications.BaseApplicationCount);
        Assert.False(result.Applications.IncludeSaaSApplications);
        Assert.True(result.Cmdb.IncludeConfigurationManagement);
        Assert.False(result.Cmdb.IncludeCloudServices);
        Assert.Equal(ScenarioDeviationProfiles.Clean, result.Cmdb.DeviationProfile);
        Assert.True(result.ObservedData.IncludeObservedViews);
        Assert.Equal(0.91, result.ObservedData.CoverageRatio);
    }

    [Fact]
    public void ResolveSEScenario_Maps_Bundled_Packs_Into_Resolved_Plugin_Profile()
    {
        using var powershell = System.Management.Automation.PowerShell.Create();
        powershell.AddCommand("Import-Module")
            .AddParameter("Name", typeof(NewSEEnterpriseWorldCommand).Assembly.Location);
        powershell.Invoke();
        Assert.False(powershell.HadErrors);

        powershell.Commands.Clear();
        powershell.AddCommand("Resolve-SEScenario")
            .AddParameter("Scenario", new ScenarioEnvelope
            {
                Name = "Pack Scenario",
                Packs = new ScenarioPackProfile
                {
                    IncludeBundledPacks = true,
                    EnabledPacks =
                    {
                        new ScenarioPackSelection
                        {
                            PackId = "FirstParty.ITSM",
                            Settings = new(StringComparer.OrdinalIgnoreCase)
                            {
                                ["TicketCount"] = "11"
                            }
                        }
                    }
                }
            });

        var result = Assert.Single(powershell.Invoke<ScenarioDefinition>());

        Assert.False(powershell.HadErrors);
        Assert.Contains(
            result.ExternalPlugins.PluginRootPaths,
            path => path.EndsWith(Path.Combine("packs", "first-party"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains("FirstParty.ITSM", result.ExternalPlugins.EnabledCapabilities);
        Assert.Equal("11", Assert.Single(result.ExternalPlugins.CapabilityConfigurations).Settings["TicketCount"]);
    }

    [Fact]
    public void ResolveSEScenario_Preserves_Timeline_Profile()
    {
        using var powershell = System.Management.Automation.PowerShell.Create();
        powershell.AddCommand("Import-Module")
            .AddParameter("Name", typeof(NewSEEnterpriseWorldCommand).Assembly.Location);
        powershell.Invoke();
        Assert.False(powershell.HadErrors);

        powershell.Commands.Clear();
        powershell.AddCommand("Resolve-SEScenario")
            .AddParameter("Scenario", new ScenarioEnvelope
            {
                Name = "Timeline Scenario",
                Timeline = new TimelineProfile
                {
                    Enabled = true,
                    StartAtUtc = "2026-02-01T00:00:00Z",
                    DurationDays = 45,
                    SnapshotDays = new() { 0, 20, 45 }
                }
            });

        var result = Assert.Single(powershell.Invoke<ScenarioDefinition>());

        Assert.False(powershell.HadErrors);
        Assert.True(result.Timeline.Enabled);
        Assert.Equal("2026-02-01T00:00:00Z", result.Timeline.StartAtUtc);
        Assert.Equal(45, result.Timeline.DurationDays);
        Assert.Equal(new[] { 0, 20, 45 }, result.Timeline.SnapshotDays);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"datagen-scenario-cmdlet-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
