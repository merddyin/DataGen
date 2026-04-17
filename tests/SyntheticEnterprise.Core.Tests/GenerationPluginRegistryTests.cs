using Microsoft.Extensions.DependencyInjection;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Contracts.Plugins;
using SyntheticEnterprise.Core.DependencyInjection;
using SyntheticEnterprise.Core.Plugins;

namespace SyntheticEnterprise.Core.Tests;

public sealed class GenerationPluginRegistryTests
{
    [Fact]
    public void Discover_Loads_Legacy_And_Json_Plugin_Manifests()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            var legacyRoot = Path.Combine(tempRoot, "address.Generator");
            Directory.CreateDirectory(Path.Combine(legacyRoot, "data"));
            File.WriteAllText(Path.Combine(legacyRoot, "address.Generator.psd1"), """
                @{
                    RootModule = 'address.Generator.psm1'
                    FunctionsToExport = 'address'
                    PrivateData = @{
                        PSData = @{
                            FriendlyName = 'address'
                            LocalDataFiles = @('data\countries.csv')
                            GeneratorType = 'mid'
                            DependsOn = @('city.Generator','word.Generator')
                        }
                    }
                }
                """);
            File.WriteAllText(Path.Combine(legacyRoot, "address.Generator.psm1"), "function address { }");
            File.WriteAllText(Path.Combine(legacyRoot, "data", "countries.csv"), "Name,Code\nUnited States,US\n");

            File.WriteAllText(Path.Combine(tempRoot, "business.generator.json"), """
                {
                  "capability": "Business",
                  "displayName": "Business Plugin",
                  "description": "Adds business-level generation metadata.",
                  "pluginKind": "Manifest",
                  "executionMode": "PowerShellScript",
                  "entryPoint": "business.plugin.ps1",
                  "security": {
                    "dataOnly": true,
                    "requestedCapabilities": [ "GenerateData", "ReadPluginData" ]
                  },
                  "dependencies": [ "address" ],
                  "localDataPaths": [ "data/industries.csv" ],
                  "parameters": [
                    {
                      "name": "Region",
                      "typeName": "System.String",
                      "helpText": "Target region."
                    }
                  ],
                  "metadata": {
                    "schemaKey": "plugins.business.region"
                  }
                }
                """);
            NewPluginFile(Path.Combine(tempRoot, "business.plugin.ps1"), "param()");
            NewPluginFile(Path.Combine(tempRoot, "data", "industries.csv"), "Sector,Industry\nTechnology,Software\n");

            var securityPolicy = new DataOnlyGenerationPluginSecurityPolicy();
            var catalog = new FileSystemExternalGenerationPluginCatalog(
                new GenerationPluginManifestValidator(securityPolicy),
                securityPolicy,
                new AllowListExternalPluginTrustPolicy());
            var manifests = catalog.Discover(tempRoot);

            var address = Assert.Single(manifests, manifest => manifest.Capability == "address");
            Assert.Equal("LegacyManifest", address.PluginKind);
            Assert.Equal(PluginExecutionMode.PowerShellScript, address.ExecutionMode);
            Assert.Contains("city", address.Dependencies);
            Assert.Contains(address.LocalDataPaths, path => path.EndsWith(Path.Combine("address.Generator", "data", "countries.csv"), StringComparison.OrdinalIgnoreCase));

            var business = Assert.Single(manifests, manifest => manifest.Capability == "Business");
            Assert.Equal("Manifest", business.PluginKind);
            Assert.Equal(PluginExecutionMode.PowerShellScript, business.ExecutionMode);
            Assert.Equal("Business Plugin", business.DisplayName);
            Assert.Contains(PluginRuntimeCapability.ReadPluginData, business.Security.RequestedCapabilities);
            var parameter = Assert.Single(business.Parameters);
            Assert.Equal("Region", parameter.Name);
            Assert.Equal("plugins.business.region", business.Metadata["schemaKey"]);
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
    public void Registry_Returns_BuiltIn_And_Discovered_Manifests()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            File.WriteAllText(Path.Combine(tempRoot, "reporting.generator.json"), """{ "capability": "Reporting", "displayName": "Reporting Plugin" }""");

            var registry = new GenerationPluginRegistry(
                new[] { new TestPlugin("Organization") },
                new FileSystemExternalGenerationPluginCatalog(
                    new GenerationPluginManifestValidator(new DataOnlyGenerationPluginSecurityPolicy()),
                    new DataOnlyGenerationPluginSecurityPolicy(),
                    new AllowListExternalPluginTrustPolicy()));

