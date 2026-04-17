using SyntheticEnterprise.Contracts.Plugins;
using SyntheticEnterprise.Core.Plugins;

namespace SyntheticEnterprise.Core.Tests;

[Collection("PluginEnvironment")]
public sealed class PluginRegistrationServiceTests
{
    [Fact]
    public void RegistrationService_Can_Register_Apply_And_Unregister_Plugins()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"datagen-plugin-registration-{Guid.NewGuid():N}");
        var registryPath = Path.Combine(tempRoot, "plugin-registrations.json");
        Directory.CreateDirectory(tempRoot);
        var originalRegistryPath = Environment.GetEnvironmentVariable("SYNTHETIC_ENTERPRISE_PLUGIN_REGISTRY_PATH");

        try
        {
            Environment.SetEnvironmentVariable("SYNTHETIC_ENTERPRISE_PLUGIN_REGISTRY_PATH", registryPath);
            File.WriteAllText(Path.Combine(tempRoot, "country-tax-ids.generator.json"), """
                {
                  "capability": "CountryTaxIds",
                  "displayName": "Country Tax IDs",
                  "executionMode": "PowerShellScript",
                  "entryPoint": "country-tax-ids.plugin.ps1",
                  "security": {
                    "dataOnly": true,
                    "requestedCapabilities": [ "GenerateData" ]
                  }
                }
                """);
            File.WriteAllText(Path.Combine(tempRoot, "country-tax-ids.plugin.ps1"), "New-PluginResult -Records @() -Warnings @()");

            var securityPolicy = new DataOnlyGenerationPluginSecurityPolicy();
            var catalog = new FileSystemExternalGenerationPluginCatalog(
                new GenerationPluginManifestValidator(securityPolicy),
                securityPolicy,
                new AllowListExternalPluginTrustPolicy());
            var store = new JsonGenerationPluginRegistrationStore();
            var service = new GenerationPluginRegistrationService(catalog, store);

            var registration = service.Register(new[] { tempRoot }, allowAssemblyPlugins: false);
            var registeredPlugin = Assert.Single(registration.Registered);
            Assert.Equal("CountryTaxIds", registeredPlugin.Capability);
            Assert.True(File.Exists(store.StoragePath));

            var applied = service.ApplyRegistrations(new ExternalPluginExecutionSettings
            {
                EnabledCapabilities = new() { "CountryTaxIds" }
            }, includeAllRegisteredCapabilities: false);

            Assert.Contains(Path.GetFullPath(tempRoot), applied.PluginRootPaths);
            Assert.Contains("CountryTaxIds", applied.EnabledCapabilities);
            Assert.Contains(registeredPlugin.ContentHash, applied.AllowedContentHashes);
            Assert.True(applied.RequireContentHashAllowList);

            var removed = service.Unregister(new[] { "CountryTaxIds" }, rootPaths: null);
            Assert.Equal(1, removed);
            Assert.Empty(service.GetAll());
        }
        finally
        {
            Environment.SetEnvironmentVariable("SYNTHETIC_ENTERPRISE_PLUGIN_REGISTRY_PATH", originalRegistryPath);
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
