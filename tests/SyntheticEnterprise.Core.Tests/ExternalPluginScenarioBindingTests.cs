using Microsoft.Extensions.DependencyInjection;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Plugins;
using SyntheticEnterprise.Contracts.Scenarios;
using SyntheticEnterprise.Core.DependencyInjection;
using SyntheticEnterprise.Core.Plugins;
using SyntheticEnterprise.Core.Scenarios;

namespace SyntheticEnterprise.Core.Tests;

public sealed class ExternalPluginScenarioBindingTests
{
    [Fact]
    public void ScenarioDefaultsResolver_Preserves_ExternalPlugin_Profile()
    {
        var resolver = new ScenarioDefaultsResolver();

        var scenario = resolver.Resolve(new ScenarioEnvelope
        {
            Name = "Plugin Envelope",
            IndustryProfile = "Technology",
            GeographyProfile = "Regional-US",
            CompanyCount = 1,
            ExternalPlugins = new ExternalPluginScenarioProfile
            {
                PluginRootPaths = new() { @"C:\plugins" },
                EnabledCapabilities = new() { "TaxIdentifiers" },
                CapabilityConfigurations = new()
                {
                    new ExternalPluginCapabilityConfiguration
                    {
                        Capability = "TaxIdentifiers",
                        Settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["Country"] = "US"
                        }
                    }
                }
            }
        });

        Assert.Equal(@"C:\plugins", Assert.Single(scenario.ExternalPlugins.PluginRootPaths));
        Assert.Equal("TaxIdentifiers", Assert.Single(scenario.ExternalPlugins.EnabledCapabilities));
        var configuration = Assert.Single(scenario.ExternalPlugins.CapabilityConfigurations);
        Assert.Equal("US", configuration.Settings["Country"]);
    }

    [Fact]
    public void ScenarioBindingService_Merges_Scenario_Profile_And_Overrides()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();
        var binder = services.GetRequiredService<IExternalPluginScenarioBindingService>();

        var settings = binder.Bind(
            new ExternalPluginScenarioProfile
            {
                PluginRootPaths = new() { @"C:\plugins\base" },
                EnabledCapabilities = new() { "RootPlugin" },
                CapabilityConfigurations = new()
                {
                    new ExternalPluginCapabilityConfiguration
                    {
                        Capability = "RootPlugin",
                        Settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["Mode"] = "Base",
                            ["Region"] = "US"
                        }
                    }
                },
                ExecutionTimeoutSeconds = 20,
                AllowAssemblyPlugins = false
            },
            new ExternalPluginExecutionOverrides
            {
                PluginRootPaths = new() { @"C:\plugins\override" },
                EnabledCapabilities = new() { "ChildPlugin" },
                CapabilityConfigurations = new()
                {
                    new ExternalPluginCapabilityConfiguration
                    {
                        Capability = "RootPlugin",
                        Settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["Mode"] = "Override"
                        }
                    }
                },
                ExecutionTimeoutSeconds = 5,
                AllowAssemblyPlugins = true
            });

        Assert.True(settings.Enabled);
        Assert.Contains(@"C:\plugins\base", settings.PluginRootPaths);
        Assert.Contains(@"C:\plugins\override", settings.PluginRootPaths);
        Assert.Contains("RootPlugin", settings.EnabledCapabilities);
        Assert.Contains("ChildPlugin", settings.EnabledCapabilities);
        Assert.True(settings.AllowAssemblyPlugins);
        Assert.Equal(5, settings.ExecutionTimeoutSeconds);
        var configuration = Assert.Single(settings.CapabilityConfigurations, item => item.Capability == "RootPlugin");
        Assert.Equal("Override", configuration.Settings["Mode"]);
        Assert.Equal("US", configuration.Settings["Region"]);
    }
}