            var manifests = registry.GetAllManifests(new[] { tempRoot });

            Assert.Contains(manifests, manifest => manifest.Capability == "Organization" && manifest.PluginKind == "BuiltIn");
            Assert.Contains(manifests, manifest => manifest.Capability == "Reporting" && manifest.PluginKind == "Manifest");
            Assert.Contains(manifests, manifest => manifest.Capability == "Reporting" && manifest.ExecutionMode == PluginExecutionMode.MetadataOnly);
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
    public void Validator_Distinguishes_Script_Assembly_And_Invalid_Plugins()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            var scriptPath = Path.Combine(tempRoot, "plugin.ps1");
            var dllPath = Path.Combine(tempRoot, "plugin.dll");
            File.WriteAllText(scriptPath, "param()");
            File.WriteAllText(dllPath, "placeholder");

            var validator = new GenerationPluginManifestValidator(new DataOnlyGenerationPluginSecurityPolicy());

            var scriptResult = validator.Validate(new GenerationPluginManifest
            {
                Capability = "TaxIdentifiers",
                DisplayName = "Tax Identifiers",
                PluginKind = "Manifest",
                ExecutionMode = PluginExecutionMode.PowerShellScript,
                EntryPoint = scriptPath
            });

            var assemblyResult = validator.Validate(new GenerationPluginManifest
            {
                Capability = "CardGenerators",
                DisplayName = "Card Generators",
                PluginKind = "Manifest",
                ExecutionMode = PluginExecutionMode.DotNetAssembly,
                EntryPoint = dllPath
            });

            var invalidResult = validator.Validate(new GenerationPluginManifest
            {
                Capability = "BrokenPlugin",
                DisplayName = "Broken",
                PluginKind = "Manifest",
                ExecutionMode = PluginExecutionMode.PowerShellScript,
                EntryPoint = dllPath
            });

            Assert.True(scriptResult.IsValid);
            Assert.True(assemblyResult.IsValid);
            Assert.False(invalidResult.IsValid);
            Assert.Contains(invalidResult.Messages, message => message.Message.Contains("supported file type", StringComparison.OrdinalIgnoreCase));
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
    public void Discovery_Skips_Invalid_External_Plugins()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            File.WriteAllText(Path.Combine(tempRoot, "broken.generator.json"), """
                {
                  "capability": "Broken",
                  "displayName": "Broken Plugin",
                  "executionMode": "DotNetAssembly",
                  "entryPoint": "missing.dll"
                }
                """);

            var securityPolicy = new DataOnlyGenerationPluginSecurityPolicy();
            var catalog = new FileSystemExternalGenerationPluginCatalog(
                new GenerationPluginManifestValidator(securityPolicy),
                securityPolicy,
                new AllowListExternalPluginTrustPolicy());
            var manifests = catalog.Discover(tempRoot);

            Assert.DoesNotContain(manifests, manifest => manifest.Capability == "Broken");
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
    public void Validator_Rejects_Unsafe_Runtime_Capabilities_For_External_Plugins()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            var scriptPath = Path.Combine(tempRoot, "unsafe.plugin.ps1");
            File.WriteAllText(scriptPath, "param()");

            var validator = new GenerationPluginManifestValidator(new DataOnlyGenerationPluginSecurityPolicy());
            var result = validator.Validate(new GenerationPluginManifest
            {
                Capability = "UnsafePlugin",
                DisplayName = "Unsafe Plugin",
                PluginKind = "Manifest",
                ExecutionMode = PluginExecutionMode.PowerShellScript,
                EntryPoint = scriptPath,
                Security = new PluginSecurityProfile
                {
                    DataOnly = false,
                    RequestedCapabilities = new()
                    {
                        PluginRuntimeCapability.GenerateData,
                        PluginRuntimeCapability.WriteFiles,
                        PluginRuntimeCapability.InvokeNetwork,
                        PluginRuntimeCapability.ModifyEnvironment
                    }
                }
            });

