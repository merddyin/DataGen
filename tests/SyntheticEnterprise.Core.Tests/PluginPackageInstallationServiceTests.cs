using SyntheticEnterprise.Core.Plugins;

namespace SyntheticEnterprise.Core.Tests;

[Collection("PluginEnvironment")]
public sealed class PluginPackageInstallationServiceTests
{
    [Fact]
    public void InstallationService_Copies_And_Registers_Installable_Packages()
    {
        var sourceRoot = Path.Combine(Path.GetTempPath(), $"datagen-plugin-install-source-{Guid.NewGuid():N}");
        var managedRoot = Path.Combine(Path.GetTempPath(), $"datagen-plugin-install-managed-{Guid.NewGuid():N}");
        var registryPath = Path.Combine(Path.GetTempPath(), $"datagen-plugin-install-registry-{Guid.NewGuid():N}.json");
        var originalRegistryPath = Environment.GetEnvironmentVariable("SYNTHETIC_ENTERPRISE_PLUGIN_REGISTRY_PATH");
        var originalManagedRoot = Environment.GetEnvironmentVariable("SYNTHETIC_ENTERPRISE_MANAGED_PLUGIN_ROOT");

        Directory.CreateDirectory(sourceRoot);

        try
        {
            Environment.SetEnvironmentVariable("SYNTHETIC_ENTERPRISE_PLUGIN_REGISTRY_PATH", registryPath);
            Environment.SetEnvironmentVariable("SYNTHETIC_ENTERPRISE_MANAGED_PLUGIN_ROOT", managedRoot);

            File.WriteAllText(Path.Combine(sourceRoot, "country-tax-ids.generator.json"), """
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
            File.WriteAllText(Path.Combine(sourceRoot, "country-tax-ids.plugin.ps1"), "New-PluginResult -Records @() -Warnings @()");

            var securityPolicy = new DataOnlyGenerationPluginSecurityPolicy();
            var catalog = new FileSystemExternalGenerationPluginCatalog(
                new GenerationPluginManifestValidator(securityPolicy),
                securityPolicy,
                new AllowListExternalPluginTrustPolicy());
            var store = new JsonGenerationPluginRegistrationStore();
            var registrationService = new GenerationPluginRegistrationService(catalog, store);
            var validator = new GenerationPluginPackageValidator(catalog);
            var installer = new GenerationPluginInstallationService(validator, registrationService);

            var result = installer.Install(new[] { sourceRoot }, allowAssemblyPlugins: false);

            Assert.Equal(Path.GetFullPath(managedRoot), result.ManagedRootPath);
            var installedPath = Assert.Single(result.InstalledPaths);
            Assert.True(Directory.Exists(installedPath));
            Assert.True(File.Exists(Path.Combine(installedPath, "country-tax-ids.generator.json")));
            Assert.Single(result.Registered);
            Assert.Equal("CountryTaxIds", result.Registered[0].Capability);
            Assert.True(File.Exists(store.StoragePath));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SYNTHETIC_ENTERPRISE_PLUGIN_REGISTRY_PATH", originalRegistryPath);
            Environment.SetEnvironmentVariable("SYNTHETIC_ENTERPRISE_MANAGED_PLUGIN_ROOT", originalManagedRoot);
            if (Directory.Exists(sourceRoot))
            {
                Directory.Delete(sourceRoot, recursive: true);
            }

            if (Directory.Exists(managedRoot))
            {
                Directory.Delete(managedRoot, recursive: true);
            }

            if (File.Exists(registryPath))
            {
                for (var attempt = 0; attempt < 5; attempt++)
                {
                    try
                    {
                        File.Delete(registryPath);
                        break;
                    }
                    catch (IOException)
                    {
                        if (attempt == 4)
                        {
                            break;
                        }

                        Thread.Sleep(200);
                    }
                }
            }
        }
    }
}
