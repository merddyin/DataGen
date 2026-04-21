using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Plugins;
using SyntheticEnterprise.Core.Abstractions;
using SyntheticEnterprise.Core.DependencyInjection;
using SyntheticEnterprise.Core.Plugins;
using SyntheticEnterprise.Core.Scenarios;

namespace SyntheticEnterprise.Core.Tests;

public sealed class ExternalPluginRuntimeTests
{
    [Fact]
    public void WorldGenerator_Executes_Safe_Script_Plugin_In_Restricted_Host()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            File.WriteAllText(Path.Combine(tempRoot, "taxidentifiers.generator.json"), """
                {
                  "capability": "TaxIdentifiers",
                  "displayName": "Tax Identifier Plugin",
                  "executionMode": "PowerShellScript",
                  "entryPoint": "taxidentifiers.plugin.ps1",
                  "security": {
                    "dataOnly": true,
                    "requestedCapabilities": [ "GenerateData" ]
                  }
                }
                """);

            File.WriteAllText(Path.Combine(tempRoot, "taxidentifiers.plugin.ps1"), """
                $records = @()
                foreach ($person in $InputWorld.People) {
                  $records += New-PluginRecord -RecordType 'TaxIdentifier' -AssociatedEntityType 'Person' -AssociatedEntityId $person.Id -Properties @{
                    IdentifierType = 'Synthetic'
                    Country = $person.Country
                  }
                }

                New-PluginResult -Records $records -Warnings @('restricted-host-ok')
                """);

            var services = new ServiceCollection()
                .AddSyntheticEnterpriseCore()
                .BuildServiceProvider();

            var generator = services.GetRequiredService<IWorldGenerator>();
            var result = generator.Generate(
                new GenerationContext
                {
                    Scenario = new ScenarioDefinition
                    {
                        Name = "Plugin Test",
                        Companies = new()
                        {
                            new ScenarioCompanyDefinition
                            {
                                Name = "Plugin Test Co",
                                Industry = "Technology",
                                EmployeeCount = 4,
                                OfficeCount = 1,
                                Countries = new() { "United States" }
                            }
                        }
                    },
                    Seed = 42,
                    ExternalPlugins = new ExternalPluginExecutionSettings
                    {
                        Enabled = true,
                        PluginRootPaths = new() { tempRoot },
                        EnabledCapabilities = new() { "TaxIdentifiers" }
                    }
                },
                new CatalogSet());