            Assert.False(result.IsValid);
            Assert.Contains(result.Messages, message => message.Message.Contains("data-only", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Messages, message => message.Message.Contains("WriteFiles", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Messages, message => message.Message.Contains("InvokeNetwork", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Messages, message => message.Message.Contains("ModifyEnvironment", StringComparison.OrdinalIgnoreCase));
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
    public void TrustPolicy_Requires_Assembly_OptIn_And_Hash_Approval()
    {
        var trustPolicy = new AllowListExternalPluginTrustPolicy();
        var manifest = new GenerationPluginManifest
        {
            Capability = "Cards",
            DisplayName = "Cards",
            PluginKind = "Manifest",
            ExecutionMode = PluginExecutionMode.DotNetAssembly,
            EntryPoint = @"C:\plugins\cards.dll",
            Provenance = new PluginProvenance
            {
                ContentHash = "ABC123",
                EntryPointHash = "ENTRY123"
            }
        };

        var deniedByDefault = trustPolicy.Evaluate(manifest, new ExternalPluginExecutionSettings());
        Assert.False(deniedByDefault.Allowed);
        Assert.Contains(deniedByDefault.Reasons, reason => reason.Contains("AllowAssemblyPlugins", StringComparison.OrdinalIgnoreCase));

        var deniedWithoutHash = trustPolicy.Evaluate(manifest, new ExternalPluginExecutionSettings
        {
            AllowAssemblyPlugins = true
        });
        Assert.False(deniedWithoutHash.Allowed);
        Assert.Contains(deniedWithoutHash.Reasons, reason => reason.Contains("allowed hash list", StringComparison.OrdinalIgnoreCase));

        var allowed = trustPolicy.Evaluate(manifest, new ExternalPluginExecutionSettings
        {
            AllowAssemblyPlugins = true,
            AllowedContentHashes = new() { "ABC123" }
        });
        Assert.True(allowed.Allowed);
    }

    [Fact]
    public void TrustPolicy_Rejects_Assembly_Plugins_With_Incomplete_Provenance()
    {
        var trustPolicy = new AllowListExternalPluginTrustPolicy();
        var manifest = new GenerationPluginManifest
        {
            Capability = "Cards",
            DisplayName = "Cards",
            PluginKind = "Manifest",
            ExecutionMode = PluginExecutionMode.DotNetAssembly,
            EntryPoint = @"C:\plugins\cards.dll",
            Provenance = new PluginProvenance
            {
                ContentHash = "ABC123"
            }
        };

        var denied = trustPolicy.Evaluate(manifest, new ExternalPluginExecutionSettings
        {
            AllowAssemblyPlugins = true,
            AllowedContentHashes = new() { "ABC123" }
        });

        Assert.False(denied.Allowed);
        Assert.Contains(denied.Reasons, reason => reason.Contains("complete entry point", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validator_Rejects_EntryPoint_And_LocalData_Outside_Plugin_Root()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            var pluginRoot = Path.Combine(tempRoot, "plugin");
            Directory.CreateDirectory(pluginRoot);
            var outsideScriptPath = Path.Combine(tempRoot, "outside.plugin.ps1");
            var outsideDataPath = Path.Combine(tempRoot, "outside.csv");
            File.WriteAllText(outsideScriptPath, "param()");
            File.WriteAllText(outsideDataPath, "Value\n1\n");

            var validator = new GenerationPluginManifestValidator(new DataOnlyGenerationPluginSecurityPolicy());
            var result = validator.Validate(new GenerationPluginManifest
            {
                Capability = "EscapedPlugin",
                DisplayName = "Escaped Plugin",
                PluginKind = "Manifest",
                ExecutionMode = PluginExecutionMode.PowerShellScript,
                SourcePath = Path.Combine(pluginRoot, "escaped.generator.json"),
                EntryPoint = outsideScriptPath,
                LocalDataPaths = new() { outsideDataPath }
            });

            Assert.False(result.IsValid);
            Assert.Contains(result.Messages, message => message.Message.Contains("entry point", StringComparison.OrdinalIgnoreCase) && message.Message.Contains("package root", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Messages, message => message.Message.Contains("Local data path", StringComparison.OrdinalIgnoreCase) && message.Message.Contains("package root", StringComparison.OrdinalIgnoreCase));
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
    public void Inspect_Reports_Invalid_And_Trust_Gated_Plugins()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            File.WriteAllText(Path.Combine(tempRoot, "broken.generator.json"), "{ not-json");
            File.WriteAllText(Path.Combine(tempRoot, "cards.generator.json"), """
                {
                  "capability": "Cards",
                  "displayName": "Card Plugin",
                  "executionMode": "DotNetAssembly",
                  "entryPoint": "cards.dll",
                  "security": {
                    "dataOnly": true,
                    "requestedCapabilities": [ "GenerateData" ]
                  }
                }
                """);
            File.WriteAllText(Path.Combine(tempRoot, "cards.dll"), "placeholder");

            var securityPolicy = new DataOnlyGenerationPluginSecurityPolicy();
            var catalog = new FileSystemExternalGenerationPluginCatalog(
                new GenerationPluginManifestValidator(securityPolicy),
                securityPolicy,
                new AllowListExternalPluginTrustPolicy());

            var records = catalog.Inspect(new[] { tempRoot }, new ExternalPluginExecutionSettings());

            var broken = Assert.Single(records, item => item.SourcePath.EndsWith("broken.generator.json", StringComparison.OrdinalIgnoreCase));
            Assert.False(broken.Parsed);
            Assert.False(broken.Valid);
            Assert.Contains(broken.ValidationMessages, message => message.Contains("could not be parsed", StringComparison.OrdinalIgnoreCase));

            var cards = Assert.Single(records, item => item.Capability == "Cards");
            Assert.True(cards.Parsed);
            Assert.True(cards.Valid);
            Assert.False(cards.Trusted);
            Assert.True(cards.RequiresAssemblyOptIn);
            Assert.True(cards.RequiresHashApproval);
            Assert.Contains(cards.TrustMessages, message => message.Contains("AllowAssemblyPlugins", StringComparison.OrdinalIgnoreCase));
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
    public void CapabilityResolver_Auto_Includes_Dependencies_And_Exposes_Parameter_Metadata()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            File.WriteAllText(Path.Combine(tempRoot, "dependency.generator.json"), """
                {
                  "capability": "DependencyPlugin",
                  "displayName": "Dependency Plugin",
                  "executionMode": "PowerShellScript",
                  "entryPoint": "dependency.plugin.ps1",
                  "parameters": [
                    {
                      "name": "DependencyMode",
                      "typeName": "System.String",
                      "helpText": "How the dependency behaves."
                    }
                  ],
                  "metadata": {
                    "schemaKey": "plugins.dependency.mode"
                  }
                }
                """);
            File.WriteAllText(Path.Combine(tempRoot, "dependency.plugin.ps1"), "param()");

            File.WriteAllText(Path.Combine(tempRoot, "root.generator.json"), """
                {
                  "capability": "RootPlugin",
                  "displayName": "Root Plugin",
                  "executionMode": "PowerShellScript",
                  "entryPoint": "root.plugin.ps1",
                  "dependencies": [ "DependencyPlugin" ],
                  "parameters": [
                    {
                      "name": "RootMode",
                      "typeName": "System.String",
                      "helpText": "How the root behaves."
                    }
                  ]
                }
                """);
            File.WriteAllText(Path.Combine(tempRoot, "root.plugin.ps1"), "param()");

            var services = new ServiceCollection()
                .AddSyntheticEnterpriseCore()
                .BuildServiceProvider();
            var resolver = services.GetRequiredService<IExternalPluginCapabilityResolver>();

            var plan = resolver.Resolve(new GenerationContext
            {
                Scenario = new ScenarioDefinition
                {
                    Name = "Capability Plan"
                },
                ExternalPlugins = new ExternalPluginExecutionSettings
                {
                    Enabled = true,
                    PluginRootPaths = new() { tempRoot },
                    EnabledCapabilities = new() { "RootPlugin" }
                }
            });

            Assert.Equal(new[] { "DependencyPlugin", "RootPlugin" }, plan.ActivePlugins.Select(item => item.Capability).ToArray());
            var dependencyContribution = Assert.Single(plan.Contributions, item => item.Capability == "DependencyPlugin");
            Assert.Equal("DependencyMode", Assert.Single(dependencyContribution.Parameters).Name);
            Assert.Equal("plugins.dependency.mode", dependencyContribution.Metadata["schemaKey"]);
            Assert.Empty(plan.Warnings);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static void NewPluginFile(string path, string contents)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, contents);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"datagen-plugin-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class TestPlugin : IWorldGenerationPlugin
    {
        public TestPlugin(string capability)
        {
            Manifest = new GenerationPluginManifest
            {
                Capability = capability,
                DisplayName = capability
            };
        }

        public GenerationPluginManifest Manifest { get; }

        public bool IsEnabled(ScenarioDefinition scenario) => true;

        public void Execute(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs)
        {
        }
    }
}
