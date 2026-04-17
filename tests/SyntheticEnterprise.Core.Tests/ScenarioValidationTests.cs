using Microsoft.Extensions.DependencyInjection;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Plugins;
using SyntheticEnterprise.Contracts.Scenarios;
using SyntheticEnterprise.Core.DependencyInjection;
using SyntheticEnterprise.Core.Scenarios;

namespace SyntheticEnterprise.Core.Tests;

public sealed class ScenarioValidationTests
{
    [Fact]
    public void Resolver_Preserves_Root_Deviation_Profile()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();
        var resolver = services.GetRequiredService<IScenarioDefaultsResolver>();

        var scenario = resolver.Resolve(new ScenarioEnvelope
        {
            Name = "Deviation Scenario",
            DeviationProfile = ScenarioDeviationProfiles.Clean
        });

        Assert.Equal(ScenarioDeviationProfiles.Clean, scenario.DeviationProfile);
    }

    [Fact]
    public void Resolver_Preserves_Expanded_Generation_Profiles()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();
        var resolver = services.GetRequiredService<IScenarioDefaultsResolver>();

        var scenario = resolver.Resolve(new ScenarioEnvelope
        {
            Name = "Expanded Scenario",
            DeviationProfile = ScenarioDeviationProfiles.Aggressive,
            Applications = new ApplicationProfile
            {
                IncludeApplications = true,
                BaseApplicationCount = 12,
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
                CoverageRatio = 0.83
            }
        });

        Assert.True(scenario.Applications.IncludeApplications);
        Assert.Equal(12, scenario.Applications.BaseApplicationCount);
        Assert.False(scenario.Applications.IncludeSaaSApplications);
        Assert.True(scenario.Cmdb.IncludeConfigurationManagement);
        Assert.False(scenario.Cmdb.IncludeCloudServices);
        Assert.Equal(ScenarioDeviationProfiles.Clean, scenario.Cmdb.DeviationProfile);
        Assert.True(scenario.ObservedData.IncludeObservedViews);
        Assert.Equal(0.83, scenario.ObservedData.CoverageRatio);
    }

    [Fact]
    public void Validator_Rejects_Unknown_Deviation_Profile()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();
        var validator = services.GetRequiredService<IScenarioValidator>();

        var result = validator.Validate(new ScenarioEnvelope
        {
            Name = "Invalid Deviation Scenario",
            DeviationProfile = "WildWest"
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Messages, message => message.Code == "deviation-profile");
    }

    [Fact]
    public void Resolver_Applies_Plugin_Default_Settings_To_Resolved_Scenario()
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

            var services = new ServiceCollection()
                .AddSyntheticEnterpriseCore()
                .BuildServiceProvider();
            var resolver = services.GetRequiredService<IScenarioDefaultsResolver>();
            var hydrator = services.GetRequiredService<IScenarioPluginProfileHydrator>();

            var scenario = hydrator.Hydrate(resolver.Resolve(new ScenarioEnvelope
            {
                Name = "Defaulted Plugin Scenario",
                ExternalPlugins = new ExternalPluginScenarioProfile
                {
                    PluginRootPaths = new() { tempRoot },
                    EnabledCapabilities = new() { "TaxIdentifiers" }
                }
            })).Scenario;

            var configuration = Assert.Single(scenario.ExternalPlugins.CapabilityConfigurations);
            Assert.Equal("TaxIdentifiers", configuration.Capability);
            Assert.Equal("US", configuration.Settings["Region"]);
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
    public void Validator_Exposes_Plugin_Contributions_And_Warns_On_Unknown_Settings()
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
                      "required": true
                    },
                    {
                      "name": "Profile",
                      "typeName": "System.String"
                    }
                  ]
                }
                """);
            File.WriteAllText(Path.Combine(tempRoot, "tax.plugin.ps1"), "param()");

            var services = new ServiceCollection()
                .AddSyntheticEnterpriseCore()
                .BuildServiceProvider();
            var validator = services.GetRequiredService<IScenarioValidator>();

            var result = validator.Validate(new ScenarioEnvelope
            {
                Name = "Plugin-Aware Scenario",
                ExternalPlugins = new ExternalPluginScenarioProfile
                {
                    PluginRootPaths = new() { tempRoot },
                    EnabledCapabilities = new() { "TaxIdentifiers" },
                    CapabilityConfigurations = new()
                    {
                        new ExternalPluginCapabilityConfiguration
                        {
                            Capability = "TaxIdentifiers",
                            Settings = new(StringComparer.OrdinalIgnoreCase)
                            {
                                ["Region"] = "US",
                                ["UnknownSetting"] = "Value"
                            }
                        }
                    }
                }
            });

            Assert.True(result.IsValid);
            var contribution = Assert.Single(result.Contributions);
            Assert.Equal("TaxIdentifiers", contribution.Capability);
            Assert.Equal(new[] { "Region", "Profile" }, contribution.Parameters.Select(parameter => parameter.Name).ToArray());
            var hint = Assert.Single(result.AuthoringHints);
            Assert.Equal("TaxIdentifiers", hint.Capability);
            Assert.Equal("US", hint.SuggestedSettings["Region"]);
            Assert.Equal(new[] { "Region", "Profile" }, hint.Parameters.Select(parameter => parameter.Name).ToArray());
            Assert.Contains(result.Messages, message => message.Code == "external-plugin-unknown-setting");
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
    public void Validator_Rejects_Missing_Required_Plugin_Settings()
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
                      "required": true
                    }
                  ]
                }
                """);
            File.WriteAllText(Path.Combine(tempRoot, "tax.plugin.ps1"), "param()");

            var services = new ServiceCollection()
                .AddSyntheticEnterpriseCore()
                .BuildServiceProvider();
            var validator = services.GetRequiredService<IScenarioValidator>();

            var result = validator.Validate(new ScenarioEnvelope
            {
                Name = "Plugin Validation Error",
                ExternalPlugins = new ExternalPluginScenarioProfile
                {
                    PluginRootPaths = new() { tempRoot },
                    EnabledCapabilities = new() { "TaxIdentifiers" }
                }
            });

            Assert.False(result.IsValid);
            Assert.Contains(result.Messages, message => message.Code == "external-plugin-missing-required-setting");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"datagen-scenario-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