            Assert.NotEmpty(result.World.People);
            Assert.Equal(result.World.People.Count, result.World.PluginRecords.Count);
            Assert.All(result.World.PluginRecords, record => Assert.Equal("TaxIdentifiers", record.PluginCapability));
            Assert.Contains(result.Warnings, warning => warning.Contains("restricted-host-ok", StringComparison.OrdinalIgnoreCase));
            Assert.Contains("TaxIdentifiers", result.WorldMetadata!.AppliedLayers);
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
    public void WorldGenerator_Auto_Includes_External_Plugin_Dependencies()
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
                  "security": {
                    "dataOnly": true,
                    "requestedCapabilities": [ "GenerateData" ]
                  }
                }
                """);
            File.WriteAllText(Path.Combine(tempRoot, "dependency.plugin.ps1"), """
                New-PluginResult -Records @(
                  (New-PluginRecord -RecordType 'DependencyAudit' -AssociatedEntityType 'Company' -AssociatedEntityId $InputWorld.Companies[0].Id -Properties @{ Source = 'Dependency' })
                ) -Warnings @('dependency-ran')
                """);

            File.WriteAllText(Path.Combine(tempRoot, "root.generator.json"), """
                {
                  "capability": "RootPlugin",
                  "displayName": "Root Plugin",
                  "executionMode": "PowerShellScript",
                  "entryPoint": "root.plugin.ps1",
                  "dependencies": [ "DependencyPlugin" ],
                  "security": {
                    "dataOnly": true,
                    "requestedCapabilities": [ "GenerateData" ]
                  }
                }
                """);
            File.WriteAllText(Path.Combine(tempRoot, "root.plugin.ps1"), """
                New-PluginResult -Records @(
                  (New-PluginRecord -RecordType 'RootAudit' -AssociatedEntityType 'Company' -AssociatedEntityId $InputWorld.Companies[0].Id -Properties @{ Source = 'Root' })
                ) -Warnings @('root-ran')
                """);

            var services = new ServiceCollection()
                .AddSyntheticEnterpriseCore()
                .BuildServiceProvider();

            var generator = services.GetRequiredService<IWorldGenerator>();
            var result = generator.Generate(
                new GenerationContext
                {
                    Scenario = MinimalScenario("Dependency Co"),
                    ExternalPlugins = new ExternalPluginExecutionSettings
                    {
                        Enabled = true,
                        PluginRootPaths = new() { tempRoot },
                        EnabledCapabilities = new() { "RootPlugin" }
                    }
                },
                new CatalogSet());

            Assert.Equal(2, result.World.PluginRecords.Count);
            Assert.Contains(result.World.PluginRecords, record => record.PluginCapability == "DependencyPlugin");
            Assert.Contains(result.World.PluginRecords, record => record.PluginCapability == "RootPlugin");
            Assert.Contains(result.Warnings, warning => warning.Contains("dependency-ran", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Warnings, warning => warning.Contains("root-ran", StringComparison.OrdinalIgnoreCase));
            Assert.Contains("DependencyPlugin", result.WorldMetadata!.AppliedLayers);
            Assert.Contains("RootPlugin", result.WorldMetadata!.AppliedLayers);
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
    public void WorldGenerator_Clones_Script_Plugin_Inputs_And_Captures_Bounded_Diagnostics()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            File.WriteAllText(Path.Combine(tempRoot, "diagnostics.generator.json"), """
                {
                  "capability": "DiagnosticsPlugin",
                  "displayName": "Diagnostics Plugin",
                  "executionMode": "PowerShellScript",
                  "entryPoint": "diagnostics.plugin.ps1",
                  "security": {
                    "dataOnly": true,
                    "requestedCapabilities": [ "GenerateData", "EmitDiagnostics" ]
                  }
                }
                """);

            File.WriteAllText(Path.Combine(tempRoot, "diagnostics.plugin.ps1"), """
                $VerbosePreference = 'Continue'
                $InformationPreference = 'Continue'
                $PluginRequest.Metadata['Mutation'] = 'Mutated By Plugin'
                Write-Warning ('W' * 80)
                Write-Verbose ('V' * 80)
                Write-Information ('I' * 80)
                New-PluginResult -Records @(
                  (New-PluginRecord -RecordType 'Audit' -AssociatedEntityType 'Company' -AssociatedEntityId $InputWorld.Companies[0].Id -Properties @{
                    MutationValue = $PluginRequest.Metadata['Mutation']
                  })
                ) -Warnings @()
                """);

            var services = new ServiceCollection()
                .AddSyntheticEnterpriseCore()
                .BuildServiceProvider();

            var generator = services.GetRequiredService<IWorldGenerator>();
            var context = new GenerationContext
            {
                Scenario = MinimalScenario("Diagnostics Co"),
                Metadata = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Mutation"] = "Original"
                },
                ExternalPlugins = new ExternalPluginExecutionSettings
                {
                    Enabled = true,
                    PluginRootPaths = new() { tempRoot },
                    EnabledCapabilities = new() { "DiagnosticsPlugin" },
                    MaxDiagnosticEntries = 2,
                    MaxDiagnosticCharacters = 32
                }
            };

            var result = generator.Generate(context, new CatalogSet());

            Assert.Equal("Original", context.Metadata["Mutation"]);
            var record = Assert.Single(result.World.PluginRecords);
            Assert.Equal("DiagnosticsPlugin", record.PluginCapability);
            Assert.Equal("Mutated By Plugin", record.Properties["MutationValue"]);
            Assert.Contains(result.Warnings, warning => warning.Contains("[warning]", StringComparison.OrdinalIgnoreCase) && warning.Contains("truncated", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Warnings, warning => warning.Contains("[verbose]", StringComparison.OrdinalIgnoreCase) && warning.Contains("truncated", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(result.Warnings, warning => warning.Contains("[info]", StringComparison.OrdinalIgnoreCase));
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
    public void WorldGenerator_Passes_Capability_Settings_To_Plugin_Request()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            File.WriteAllText(Path.Combine(tempRoot, "settings.generator.json"), """
                {
                  "capability": "SettingsPlugin",
                  "displayName": "Settings Plugin",
                  "executionMode": "PowerShellScript",
                  "entryPoint": "settings.plugin.ps1",
                  "security": {
                    "dataOnly": true,
                    "requestedCapabilities": [ "GenerateData" ]
                  }
                }
                """);

            File.WriteAllText(Path.Combine(tempRoot, "settings.plugin.ps1"), """
                New-PluginResult -Records @(
                  (New-PluginRecord -RecordType 'SettingsAudit' -AssociatedEntityType 'Company' -AssociatedEntityId $InputWorld.Companies[0].Id -Properties @{
                    Region = $PluginRequest.PluginSettings['Region']
                    Profile = $PluginRequest.PluginSettings['Profile']
                  })
                ) -Warnings @()
                """);

            var services = new ServiceCollection()
                .AddSyntheticEnterpriseCore()
                .BuildServiceProvider();

            var generator = services.GetRequiredService<IWorldGenerator>();
            var result = generator.Generate(
                new GenerationContext
                {
                    Scenario = MinimalScenario("Settings Co"),
                    ExternalPlugins = new ExternalPluginExecutionSettings
                    {
                        Enabled = true,
                        PluginRootPaths = new() { tempRoot },
                        EnabledCapabilities = new() { "SettingsPlugin" },
                        CapabilityConfigurations = new()
                        {
                            new ExternalPluginCapabilityConfiguration
                            {
                                Capability = "SettingsPlugin",
                                Settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                                {
                                    ["Region"] = "US",
                                    ["Profile"] = "Finance"
                                }
                            }
                        }
                    }
                },
                new CatalogSet());

            var record = Assert.Single(result.World.PluginRecords);
            Assert.Equal("US", record.Properties["Region"]);
            Assert.Equal("Finance", record.Properties["Profile"]);
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
    public void WorldGenerator_Applies_Default_Plugin_Settings_From_Scenario_Profile()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            File.WriteAllText(Path.Combine(tempRoot, "defaults.generator.json"), """
                {
                  "capability": "DefaultedPlugin",
                  "displayName": "Defaulted Plugin",
                  "executionMode": "PowerShellScript",
                  "entryPoint": "defaulted.plugin.ps1",
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
            File.WriteAllText(Path.Combine(tempRoot, "defaulted.plugin.ps1"), """
                $region = $PluginRequest.PluginSettings['Region']
                New-PluginResult -Records @(
                    New-PluginRecord -Type 'DefaultedPlugin' -Properties @{ Region = $region }
                )
                """);

            var services = new ServiceCollection()
                .AddSyntheticEnterpriseCore()
                .BuildServiceProvider();
            var worldGenerator = services.GetRequiredService<IWorldGenerator>();
            var catalogs = new CatalogSet();
            var scenario = services.GetRequiredService<IScenarioPluginProfileHydrator>()
                .Hydrate(new ScenarioDefinition
                {
                    Name = "Defaulted Plugin Runtime",
                    ExternalPlugins = new ExternalPluginScenarioProfile
                    {
                        PluginRootPaths = new() { tempRoot },
                        EnabledCapabilities = new() { "DefaultedPlugin" }
                    }
                }).Scenario;

            var result = worldGenerator.Generate(new GenerationContext
            {
                Scenario = scenario,
                ExternalPlugins = new ExternalPluginExecutionSettings
                {
                    Enabled = true,
                    PluginRootPaths = new() { tempRoot },
                    EnabledCapabilities = new() { "DefaultedPlugin" },
                    CapabilityConfigurations = scenario.ExternalPlugins.CapabilityConfigurations.ToList()
                }
            }, catalogs);

            var record = Assert.Single(result.World.PluginRecords);
            Assert.Equal("US", record.Properties["Region"]);
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
    public void WorldGenerator_Executes_Bundled_FirstParty_Packs_From_Scenario_Profile()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();
        var hydrator = services.GetRequiredService<IScenarioPluginProfileHydrator>();
        var generator = services.GetRequiredService<IWorldGenerator>();

        var scenario = hydrator.Hydrate(new ScenarioDefinition
        {
            Name = "Bundled First-Party Packs",
            Companies = new()
            {
                new ScenarioCompanyDefinition
                {
                    Name = "Pack Test Co",
                    Industry = "Technology",
                    EmployeeCount = 18,
                    OfficeCount = 2,
                    Countries = new() { "United States" },
                    DatabaseCount = 2,
                    FileShareCount = 2,
                    CollaborationSiteCount = 3
                }
            },
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
                            ["TicketCount"] = "4"
                        }
                    },
                    new ScenarioPackSelection
                    {
                        PackId = "FirstParty.SecOps",
                        Settings = new(StringComparer.OrdinalIgnoreCase)
                        {
                            ["AlertCount"] = "3"
                        }
                    },
                    new ScenarioPackSelection
                    {
                        PackId = "FirstParty.BusinessOps",
                        Settings = new(StringComparer.OrdinalIgnoreCase)
                        {
                            ["RequestCount"] = "2"
                        }
                    }
                }
            }
        }).Scenario;

        var result = generator.Generate(
            new GenerationContext
            {
                Scenario = scenario,
                ExternalPlugins = new ExternalPluginExecutionSettings
                {
                    Enabled = true,
                    PluginRootPaths = scenario.ExternalPlugins.PluginRootPaths.ToList(),
                    EnabledCapabilities = scenario.ExternalPlugins.EnabledCapabilities.ToList(),
                    CapabilityConfigurations = scenario.ExternalPlugins.CapabilityConfigurations.ToList(),
                    MaxInputPayloadBytes = 64 * 1024 * 1024,
                    MaxOutputPayloadBytes = 64 * 1024 * 1024
                }
            },
            new CatalogSet());

        Assert.Contains(result.World.PluginRecords, record => record.PluginCapability == "FirstParty.ITSM" && record.RecordType == "ItsmTicket");
        Assert.Contains(result.World.PluginRecords, record => record.PluginCapability == "FirstParty.SecOps" && record.RecordType == "SecurityAlert");
        Assert.Contains(result.World.PluginRecords, record => record.PluginCapability == "FirstParty.BusinessOps" && record.RecordType == "Vendor");
        Assert.Contains(result.World.PluginRecords, record => record.RecordType == "ItsmQueueOwnership");
        Assert.Contains(result.World.PluginRecords, record => record.RecordType == "SecurityAlertOwnership");
        Assert.Contains(result.World.PluginRecords, record => record.RecordType == "VendorOwnership");
        Assert.Contains("FirstParty.ITSM", result.WorldMetadata!.AppliedLayers);
        Assert.Contains("FirstParty.SecOps", result.WorldMetadata.AppliedLayers);
        Assert.Contains("FirstParty.BusinessOps", result.WorldMetadata.AppliedLayers);
    }

    [Fact]
    public void WorldGenerator_Executes_Assembly_Plugin_In_Isolated_Host()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            WriteAssemblyPluginProject(tempRoot, "cards", """
                using SyntheticEnterprise.Contracts.Plugins;
                using SyntheticEnterprise.Contracts.Models;

                public sealed class CardsPlugin : IExternalGenerationAssemblyPlugin
                {
                    public string Capability => "Cards";

                    public ExternalPluginExecutionResponse Execute(ExternalPluginExecutionRequest request)
                    {
                        return new ExternalPluginExecutionResponse
                        {
                            Executed = true,
                            Records = new()
                            {
                                new PluginGeneratedRecord
                                {
                                    Id = "PLUGIN-TEST-1",
                                    PluginCapability = request.Manifest.Capability,
                                    RecordType = "Card",
                                    AssociatedEntityType = "Company",
                                    AssociatedEntityId = request.InputWorld.Companies[0].Id,
                                    Properties = new Dictionary<string, string?>
                                    {
                                        ["Issuer"] = "Synthetic",
                                        ["Status"] = "Generated"
                                    }
                                }
                            },
                            Warnings = new()
                            {
                                "assembly-host-ok"
                            }
                        };
                    }
                }
                """);
            BuildAssemblyPlugin(tempRoot, "cards");

            File.WriteAllText(Path.Combine(tempRoot, "cards.generator.json"), """
                {
                  "capability": "Cards",
                  "displayName": "Card Plugin",
                  "executionMode": "DotNetAssembly",
                  "entryPoint": "bin/Debug/net8.0/cards.dll",
                  "security": {
                    "dataOnly": true,
                    "requestedCapabilities": [ "GenerateData" ]
                  }
                }
                """);

            var services = new ServiceCollection()
                .AddSyntheticEnterpriseCore()
                .BuildServiceProvider();
            var registry = services.GetRequiredService<IGenerationPluginRegistry>();
            var manifest = Assert.Single(registry.GetDiscoveredManifests(new[] { tempRoot }), item => item.Capability == "Cards");

            var generator = services.GetRequiredService<IWorldGenerator>();
            var result = generator.Generate(
                new GenerationContext
                {
                    Scenario = new ScenarioDefinition
                    {
                        Name = "Assembly Plugin Test",
                        Companies = new()
                        {
                            new ScenarioCompanyDefinition
                            {
                                Name = "Assembly Test Co",
                                Industry = "Technology",
                                EmployeeCount = 2,
                                OfficeCount = 1
                            }
                        }
                    },
                    ExternalPlugins = new ExternalPluginExecutionSettings
                    {
                        Enabled = true,
                        PluginRootPaths = new() { tempRoot },
                        EnabledCapabilities = new() { "Cards" },
                        AllowAssemblyPlugins = true,
                        AllowedContentHashes = new() { manifest.Provenance.ContentHash! }
                    }
                },
                new CatalogSet());

            var record = Assert.Single(result.World.PluginRecords);
            Assert.Equal("Cards", record.PluginCapability);
            Assert.Equal("Company", record.AssociatedEntityType);
            Assert.Contains(result.Warnings, warning => warning.Contains("assembly-host-ok", StringComparison.OrdinalIgnoreCase));
            Assert.Contains("Cards", result.WorldMetadata!.AppliedLayers);
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
    public void WorldGenerator_Captures_Bounded_Assembly_Host_Diagnostics()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            WriteAssemblyPluginProject(tempRoot, "diagnostics", """
                using SyntheticEnterprise.Contracts.Plugins;

                public sealed class DiagnosticsPlugin : IExternalGenerationAssemblyPlugin
                {
                    public string Capability => "DiagnosticsAssembly";

                    public ExternalPluginExecutionResponse Execute(ExternalPluginExecutionRequest request)
                    {
                        Console.WriteLine(new string('O', 80));
                        Console.Error.WriteLine(new string('E', 80));
                        return new ExternalPluginExecutionResponse
                        {
                            Executed = true,
                            Warnings = new()
                            {
                                new string('W', 80)
                            }
                        };
                    }
                }
                """);
            BuildAssemblyPlugin(tempRoot, "diagnostics");

            File.WriteAllText(Path.Combine(tempRoot, "diagnostics.generator.json"), """
                {
                  "capability": "DiagnosticsAssembly",
                  "displayName": "Diagnostics Assembly Plugin",
                  "executionMode": "DotNetAssembly",
                  "entryPoint": "bin/Debug/net8.0/diagnostics.dll",
                  "security": {
                    "dataOnly": true,
                    "requestedCapabilities": [ "GenerateData", "EmitDiagnostics" ]
                  }
                }
                """);

            var services = new ServiceCollection()
                .AddSyntheticEnterpriseCore()
                .BuildServiceProvider();
            var registry = services.GetRequiredService<IGenerationPluginRegistry>();
            var manifest = Assert.Single(registry.GetDiscoveredManifests(new[] { tempRoot }), item => item.Capability == "DiagnosticsAssembly");
            var generator = services.GetRequiredService<IWorldGenerator>();

            var result = generator.Generate(
                new GenerationContext
                {
                    Scenario = MinimalScenario("Diagnostics Assembly Co"),
                    ExternalPlugins = new ExternalPluginExecutionSettings
                    {
                        Enabled = true,
                        PluginRootPaths = new() { tempRoot },
                        EnabledCapabilities = new() { "DiagnosticsAssembly" },
                        AllowAssemblyPlugins = true,
                        AllowedContentHashes = new() { manifest.Provenance.ContentHash! },
                        MaxDiagnosticCharacters = 32
                    }
                },
                new CatalogSet());

            Assert.Contains("DiagnosticsAssembly", result.WorldMetadata!.AppliedLayers);
            Assert.Contains(result.Warnings, warning => warning.Contains("[stdout]", StringComparison.OrdinalIgnoreCase) && warning.Contains("truncated", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Warnings, warning => warning.Contains("[stderr]", StringComparison.OrdinalIgnoreCase) && warning.Contains("truncated", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Warnings, warning => warning.Contains("truncated", StringComparison.OrdinalIgnoreCase));
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
    public void WorldGenerator_Requires_Explicit_OptIn_And_Hash_Approval_For_Assembly_Plugins()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            WriteAssemblyPluginProject(tempRoot, "cards", """
                using SyntheticEnterprise.Contracts.Plugins;

                public sealed class CardsPlugin : IExternalGenerationAssemblyPlugin
                {
                    public string Capability => "Cards";

                    public ExternalPluginExecutionResponse Execute(ExternalPluginExecutionRequest request)
                    {
                        return new ExternalPluginExecutionResponse
                        {
                            Executed = true
                        };
                    }
                }
                """);
            BuildAssemblyPlugin(tempRoot, "cards");

            File.WriteAllText(Path.Combine(tempRoot, "cards.generator.json"), """
                {
                  "capability": "Cards",
                  "displayName": "Card Plugin",
                  "executionMode": "DotNetAssembly",
                  "entryPoint": "bin/Debug/net8.0/cards.dll",
                  "security": {
                    "dataOnly": true,
                    "requestedCapabilities": [ "GenerateData" ]
                  }
                }
                """);

            var services = new ServiceCollection()
                .AddSyntheticEnterpriseCore()
                .BuildServiceProvider();
            var registry = services.GetRequiredService<IGenerationPluginRegistry>();
            var manifest = Assert.Single(registry.GetDiscoveredManifests(new[] { tempRoot }), item => item.Capability == "Cards");
            var generator = services.GetRequiredService<IWorldGenerator>();

            var notOptedIn = generator.Generate(
                new GenerationContext
                {
                    Scenario = MinimalScenario("Assembly Trust Co"),
                    ExternalPlugins = new ExternalPluginExecutionSettings
                    {
                        Enabled = true,
                        PluginRootPaths = new() { tempRoot },
                        EnabledCapabilities = new() { "Cards" }
                    }
                },
                new CatalogSet());

            Assert.Empty(notOptedIn.World.PluginRecords);
            Assert.Contains(notOptedIn.Warnings, warning => warning.Contains("AllowAssemblyPlugins", StringComparison.OrdinalIgnoreCase));

            var missingHashApproval = generator.Generate(
                new GenerationContext
                {
                    Scenario = MinimalScenario("Assembly Trust Co"),
                    ExternalPlugins = new ExternalPluginExecutionSettings
                    {
                        Enabled = true,
                        PluginRootPaths = new() { tempRoot },
                        EnabledCapabilities = new() { "Cards" },
                        AllowAssemblyPlugins = true
                    }
                },
                new CatalogSet());

            Assert.Empty(missingHashApproval.World.PluginRecords);
            Assert.Contains(missingHashApproval.Warnings, warning => warning.Contains("allowed hash list", StringComparison.OrdinalIgnoreCase));

            var allowed = generator.Generate(
                new GenerationContext
                {
                    Scenario = MinimalScenario("Assembly Trust Co"),
                    ExternalPlugins = new ExternalPluginExecutionSettings
                    {
                        Enabled = true,
                        PluginRootPaths = new() { tempRoot },
                        EnabledCapabilities = new() { "Cards" },
                        AllowAssemblyPlugins = true,
                        AllowedContentHashes = new() { manifest.Provenance.ContentHash! }
                    }
                },
                new CatalogSet());

            Assert.Contains("Cards", allowed.WorldMetadata!.AppliedLayers);
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
    public void WorldGenerator_Stops_Script_Plugin_When_Timeout_Is_Reached()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            File.WriteAllText(Path.Combine(tempRoot, "slow.generator.json"), """
                {
                  "capability": "SlowPlugin",
                  "displayName": "Slow Plugin",
                  "executionMode": "PowerShellScript",
                  "entryPoint": "slow.plugin.ps1",
                  "security": {
                    "dataOnly": true,
                    "requestedCapabilities": [ "GenerateData" ]
                  }
                }
                """);
            File.WriteAllText(Path.Combine(tempRoot, "slow.plugin.ps1"), "while ($true) { }");

            var services = new ServiceCollection()
                .AddSyntheticEnterpriseCore()
                .BuildServiceProvider();

            var generator = services.GetRequiredService<IWorldGenerator>();
            var result = generator.Generate(
                new GenerationContext
                {
                    Scenario = MinimalScenario("Timeout Co"),
                    ExternalPlugins = new ExternalPluginExecutionSettings
                    {
                        Enabled = true,
                        PluginRootPaths = new() { tempRoot },
                        EnabledCapabilities = new() { "SlowPlugin" },
                        ExecutionTimeoutSeconds = 1
                    }
                },
                new CatalogSet());

            Assert.Empty(result.World.PluginRecords);
            Assert.Contains(result.Warnings, warning => warning.Contains("timed out", StringComparison.OrdinalIgnoreCase));
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
    public void WorldGenerator_Rejects_Script_Plugin_When_Input_Payload_Exceeds_Configured_Limit()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            File.WriteAllText(Path.Combine(tempRoot, "sizecheck.generator.json"), """
                {
                  "capability": "SizeCheck",
                  "displayName": "Size Check Plugin",
                  "executionMode": "PowerShellScript",
                  "entryPoint": "sizecheck.plugin.ps1",
                  "security": {
                    "dataOnly": true,
                    "requestedCapabilities": [ "GenerateData" ]
                  }
                }
                """);
            File.WriteAllText(Path.Combine(tempRoot, "sizecheck.plugin.ps1"), """
                New-PluginResult -Records @() -Warnings @('should-not-run')
                """);

            var services = new ServiceCollection()
                .AddSyntheticEnterpriseCore()
                .BuildServiceProvider();

            var generator = services.GetRequiredService<IWorldGenerator>();
            var result = generator.Generate(
                new GenerationContext
                {
                    Scenario = new ScenarioDefinition
                    {
                        Name = "Payload Limit Co",
                        Companies = new()
                        {
                            new ScenarioCompanyDefinition
                            {
                                Name = "Payload Limit Co",
                                Industry = "Technology",
                                EmployeeCount = 500,
                                OfficeCount = 3,
                                Countries = new() { "United States" }
                            }
                        }
                    },
                    ExternalPlugins = new ExternalPluginExecutionSettings
                    {
                        Enabled = true,
                        PluginRootPaths = new() { tempRoot },
                        EnabledCapabilities = new() { "SizeCheck" },
                        MaxInputPayloadBytes = 1024
                    }
                },
                new CatalogSet());

            Assert.Empty(result.World.PluginRecords);
            Assert.Contains(result.Warnings, warning => warning.Contains("Input payload exceeded", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(result.Warnings, warning => warning.Contains("should-not-run", StringComparison.OrdinalIgnoreCase));
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
    public void WorldGenerator_Truncates_External_Plugin_Output_To_Configured_Limit()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            File.WriteAllText(Path.Combine(tempRoot, "bulk.generator.json"), """
                {
                  "capability": "BulkPlugin",
                  "displayName": "Bulk Plugin",
                  "executionMode": "PowerShellScript",
                  "entryPoint": "bulk.plugin.ps1",
                  "security": {
                    "dataOnly": true,
                    "requestedCapabilities": [ "GenerateData" ]
                  }
                }
                """);
            File.WriteAllText(Path.Combine(tempRoot, "bulk.plugin.ps1"), """
                $records = @()
                for ($i = 0; $i -lt 12; $i++) {
                  $records += New-PluginRecord -RecordType 'Bulk' -AssociatedEntityType 'Company' -AssociatedEntityId $InputWorld.Companies[0].Id -Properties @{ Index = [string]$i }
                }
                New-PluginResult -Records $records -Warnings @('one','two','three')
                """);

            var services = new ServiceCollection()
                .AddSyntheticEnterpriseCore()
                .BuildServiceProvider();

            var generator = services.GetRequiredService<IWorldGenerator>();
            var result = generator.Generate(
                new GenerationContext
                {
                    Scenario = MinimalScenario("Bulk Co"),
                    ExternalPlugins = new ExternalPluginExecutionSettings
                    {
                        Enabled = true,
                        PluginRootPaths = new() { tempRoot },
                        EnabledCapabilities = new() { "BulkPlugin" },
                        MaxGeneratedRecords = 5,
                        MaxWarningCount = 2
                    }
                },
                new CatalogSet());

            Assert.Equal(5, result.World.PluginRecords.Count);
            Assert.Contains(result.Warnings, warning => warning.Contains("truncated", StringComparison.OrdinalIgnoreCase));
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
    public void WorldGenerator_Can_Require_Content_Hash_Approval_For_Script_Plugins()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            File.WriteAllText(Path.Combine(tempRoot, "trusted.generator.json"), """
                {
                  "capability": "TrustedPlugin",
                  "displayName": "Trusted Plugin",
                  "executionMode": "PowerShellScript",
                  "entryPoint": "trusted.plugin.ps1",
                  "security": {
                    "dataOnly": true,
                    "requestedCapabilities": [ "GenerateData" ]
                  }
                }
                """);
            File.WriteAllText(Path.Combine(tempRoot, "trusted.plugin.ps1"), """
                $records = @()
                $records += New-PluginRecord -RecordType 'Trust' -AssociatedEntityType 'Company' -AssociatedEntityId $InputWorld.Companies[0].Id -Properties @{
                  Status = 'Allowed'
                }

                New-PluginResult -Records $records -Warnings @()
                """);

            var services = new ServiceCollection()
                .AddSyntheticEnterpriseCore()
                .BuildServiceProvider();

            var registry = services.GetRequiredService<IGenerationPluginRegistry>();
            var manifest = Assert.Single(registry.GetDiscoveredManifests(new[] { tempRoot }), item => item.Capability == "TrustedPlugin");
            Assert.False(string.IsNullOrWhiteSpace(manifest.Provenance.ContentHash));

            var generator = services.GetRequiredService<IWorldGenerator>();
            var deniedResult = generator.Generate(
                new GenerationContext
                {
                    Scenario = MinimalScenario("Denied Co"),
                    ExternalPlugins = new ExternalPluginExecutionSettings
                    {
                        Enabled = true,
                        PluginRootPaths = new() { tempRoot },
                        EnabledCapabilities = new() { "TrustedPlugin" },
                        RequireContentHashAllowList = true
                    }
                },
                new CatalogSet());

            Assert.Empty(deniedResult.World.PluginRecords);
            Assert.Contains(deniedResult.Warnings, warning => warning.Contains("allowed hash list", StringComparison.OrdinalIgnoreCase));

            var allowedResult = generator.Generate(
                new GenerationContext
                {
                    Scenario = MinimalScenario("Allowed Co"),
                    ExternalPlugins = new ExternalPluginExecutionSettings
                    {
                        Enabled = true,
                        PluginRootPaths = new() { tempRoot },
                        EnabledCapabilities = new() { "TrustedPlugin" },
                        RequireContentHashAllowList = true,
                        AllowedContentHashes = new() { manifest.Provenance.ContentHash! }
                    }
                },
                new CatalogSet());

            Assert.Single(allowedResult.World.PluginRecords);
            Assert.DoesNotContain(allowedResult.Warnings, warning => warning.Contains("allowed hash list", StringComparison.OrdinalIgnoreCase));
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
    public void WorldGenerator_Rejects_Assembly_Plugin_When_Output_Payload_Exceeds_Configured_Limit()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            WriteAssemblyPluginProject(tempRoot, "oversized", """
                using SyntheticEnterprise.Contracts.Plugins;

                public sealed class OversizedPlugin : IExternalGenerationAssemblyPlugin
                {
                    public string Capability => "OversizedAssembly";

                    public ExternalPluginExecutionResponse Execute(ExternalPluginExecutionRequest request)
                    {
                        return new ExternalPluginExecutionResponse
                        {
                            Executed = true,
                            Warnings = new()
                            {
                                new string('W', 5000)
                            }
                        };
                    }
                }
                """);
            BuildAssemblyPlugin(tempRoot, "oversized");

            File.WriteAllText(Path.Combine(tempRoot, "oversized.generator.json"), """
                {
                  "capability": "OversizedAssembly",
                  "displayName": "Oversized Assembly Plugin",
                  "executionMode": "DotNetAssembly",
                  "entryPoint": "bin/Debug/net8.0/oversized.dll",
                  "security": {
                    "dataOnly": true,
                    "requestedCapabilities": [ "GenerateData", "EmitDiagnostics" ]
                  }
                }
                """);

            var services = new ServiceCollection()
                .AddSyntheticEnterpriseCore()
                .BuildServiceProvider();
            var registry = services.GetRequiredService<IGenerationPluginRegistry>();
            var manifest = Assert.Single(registry.GetDiscoveredManifests(new[] { tempRoot }), item => item.Capability == "OversizedAssembly");
            var generator = services.GetRequiredService<IWorldGenerator>();

            var result = generator.Generate(
                new GenerationContext
                {
                    Scenario = MinimalScenario("Oversized Co"),
                    ExternalPlugins = new ExternalPluginExecutionSettings
                    {
                        Enabled = true,
                        PluginRootPaths = new() { tempRoot },
                        EnabledCapabilities = new() { "OversizedAssembly" },
                        AllowAssemblyPlugins = true,
                        AllowedContentHashes = new() { manifest.Provenance.ContentHash! },
                        MaxOutputPayloadBytes = 1024
                    }
                },
                new CatalogSet());

            Assert.Empty(result.World.PluginRecords);
            Assert.Contains(result.Warnings, warning => warning.Contains("output exceeded", StringComparison.OrdinalIgnoreCase));
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
    public void AssemblyHost_Rejects_Tampered_Assembly_Before_Launch()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            WriteAssemblyPluginProject(tempRoot, "tampered", """
                using SyntheticEnterprise.Contracts.Plugins;

                public sealed class TamperedPlugin : IExternalGenerationAssemblyPlugin
                {
                    public string Capability => "TamperedAssembly";

                    public ExternalPluginExecutionResponse Execute(ExternalPluginExecutionRequest request)
                    {
                        return new ExternalPluginExecutionResponse
                        {
                            Executed = true
                        };
                    }
                }
                """);
            BuildAssemblyPlugin(tempRoot, "tampered");

            File.WriteAllText(Path.Combine(tempRoot, "tampered.generator.json"), """
                {
                  "capability": "TamperedAssembly",
                  "displayName": "Tampered Assembly Plugin",
                  "executionMode": "DotNetAssembly",
                  "entryPoint": "bin/Debug/net8.0/tampered.dll",
                  "security": {
                    "dataOnly": true,
                    "requestedCapabilities": [ "GenerateData" ]
                  }
                }
                """);

            var services = new ServiceCollection()
                .AddSyntheticEnterpriseCore()
                .BuildServiceProvider();
            var registry = services.GetRequiredService<IGenerationPluginRegistry>();
            var manifest = Assert.Single(registry.GetDiscoveredManifests(new[] { tempRoot }), item => item.Capability == "TamperedAssembly");
            Assert.False(string.IsNullOrWhiteSpace(manifest.EntryPoint));

            File.WriteAllText(manifest.EntryPoint!, "tampered");

            var world = services.GetRequiredService<IWorldGenerator>()
                .Generate(new GenerationContext
                {
                    Scenario = MinimalScenario("Tampered Co")
                }, new CatalogSet())
                .World;

            var adapter = new OutOfProcessAssemblyExternalPluginHostAdapter();
            var result = adapter.Execute(
                manifest,
                world,
                new GenerationContext
                {
                    Scenario = MinimalScenario("Tampered Co"),
                    ExternalPlugins = new ExternalPluginExecutionSettings
                    {
                        Enabled = true,
                        AllowAssemblyPlugins = true,
                        AllowedContentHashes = new() { manifest.Provenance.ContentHash! }
                    }
                },
                new CatalogSet());

            Assert.False(result.Executed);
            Assert.Contains(result.Warnings, warning => warning.Contains("no longer matches discovered provenance", StringComparison.OrdinalIgnoreCase));
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
        var path = Path.Combine(Path.GetTempPath(), $"datagen-external-plugin-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteAssemblyPluginProject(string rootPath, string projectName, string source)
    {
        var contractsProjectPath = Path.Combine(TestEnvironmentPaths.GetRepositoryRoot(), "src", "SyntheticEnterprise.Contracts", "SyntheticEnterprise.Contracts.csproj");
        Assert.True(File.Exists(contractsProjectPath), $"Contracts project was not found at '{contractsProjectPath}'.");

        File.WriteAllText(Path.Combine(rootPath, $"{projectName}.csproj"), $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>

              <ItemGroup>
                <ProjectReference Include="{{contractsProjectPath}}" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(rootPath, $"{projectName}.cs"), source);
    }

    private static void BuildAssemblyPlugin(string rootPath, string projectName)
    {
        var projectPath = Path.Combine(rootPath, $"{projectName}.csproj");
        Assert.True(File.Exists(projectPath), $"Assembly plugin project was not created at '{projectPath}'.");

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var processStartInfo = new ProcessStartInfo("dotnet", $"build \"{projectPath}\" -v quiet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processStartInfo);
            Assert.NotNull(process);
            process!.WaitForExit();

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            if (process.ExitCode == 0)
            {
                return;
            }

            var missingProject = output.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
                                 || error.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
                                 || output.Contains("could not be loaded", StringComparison.OrdinalIgnoreCase)
                                 || error.Contains("could not be loaded", StringComparison.OrdinalIgnoreCase);

            if (attempt < 2 && missingProject)
            {
                Thread.Sleep(200);
                continue;
            }

            Assert.Fail($"Assembly plugin build failed.{Environment.NewLine}{output}{Environment.NewLine}{error}");
        }
    }

    private static ScenarioDefinition MinimalScenario(string companyName)
        => new()
        {
            Name = companyName,
            Companies = new()
            {
                new ScenarioCompanyDefinition
                {
                    Name = companyName,
                    Industry = "Technology",
                    EmployeeCount = 2,
                    OfficeCount = 1,
                    Countries = new() { "United States" }
                }
            }
        };
}
