using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Contracts.Plugins;
using SyntheticEnterprise.Core.Abstractions;
using SyntheticEnterprise.Core.DependencyInjection;
using SyntheticEnterprise.Core.Plugins;
using SyntheticEnterprise.Core.Scenarios;

namespace SyntheticEnterprise.Core.Tests;

public sealed class ExternalPluginRuntimeTests
{
    private static readonly JsonSerializerOptions PluginJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void BoundedPluginPayloadStream_Stops_Writing_At_Configured_Limit()
    {
        using var output = new MemoryStream();
        using var stream = new BoundedPluginPayloadStream(output, 1024);

        var exception = Record.Exception(() =>
        {
            JsonSerializer.Serialize(stream, new { Population = new string('P', 32 * 1024) }, PluginJsonOptions);
        });

        Assert.IsType<PluginInputPayloadLimitExceededException>(exception);
        Assert.Equal(1024, stream.BytesWritten);
        Assert.Equal(1024, output.Length);
    }

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AssemblyHost_Stops_When_Diagnostic_Pipe_Crosses_Configured_Limit(bool standardError)
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            var writeStatement = standardError
                ? "Console.Error.Write(new string('E', 2 * 1024 * 1024));"
                : "Console.Out.Write(new string('O', 2 * 1024 * 1024));";
            WriteAssemblyPluginProject(tempRoot, "pipeoverflow", $$"""
                using SyntheticEnterprise.Contracts.Plugins;

                public sealed class PipeOverflowPlugin : IExternalGenerationAssemblyPlugin
                {
                    public string Capability => "PipeOverflow";

                    public ExternalPluginExecutionResponse Execute(ExternalPluginExecutionRequest request)
                    {
                        {{writeStatement}}
                        return new ExternalPluginExecutionResponse { Executed = true };
                    }
                }
                """);
            BuildAssemblyPlugin(tempRoot, "pipeoverflow");
            File.WriteAllText(Path.Combine(tempRoot, "pipeoverflow.generator.json"), """
                {
                  "capability": "PipeOverflow",
                  "displayName": "Pipe Overflow",
                  "executionMode": "DotNetAssembly",
                  "entryPoint": "bin/Debug/net8.0/pipeoverflow.dll",
                  "security": {
                    "dataOnly": true,
                    "requestedCapabilities": [ "GenerateData", "EmitDiagnostics" ]
                  }
                }
                """);

            using var services = new ServiceCollection()
                .AddSyntheticEnterpriseCore()
                .BuildServiceProvider();
            var manifest = Assert.Single(
                services.GetRequiredService<IGenerationPluginRegistry>().GetDiscoveredManifests(new[] { tempRoot }),
                candidate => candidate.Capability == "PipeOverflow");
            var scenario = MinimalScenario("Pipe Overflow Co");
            var world = services.GetRequiredService<IWorldGenerator>()
                .Generate(new GenerationContext { Scenario = scenario }, new CatalogSet())
                .World;

            var result = new OutOfProcessAssemblyExternalPluginHostAdapter().Execute(
                manifest,
                world,
                new GenerationContext
                {
                    Scenario = scenario,
                    ExternalPlugins = new ExternalPluginExecutionSettings
                    {
                        ExecutionTimeoutSeconds = 10,
                        MaxOutputPayloadBytes = 1024
                    }
                },
                new CatalogSet());

            Assert.False(result.Executed);
            Assert.Contains(result.Warnings, warning => warning.Contains("Plugin output exceeded the configured limit of 1024 bytes.", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void PowerShellHost_Rejects_Oversized_Population_Input_Before_Script_Execution()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            File.WriteAllText(Path.Combine(tempRoot, "population.generator.json"), """
                {
                  "capability": "PopulationPowerShell",
                  "displayName": "Population PowerShell Plugin",
                  "executionMode": "PowerShellScript",
                  "entryPoint": "population.plugin.ps1",
                  "security": {
                    "dataOnly": true,
                    "requestedCapabilities": [ "GenerateData" ]
                  }
                }
                """);
            File.WriteAllText(Path.Combine(tempRoot, "population.plugin.ps1"), """
                New-PluginResult -Records @() -Warnings @('script-executed')
                """);

            using var services = new ServiceCollection()
                .AddSyntheticEnterpriseCore()
                .BuildServiceProvider();
            var manifest = Assert.Single(
                services.GetRequiredService<IGenerationPluginRegistry>().GetDiscoveredManifests(new[] { tempRoot }),
                candidate => candidate.Capability == "PopulationPowerShell");
            var scenario = PopulationScenario("Oversized PowerShell Population", 128);
            var world = services.GetRequiredService<IWorldGenerator>()
                .Generate(new GenerationContext { Scenario = scenario }, new CatalogSet())
                .World;
            Assert.NotEmpty(world.ManagementObservations);

            var result = services.GetServices<IExternalPluginHostAdapter>()
                .OfType<RestrictedPowerShellExternalPluginHostAdapter>()
                .Single()
                .Execute(
                    manifest,
                    world,
                    new GenerationContext
                    {
                        Scenario = scenario,
                        ExternalPlugins = new ExternalPluginExecutionSettings { MaxInputPayloadBytes = 1024 }
                    },
                    new CatalogSet());

            Assert.False(result.Executed);
            Assert.Contains(result.Warnings, warning => warning.Contains("Input payload exceeded the configured limit of 1024 bytes.", StringComparison.Ordinal));
            Assert.DoesNotContain(result.Warnings, warning => warning.Contains("script-executed", StringComparison.Ordinal));
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
    public void PowerShellHost_Executes_Population_Input_At_Configured_Byte_Boundary()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            File.WriteAllText(Path.Combine(tempRoot, "boundary.generator.json"), """
                {
                  "capability": "BoundaryPowerShell",
                  "displayName": "Boundary PowerShell Plugin",
                  "executionMode": "PowerShellScript",
                  "entryPoint": "boundary.plugin.ps1",
                  "security": {
                    "dataOnly": true,
                    "requestedCapabilities": [ "GenerateData" ]
                  }
                }
                """);
            File.WriteAllText(Path.Combine(tempRoot, "boundary.plugin.ps1"), """
                New-PluginResult -Records @() -Warnings @('script-executed')
                """);

            using var services = new ServiceCollection()
                .AddSyntheticEnterpriseCore()
                .BuildServiceProvider();
            var manifest = Assert.Single(
                services.GetRequiredService<IGenerationPluginRegistry>().GetDiscoveredManifests(new[] { tempRoot }),
                candidate => candidate.Capability == "BoundaryPowerShell");
            var scenario = PopulationScenario("Boundary PowerShell Population", 16);
            var world = services.GetRequiredService<IWorldGenerator>()
                .Generate(new GenerationContext { Scenario = scenario }, new CatalogSet())
                .World;
            var context = new GenerationContext { Scenario = scenario };
            var inputBytes = GetPowerShellInputPayloadBytes(manifest, world, context);

            var result = services.GetServices<IExternalPluginHostAdapter>()
                .OfType<RestrictedPowerShellExternalPluginHostAdapter>()
                .Single()
                .Execute(
                    manifest,
                    world,
                    new GenerationContext
                    {
                        Scenario = scenario,
                        ExternalPlugins = new ExternalPluginExecutionSettings { MaxInputPayloadBytes = inputBytes }
                    },
                    new CatalogSet());

            Assert.True(result.Executed, string.Join(Environment.NewLine, result.Warnings));
            Assert.Contains(result.Warnings, warning => warning.Contains("script-executed", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(".json")]
    [InlineData(".txt")]
    [InlineData(".csv")]
    public void PluginCatalogLoader_Rejects_Each_Catalog_File_Above_Configured_Limit(string extension)
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            var catalogPath = Path.Combine(tempRoot, $"oversized{extension}");
            File.WriteAllText(catalogPath, CreateCatalogContent(extension, 1025));
            var manifest = CreateCatalogManifest(tempRoot, "OversizedCatalog", catalogPath);

            Assert.Throws<PluginInputPayloadLimitExceededException>(() =>
                ExternalPluginCatalogLoader.LoadPluginCatalogs(
                    manifest,
                    new ExternalPluginExecutionSettings { MaxInputPayloadBytes = 1024 }));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData(".json")]
    [InlineData(".txt")]
    [InlineData(".csv")]
    public void PluginCatalogLoader_Accepts_Each_Catalog_File_At_Configured_Limit(string extension)
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            var catalogPath = Path.Combine(tempRoot, $"boundary{extension}");
            File.WriteAllText(catalogPath, CreateCatalogContent(extension, 1024));
            var manifest = CreateCatalogManifest(tempRoot, "BoundaryCatalog", catalogPath);

            var catalogs = ExternalPluginCatalogLoader.LoadPluginCatalogs(
                manifest,
                new ExternalPluginExecutionSettings { MaxInputPayloadBytes = 1024 });

            Assert.True(catalogs.CsvCatalogs.Count + catalogs.JsonCatalogs.Count > 0);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void PluginCatalogLoader_Rejects_Cumulative_Catalog_Bytes_Above_Configured_Limit()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            var firstPath = Path.Combine(tempRoot, "first.txt");
            var secondPath = Path.Combine(tempRoot, "second.txt");
            File.WriteAllText(firstPath, new string('A', 600));
            File.WriteAllText(secondPath, new string('B', 425));
            var manifest = CreateCatalogManifest(tempRoot, "CumulativeCatalog", firstPath, secondPath);

            Assert.Throws<PluginInputPayloadLimitExceededException>(() =>
                ExternalPluginCatalogLoader.LoadPluginCatalogs(
                    manifest,
                    new ExternalPluginExecutionSettings { MaxInputPayloadBytes = 1024 }));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void PluginCatalogLoader_Accepts_Cumulative_Catalog_Bytes_At_Configured_Limit()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            var firstPath = Path.Combine(tempRoot, "first.txt");
            var secondPath = Path.Combine(tempRoot, "second.txt");
            File.WriteAllText(firstPath, new string('A', 512));
            File.WriteAllText(secondPath, new string('B', 512));
            var manifest = CreateCatalogManifest(tempRoot, "CumulativeCatalogBoundary", firstPath, secondPath);

            var catalogs = ExternalPluginCatalogLoader.LoadPluginCatalogs(
                manifest,
                new ExternalPluginExecutionSettings { MaxInputPayloadBytes = 1024 });

            Assert.Equal(2, catalogs.CsvCatalogs.Count);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void PluginCatalogLoader_Uses_Defensive_File_Cap_When_Request_Limit_Is_Larger()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            var catalogPath = Path.Combine(tempRoot, "defensive-cap.txt");
            using (var stream = File.Create(catalogPath))
            {
                stream.SetLength(ExternalPluginCatalogLoader.MaximumCatalogFileBytes + 1L);
            }
            var manifest = CreateCatalogManifest(tempRoot, "DefensiveCatalogCap", catalogPath);

            Assert.Throws<PluginInputPayloadLimitExceededException>(() =>
                ExternalPluginCatalogLoader.LoadPluginCatalogs(
                    manifest,
                    new ExternalPluginExecutionSettings { MaxInputPayloadBytes = int.MaxValue }));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void BoundedCatalogStream_Rejects_Actual_Bytes_Appended_After_Length_Check()
    {
        using var source = new MemoryStream();
        source.Write(new byte[1024]);
        var checkedLength = source.Length;
        source.WriteByte(1);
        source.Position = 0;
        var budget = new PluginInputByteBudget(1024);
        using var bounded = new BoundedPluginCatalogReadStream(source, budget, 1024);

        Assert.Equal(1024, checkedLength);
        Assert.Throws<PluginInputPayloadLimitExceededException>(() =>
        {
            var buffer = new byte[256];
            while (bounded.Read(buffer, 0, buffer.Length) > 0)
            {
            }
        });
    }

    [Fact]
    public void PowerShellHost_Rejects_Oversized_Catalog_Before_Script_Execution()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            File.WriteAllText(Path.Combine(tempRoot, "catalog.generator.json"), """
                {
                  "capability": "OversizedCatalogPowerShell",
                  "displayName": "Oversized Catalog PowerShell Plugin",
                  "executionMode": "PowerShellScript",
                  "entryPoint": "catalog.plugin.ps1",
                  "localDataPaths": [ "oversized.json" ],
                  "security": {
                    "dataOnly": true,
                    "requestedCapabilities": [ "GenerateData", "ReadPluginData" ]
                  }
                }
                """);
            File.WriteAllText(Path.Combine(tempRoot, "catalog.plugin.ps1"), """
                New-PluginResult -Records @() -Warnings @('script-executed')
                """);
            File.WriteAllText(Path.Combine(tempRoot, "oversized.json"),
                CreateCatalogContent(".json", 1025));

            using var services = new ServiceCollection()
                .AddSyntheticEnterpriseCore()
                .BuildServiceProvider();
            var manifest = Assert.Single(
                services.GetRequiredService<IGenerationPluginRegistry>().GetDiscoveredManifests(new[] { tempRoot }),
                candidate => candidate.Capability == "OversizedCatalogPowerShell");
            var scenario = MinimalScenario("Oversized Catalog Co");
            var world = services.GetRequiredService<IWorldGenerator>()
                .Generate(new GenerationContext { Scenario = scenario }, new CatalogSet())
                .World;

            var result = services.GetServices<IExternalPluginHostAdapter>()
                .OfType<RestrictedPowerShellExternalPluginHostAdapter>()
                .Single()
                .Execute(
                    manifest,
                    world,
                    new GenerationContext
                    {
                        Scenario = scenario,
                        ExternalPlugins = new ExternalPluginExecutionSettings { MaxInputPayloadBytes = 1024 }
                    },
                    new CatalogSet());

            Assert.False(result.Executed);
            Assert.Contains(result.Warnings, warning => warning.Contains("Input payload exceeded", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(result.Warnings, warning => warning.Contains("script-executed", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void PowerShellHost_Stops_When_Output_Crosses_Configured_Limit()
    {
        var result = ExecutePowerShellStreamOverflowPlugin(
            "OutputOverflow",
            "1..20000 | ForEach-Object { Write-Output ('O' * 256) }",
            maxOutputPayloadBytes: 1024);

        Assert.False(result.Executed);
        Assert.Contains(result.Warnings, warning => warning.Contains("Plugin output exceeded the configured limit of 1024 bytes.", StringComparison.Ordinal));
    }

    [Fact]
    public void PowerShellHost_Stops_When_Diagnostic_Stream_Crosses_Configured_Limit()
    {
        var result = ExecutePowerShellStreamOverflowPlugin(
            "DiagnosticOverflow",
            "1..20000 | ForEach-Object { Write-Warning ('W' * 256) }",
            maxOutputPayloadBytes: 1024);

        Assert.False(result.Executed);
        Assert.Contains(result.Warnings, warning => warning.Contains("Plugin output exceeded the configured limit of 1024 bytes.", StringComparison.Ordinal));
    }

    [Fact]
    public void PowerShellHost_Drains_Many_Tiny_Records_While_Retaining_Only_Configured_Count()
    {
        var result = ExecutePowerShellStreamOverflowPlugin(
            "TinyRecordRetention",
            "1..5000 | ForEach-Object { New-PluginRecord -RecordType 'Tiny' -AssociatedEntityType 'Company' -AssociatedEntityId 'COMP-1' -Properties @{} }",
            maxOutputPayloadBytes: 64 * 1024 * 1024,
            maxGeneratedRecords: 5);

        Assert.True(result.Executed, string.Join(Environment.NewLine, result.Warnings));
        Assert.Equal(5, result.Records.Count);
        Assert.Contains(result.Warnings, warning => warning.Contains("truncated from 5000 to 5", StringComparison.Ordinal));
    }

    [Fact]
    public void PowerShellHost_Bounds_Record_Work_Inside_A_Single_Result_Envelope()
    {
        var result = ExecutePowerShellStreamOverflowPlugin(
            "EnvelopeRecordRetention",
            "$records = @(1..5000 | ForEach-Object { New-PluginRecord -RecordType 'Tiny' -AssociatedEntityType 'Company' -AssociatedEntityId 'COMP-1' -Properties @{} }); New-PluginResult -Records $records -Warnings @()",
            maxOutputPayloadBytes: 64 * 1024 * 1024,
            maxGeneratedRecords: 5);

        Assert.True(result.Executed, string.Join(Environment.NewLine, result.Warnings));
        Assert.Equal(5, result.Records.Count);
        Assert.Contains(result.Warnings, warning => warning.Contains("truncated from at least 6 to 5", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Warnings, warning => warning.Contains("5000", StringComparison.Ordinal));
    }

    [Fact]
    public void PowerShellHost_Preserves_Warnings_When_Record_Retention_Is_Zero()
    {
        var result = ExecutePowerShellStreamOverflowPlugin(
            "WarningOnlyRetention",
            "New-PluginResult -Records @() -Warnings @('warning-only')",
            maxOutputPayloadBytes: 1024 * 1024,
            maxGeneratedRecords: 0);

        Assert.True(result.Executed, string.Join(Environment.NewLine, result.Warnings));
        Assert.Empty(result.Records);
        Assert.Contains("warning-only", result.Warnings);
    }

    [Fact]
    public void PowerShellHost_Does_Not_Append_Truncation_Notice_When_Warning_Limit_Is_Zero()
    {
        var result = ExecutePowerShellStreamOverflowPlugin(
            "ZeroWarningLimit",
            "$records = @(1..10 | ForEach-Object { New-PluginRecord -RecordType 'Tiny' -AssociatedEntityType 'Company' -AssociatedEntityId 'COMP-1' -Properties @{} }); New-PluginResult -Records $records -Warnings @('plugin-warning')",
            maxOutputPayloadBytes: 1024 * 1024,
            maxGeneratedRecords: 1,
            maxWarningCount: 0);

        Assert.True(result.Executed, string.Join(Environment.NewLine, result.Warnings));
        Assert.Single(result.Records);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void PowerShellHost_Truncation_Notice_Remains_Within_Exact_Output_Payload_Boundary()
    {
        const string script = "$records = @(1..2 | ForEach-Object { New-PluginRecord -RecordType 'Tiny' -AssociatedEntityType 'Company' -AssociatedEntityId 'COMP-1' -Properties @{} }); New-PluginResult -Records $records -Warnings @()";
        var reference = ExecutePowerShellStreamOverflowPlugin(
            "ExactPowerShellOutput",
            script,
            maxOutputPayloadBytes: 1024 * 1024,
            maxGeneratedRecords: 1,
            maxWarningCount: 1);
        var exactPayloadBytes = JsonSerializer.SerializeToUtf8Bytes(reference, PluginJsonOptions).Length;

        var result = ExecutePowerShellStreamOverflowPlugin(
            "ExactPowerShellOutput",
            script,
            maxOutputPayloadBytes: exactPayloadBytes,
            maxGeneratedRecords: 1,
            maxWarningCount: 1);

        Assert.True(result.Executed, string.Join(Environment.NewLine, result.Warnings));
        Assert.True(JsonSerializer.SerializeToUtf8Bytes(result, PluginJsonOptions).Length <= exactPayloadBytes);
        Assert.True(result.Warnings.Count <= 1);
    }

    [Theory]
    [InlineData(".txt")]
    [InlineData(".csv")]
    public void CatalogLoader_Rejects_Text_Row_Amplification_At_Defensive_Limit(string extension)
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            var manifestPath = Path.Combine(tempRoot, "rows.generator.json");
            var catalogPath = Path.Combine(tempRoot, $"rows{extension}");
            File.WriteAllText(manifestPath, "{}");
            var header = extension == ".csv" ? "Value\n" : string.Empty;
            File.WriteAllText(
                catalogPath,
                header + string.Join('\n', Enumerable.Repeat("x", ExternalPluginCatalogLoader.MaximumCatalogRows + 1)));
            var manifest = CreateCatalogManifest(tempRoot, "Rows", catalogPath);

            var exception = Assert.Throws<PluginPathSecurityException>(() =>
                ExternalPluginCatalogLoader.LoadPluginCatalogs(
                    manifest,
                    new ExternalPluginExecutionSettings { MaxInputPayloadBytes = 1024 * 1024 }));

            Assert.Contains("parsed-row limit", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void CatalogLoader_Accepts_Text_At_Defensive_Row_Boundary()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            var manifestPath = Path.Combine(tempRoot, "rows.generator.json");
            var catalogPath = Path.Combine(tempRoot, "rows.txt");
            File.WriteAllText(manifestPath, "{}");
            File.WriteAllText(
                catalogPath,
                string.Join('\n', Enumerable.Repeat("x", ExternalPluginCatalogLoader.MaximumCatalogRows)));
            var manifest = CreateCatalogManifest(tempRoot, "Rows", catalogPath);

            var catalogs = ExternalPluginCatalogLoader.LoadPluginCatalogs(
                manifest,
                new ExternalPluginExecutionSettings { MaxInputPayloadBytes = 1024 * 1024 });

            Assert.Equal(ExternalPluginCatalogLoader.MaximumCatalogRows, catalogs.CsvCatalogs["rows"].Count);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void PowerShellHost_Rejects_Catalog_Row_Amplification_Before_Script_Execution()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            File.WriteAllText(Path.Combine(tempRoot, "rowlimit.generator.json"), """
                {
                  "capability": "RowLimit",
                  "displayName": "Row Limit",
                  "executionMode": "PowerShellScript",
                  "entryPoint": "rowlimit.plugin.ps1",
                  "localDataPaths": [ "rows.txt" ],
                  "security": {
                    "dataOnly": true,
                    "requestedCapabilities": [ "GenerateData", "ReadPluginData" ]
                  }
                }
                """);
            File.WriteAllText(
                Path.Combine(tempRoot, "rows.txt"),
                string.Join('\n', Enumerable.Repeat("x", ExternalPluginCatalogLoader.MaximumCatalogRows + 1)));
            File.WriteAllText(
                Path.Combine(tempRoot, "rowlimit.plugin.ps1"),
                "New-PluginResult -Records @() -Warnings @('script-executed')");

            using var services = new ServiceCollection()
                .AddSyntheticEnterpriseCore()
                .BuildServiceProvider();
            var manifest = Assert.Single(
                services.GetRequiredService<IGenerationPluginRegistry>().GetDiscoveredManifests(new[] { tempRoot }),
                candidate => candidate.Capability == "RowLimit");
            var scenario = MinimalScenario("Row Limit Co");
            var world = services.GetRequiredService<IWorldGenerator>()
                .Generate(new GenerationContext { Scenario = scenario }, new CatalogSet())
                .World;

            var result = services.GetServices<IExternalPluginHostAdapter>()
                .OfType<RestrictedPowerShellExternalPluginHostAdapter>()
                .Single()
                .Execute(
                    manifest,
                    world,
                    new GenerationContext
                    {
                        Scenario = scenario,
                        ExternalPlugins = new ExternalPluginExecutionSettings { MaxInputPayloadBytes = 1024 * 1024 }
                    },
                    new CatalogSet());

            Assert.False(result.Executed);
            Assert.Contains(result.Warnings, warning => warning.Contains("parsed-row limit", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(result.Warnings, warning => warning.Contains("script-executed", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void PowerShellHost_Rejects_Catalog_Swapped_After_Adapter_Validation()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            var catalogPath = Path.Combine(tempRoot, "swap.txt");
            File.WriteAllText(catalogPath, "approved");
            File.WriteAllText(Path.Combine(tempRoot, "catalogswap.generator.json"), """
                {
                  "capability": "PowerShellCatalogSwap",
                  "displayName": "PowerShell Catalog Swap",
                  "executionMode": "PowerShellScript",
                  "entryPoint": "catalogswap.plugin.ps1",
                  "localDataPaths": [ "swap.txt" ],
                  "security": {
                    "dataOnly": true,
                    "requestedCapabilities": [ "GenerateData", "ReadPluginData" ]
                  }
                }
                """);
            File.WriteAllText(
                Path.Combine(tempRoot, "catalogswap.plugin.ps1"),
                "New-PluginResult -Records @() -Warnings @('plugin-executed')");

            using var services = new ServiceCollection()
                .AddSyntheticEnterpriseCore()
                .BuildServiceProvider();
            var manifest = Assert.Single(
                services.GetRequiredService<IGenerationPluginRegistry>().GetDiscoveredManifests(new[] { tempRoot }),
                candidate => candidate.Capability == "PowerShellCatalogSwap");
            var scenario = MinimalScenario("PowerShell Catalog Swap Co");
            var world = services.GetRequiredService<IWorldGenerator>()
                .Generate(new GenerationContext { Scenario = scenario }, new CatalogSet())
                .World;
            var provider = new CatalogReplacingProvider(catalogPath);

            var result = new RestrictedPowerShellExternalPluginHostAdapter(
                services.GetRequiredService<IIdFactory>(),
                provider).Execute(
                    manifest,
                    world,
                    new GenerationContext { Scenario = scenario },
                    new CatalogSet());

            Assert.True(provider.ReplacedBeforeLoad);
            Assert.False(result.Executed);
            Assert.Contains(result.Warnings, warning => warning.Contains("catalog hash no longer matches", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(result.Warnings, warning => warning.Contains("plugin-executed", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void CatalogLoader_Rejects_Json_Beyond_Explicit_Depth_Limit()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            var manifestPath = Path.Combine(tempRoot, "depth.generator.json");
            var catalogPath = Path.Combine(tempRoot, "depth.json");
            File.WriteAllText(manifestPath, "{}");
            File.WriteAllText(
                catalogPath,
                new string('[', ExternalPluginCatalogLoader.MaximumJsonDepth + 1)
                + "0"
                + new string(']', ExternalPluginCatalogLoader.MaximumJsonDepth + 1));
            var manifest = CreateCatalogManifest(tempRoot, "Depth", catalogPath);

            var exception = Assert.Throws<PluginPathSecurityException>(() =>
                ExternalPluginCatalogLoader.LoadPluginCatalogs(manifest, new ExternalPluginExecutionSettings()));

            Assert.Contains("maximum depth", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData("oversized.generator.json")]
    [InlineData("oversized.Generator.psd1")]
    public void PluginDiscovery_Rejects_Manifest_Above_Defensive_Byte_Limit_With_Diagnostic(string fileName)
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            var manifestPath = Path.Combine(tempRoot, fileName);
            using (var stream = File.Create(manifestPath))
            {
                stream.SetLength(FileSystemExternalGenerationPluginCatalog.MaximumManifestFileBytes + 1L);
            }

            using var services = new ServiceCollection()
                .AddSyntheticEnterpriseCore()
                .BuildServiceProvider();
            var catalog = services.GetRequiredService<IExternalGenerationPluginCatalog>();

            Assert.Empty(catalog.Discover(tempRoot));
            var inspection = Assert.Single(catalog.Inspect(
                new[] { tempRoot },
                new ExternalPluginExecutionSettings()));
            Assert.False(inspection.Parsed);
            Assert.Contains(inspection.ValidationMessages, message =>
                message.Contains("defensive manifest limit", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void PluginDiscovery_Rejects_Json_Manifest_Beyond_Explicit_Depth_Limit_With_Diagnostic()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            var depth = FileSystemExternalGenerationPluginCatalog.MaximumManifestJsonDepth + 1;
            File.WriteAllText(
                Path.Combine(tempRoot, "deep.generator.json"),
                "{\"capability\":\"DeepManifest\",\"nested\":"
                + new string('[', depth)
                + "0"
                + new string(']', depth)
                + "}");

            using var services = new ServiceCollection()
                .AddSyntheticEnterpriseCore()
                .BuildServiceProvider();
            var catalog = services.GetRequiredService<IExternalGenerationPluginCatalog>();

            Assert.Empty(catalog.Discover(tempRoot));
            var inspection = Assert.Single(catalog.Inspect(
                new[] { tempRoot },
                new ExternalPluginExecutionSettings()));
            Assert.False(inspection.Parsed);
            Assert.Contains(inspection.ValidationMessages, message =>
                message.Contains("maximum JSON depth", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void PluginDiscovery_Reports_Shallow_Invalid_Json_As_Parse_Failure_Not_Depth_Overflow()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            File.WriteAllText(Path.Combine(tempRoot, "broken.generator.json"), "{ not-json");

            using var services = new ServiceCollection()
                .AddSyntheticEnterpriseCore()
                .BuildServiceProvider();
            var catalog = services.GetRequiredService<IExternalGenerationPluginCatalog>();

            var inspection = Assert.Single(catalog.Inspect(
                new[] { tempRoot },
                new ExternalPluginExecutionSettings()));

            Assert.False(inspection.Parsed);
            Assert.Contains(inspection.ValidationMessages, message =>
                message.Contains("could not be parsed", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(inspection.ValidationMessages, message =>
                message.Contains("maximum JSON depth", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void PluginDiscovery_Accepts_Json_Manifest_At_Defensive_Byte_Boundary()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            const string prefix = "{\"capability\":\"JsonManifestBoundary\",\"description\":\"";
            const string suffix = "\"}";
            var paddingLength = FileSystemExternalGenerationPluginCatalog.MaximumManifestFileBytes
                                - prefix.Length
                                - suffix.Length;
            var content = prefix + new string('X', paddingLength) + suffix;
            Assert.Equal(
                FileSystemExternalGenerationPluginCatalog.MaximumManifestFileBytes,
                System.Text.Encoding.UTF8.GetByteCount(content));
            File.WriteAllText(Path.Combine(tempRoot, "boundary.generator.json"), content);

            using var services = new ServiceCollection()
                .AddSyntheticEnterpriseCore()
                .BuildServiceProvider();
            var catalog = services.GetRequiredService<IExternalGenerationPluginCatalog>();

            var manifest = Assert.Single(catalog.Discover(tempRoot));
            Assert.Equal("JsonManifestBoundary", manifest.Capability);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void PluginDiscovery_Accepts_Legacy_Manifest_At_Defensive_Byte_Boundary()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            File.WriteAllText(Path.Combine(tempRoot, "boundary.ps1"), "New-PluginResult -Records @() -Warnings @()");
            const string prefix = "@{\nFriendlyName = 'LegacyManifestBoundary'\nRootModule = 'boundary.ps1'\n}\n#";
            var content = prefix + new string('X',
                FileSystemExternalGenerationPluginCatalog.MaximumManifestFileBytes - prefix.Length);
            Assert.Equal(
                FileSystemExternalGenerationPluginCatalog.MaximumManifestFileBytes,
                System.Text.Encoding.UTF8.GetByteCount(content));
            File.WriteAllText(Path.Combine(tempRoot, "boundary.Generator.psd1"), content);

            using var services = new ServiceCollection()
                .AddSyntheticEnterpriseCore()
                .BuildServiceProvider();
            var catalog = services.GetRequiredService<IExternalGenerationPluginCatalog>();

            var manifest = Assert.Single(catalog.Discover(tempRoot));
            Assert.Equal("LegacyManifestBoundary", manifest.Capability);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void PluginDiscovery_Rejects_Package_Root_That_Traverses_A_Reparse_Point()
    {
        var tempRoot = CreateTempDirectory();
        var realRoot = Path.Combine(tempRoot, "real");
        var linkedRoot = Path.Combine(tempRoot, "linked");
        Directory.CreateDirectory(realRoot);

        try
        {
            File.WriteAllText(Path.Combine(realRoot, "linked.generator.json"), """
                {
                  "capability": "LinkedRoot",
                  "displayName": "Linked Root",
                  "executionMode": "PowerShellScript",
                  "entryPoint": "linked.plugin.ps1"
                }
                """);
            File.WriteAllText(Path.Combine(realRoot, "linked.plugin.ps1"),
                "New-PluginResult -Records @() -Warnings @('executed')");
            if (!TryCreateDirectorySymbolicLink(linkedRoot, realRoot))
            {
                return;
            }

            using var services = new ServiceCollection()
                .AddSyntheticEnterpriseCore()
                .BuildServiceProvider();

            Assert.Empty(services.GetRequiredService<IGenerationPluginRegistry>()
                .GetDiscoveredManifests(new[] { linkedRoot }));
        }
        finally
        {
            if (Directory.Exists(linkedRoot))
            {
                Directory.Delete(linkedRoot);
            }

            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void PluginDiscovery_Rejects_Entry_Point_That_Traverses_A_Nested_Reparse_Point()
    {
        var tempRoot = CreateTempDirectory();
        var outsideRoot = CreateTempDirectory();
        var linkedDirectory = Path.Combine(tempRoot, "linked");

        try
        {
            File.WriteAllText(Path.Combine(outsideRoot, "escape.plugin.ps1"),
                "New-PluginResult -Records @() -Warnings @('executed')");
            if (!TryCreateDirectorySymbolicLink(linkedDirectory, outsideRoot))
            {
                return;
            }

            File.WriteAllText(Path.Combine(tempRoot, "escape.generator.json"), """
                {
                  "capability": "ReparseEscape",
                  "displayName": "Reparse Escape",
                  "executionMode": "PowerShellScript",
                  "entryPoint": "linked/escape.plugin.ps1"
                }
                """);

            using var services = new ServiceCollection()
                .AddSyntheticEnterpriseCore()
                .BuildServiceProvider();

            Assert.Empty(services.GetRequiredService<IGenerationPluginRegistry>()
                .GetDiscoveredManifests(new[] { tempRoot }));
        }
        finally
        {
            if (Directory.Exists(linkedDirectory))
            {
                Directory.Delete(linkedDirectory);
            }

            Directory.Delete(tempRoot, recursive: true);
            Directory.Delete(outsideRoot, recursive: true);
        }
    }

    [Fact]
    public void OpenedPackageFile_Rejects_Handle_Target_Outside_Approved_Package_Path()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            return;
        }

        var packageRoot = CreateTempDirectory();
        var outsideRoot = CreateTempDirectory();

        try
        {
            var expectedPath = Path.Combine(packageRoot, "catalog.txt");
            var outsidePath = Path.Combine(outsideRoot, "catalog.txt");
            File.WriteAllText(expectedPath, "approved");
            File.WriteAllText(outsidePath, "outside");
            using var outsideHandle = new FileStream(outsidePath, FileMode.Open, FileAccess.Read, FileShare.Read);

            Assert.False(ExternalPluginPathSecurity.TryValidateOpenedPackageFile(
                outsideHandle,
                packageRoot,
                expectedPath,
                out var warning));
            Assert.Contains("instead of the approved package path", warning, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(packageRoot, recursive: true);
            Directory.Delete(outsideRoot, recursive: true);
        }
    }

    [Fact]
    public void PluginDiscovery_Rejects_Entry_Script_Above_Approved_Package_File_Limit()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            File.WriteAllText(Path.Combine(tempRoot, "large.generator.json"), """
                {
                  "capability": "LargeScript",
                  "displayName": "Large Script",
                  "executionMode": "PowerShellScript",
                  "entryPoint": "large.plugin.ps1"
                }
                """);
            using (var stream = File.Create(Path.Combine(tempRoot, "large.plugin.ps1")))
            {
                stream.SetLength(ExternalPluginPathSecurity.MaximumEntryPointBytes + 1L);
            }

            using var services = new ServiceCollection()
                .AddSyntheticEnterpriseCore()
                .BuildServiceProvider();

            Assert.Empty(services.GetRequiredService<IGenerationPluginRegistry>()
                .GetDiscoveredManifests(new[] { tempRoot }));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void WorldGenerator_Preserves_Bounded_Management_History_Contract_In_Script_Plugin_Input()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            File.WriteAllText(Path.Combine(tempRoot, "managementhistory.generator.json"), """
                {
                  "capability": "ManagementHistoryContract",
                  "displayName": "Management History Contract",
                  "executionMode": "PowerShellScript",
                  "entryPoint": "managementhistory.plugin.ps1",
                  "security": {
                    "dataOnly": true,
                    "requestedCapabilities": [ "GenerateData" ]
                  }
                }
                """);
            File.WriteAllText(Path.Combine(tempRoot, "managementhistory.plugin.ps1"), """
                $currentRows = @($InputWorld.ManagementObservations | Where-Object { $_.IsCurrent })
                $historyRows = @($InputWorld.ManagementObservations | Where-Object { -not $_.IsCurrent })
                $historical = $historyRows[0]
                $superseding = @($currentRows | Where-Object { $_.Id -eq $historical.SupersededByObservationId })[0]
                $record = New-PluginRecord -RecordType 'ManagementHistoryContract' -AssociatedEntityType 'Company' -AssociatedEntityId $InputWorld.Companies[0].Id -Properties @{
                  CurrentCount = [string]$currentRows.Count
                  HistoryCount = [string]$historyRows.Count
                  HistoricalLifecycle = [string]$historical.LifecycleState
                  SupersedingLifecycle = [string]$superseding.LifecycleState
                  RegistrationIdentityStable = [string]($historical.RegistrationId -eq $superseding.RegistrationId)
                }
                New-PluginResult -Records @($record) -Warnings @('management-history-contract-ok')
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
                        Name = "Management history plugin boundary",
                        Infrastructure = new InfrastructureProfile
                        {
                            IncludeServers = true,
                            IncludeWorkstations = true,
                            IncludeNetworkAssets = false,
                            IncludeTelephony = false,
                            IncludeRepresentativeManagementObservations = true,
                            RepresentativeManagementObservationCount = 1,
                            RepresentativeManagementHistoryObservationCount = 1,
                        },
                        Companies = new()
                        {
                            new ScenarioCompanyDefinition
                            {
                                Name = "Management History Co",
                                Industry = "Technology",
                                EmployeeCount = 6,
                                OfficeCount = 1,
                                ServerCount = 2,
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
                        EnabledCapabilities = new() { "ManagementHistoryContract" },
                        MaxInputPayloadBytes = 4 * 1024 * 1024,
                    },
                },
                new CatalogSet());

            var record = Assert.Single(result.World.PluginRecords, candidate =>
                candidate.RecordType == "ManagementHistoryContract");
            Assert.Equal("1", record.Properties["CurrentCount"]);
            Assert.Equal("1", record.Properties["HistoryCount"]);
            Assert.Equal("Historical", record.Properties["HistoricalLifecycle"]);
            Assert.Equal("Current", record.Properties["SupersedingLifecycle"]);
            Assert.Equal("True", record.Properties["RegistrationIdentityStable"]);
            Assert.DoesNotContain(result.Warnings, warning =>
                warning.Contains("Input payload exceeded", StringComparison.OrdinalIgnoreCase));
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
    public void AssemblyHost_Rejects_Oversized_Population_Input_Before_Host_Execution()
    {
        var tempRoot = CreateTempDirectory();
        var executionMarker = Path.Combine(tempRoot, "assembly-executed.marker");
        var sourceMarkerPath = executionMarker.Replace("\\", "\\\\");

        try
        {
            WriteAssemblyPluginProject(tempRoot, "populationinput", $$"""
                using SyntheticEnterprise.Contracts.Plugins;

                public sealed class PopulationInputPlugin : IExternalGenerationAssemblyPlugin
                {
                    public string Capability => "PopulationAssembly";

                    public ExternalPluginExecutionResponse Execute(ExternalPluginExecutionRequest request)
                    {
                        File.WriteAllText("{{sourceMarkerPath}}", "executed");
                        return new ExternalPluginExecutionResponse { Executed = true };
                    }
                }
                """);
            BuildAssemblyPlugin(tempRoot, "populationinput");
            File.WriteAllText(Path.Combine(tempRoot, "populationinput.generator.json"), """
                {
                  "capability": "PopulationAssembly",
                  "displayName": "Population Assembly Plugin",
                  "executionMode": "DotNetAssembly",
                  "entryPoint": "bin/Debug/net8.0/populationinput.dll",
                  "security": {
                    "dataOnly": true,
                    "requestedCapabilities": [ "GenerateData" ]
                  }
                }
                """);

            using var services = new ServiceCollection()
                .AddSyntheticEnterpriseCore()
                .BuildServiceProvider();
            var manifest = Assert.Single(
                services.GetRequiredService<IGenerationPluginRegistry>().GetDiscoveredManifests(new[] { tempRoot }),
                candidate => candidate.Capability == "PopulationAssembly");
            var scenario = PopulationScenario("Oversized Assembly Population", 128);
            var world = services.GetRequiredService<IWorldGenerator>()
                .Generate(new GenerationContext { Scenario = scenario }, new CatalogSet())
                .World;
            Assert.NotEmpty(world.ManagementObservations);

            var result = new OutOfProcessAssemblyExternalPluginHostAdapter().Execute(
                manifest,
                world,
                new GenerationContext
                {
                    Scenario = scenario,
                    ExternalPlugins = new ExternalPluginExecutionSettings { MaxInputPayloadBytes = 1024 }
                },
                new CatalogSet());

            Assert.False(result.Executed);
            Assert.False(File.Exists(executionMarker));
            Assert.Contains(result.Warnings, warning => warning.Contains("Input payload exceeded the configured limit of 1024 bytes.", StringComparison.Ordinal));
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
    public void AssemblyHost_Rejects_Oversized_Catalog_Before_Plugin_Execution()
    {
        var tempRoot = CreateTempDirectory();
        var executionMarker = Path.Combine(tempRoot, "catalog-assembly-executed.marker");
        var sourceMarkerPath = executionMarker.Replace("\\", "\\\\");

        try
        {
            WriteAssemblyPluginProject(tempRoot, "cataloginput", $$"""
                using SyntheticEnterprise.Contracts.Plugins;

                public sealed class CatalogInputPlugin : IExternalGenerationAssemblyPlugin
                {
                    public string Capability => "CatalogAssembly";

                    public ExternalPluginExecutionResponse Execute(ExternalPluginExecutionRequest request)
                    {
                        File.WriteAllText("{{sourceMarkerPath}}", "executed");
                        return new ExternalPluginExecutionResponse { Executed = true };
                    }
                }
                """);
            BuildAssemblyPlugin(tempRoot, "cataloginput");
            File.WriteAllText(Path.Combine(tempRoot, "catalog.json"), CreateCatalogContent(".json", 1025));
            File.WriteAllText(Path.Combine(tempRoot, "cataloginput.generator.json"), """
                {
                  "capability": "CatalogAssembly",
                  "displayName": "Catalog Assembly",
                  "executionMode": "DotNetAssembly",
                  "entryPoint": "bin/Debug/net8.0/cataloginput.dll",
                  "localDataPaths": [ "catalog.json" ],
                  "security": {
                    "dataOnly": true,
                    "requestedCapabilities": [ "GenerateData", "ReadPluginData" ]
                  }
                }
                """);

            using var services = new ServiceCollection()
                .AddSyntheticEnterpriseCore()
                .BuildServiceProvider();
            var manifest = Assert.Single(
                services.GetRequiredService<IGenerationPluginRegistry>().GetDiscoveredManifests(new[] { tempRoot }),
                candidate => candidate.Capability == "CatalogAssembly");
            var scenario = MinimalScenario("Catalog Assembly Co");
            var world = services.GetRequiredService<IWorldGenerator>()
                .Generate(new GenerationContext { Scenario = scenario }, new CatalogSet())
                .World;

            var result = new OutOfProcessAssemblyExternalPluginHostAdapter().Execute(
                manifest,
                world,
                new GenerationContext
                {
                    Scenario = scenario,
                    ExternalPlugins = new ExternalPluginExecutionSettings { MaxInputPayloadBytes = 1024 }
                },
                new CatalogSet());

            Assert.False(result.Executed);
            Assert.False(File.Exists(executionMarker));
            Assert.Contains(result.Warnings, warning => warning.Contains("Input payload exceeded", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void AssemblyHost_Executes_Population_Input_At_Configured_Byte_Boundary()
    {
        var tempRoot = CreateTempDirectory();
        var executionMarker = Path.Combine(tempRoot, "assembly-boundary-executed.marker");
        var sourceMarkerPath = executionMarker.Replace("\\", "\\\\");

        try
        {
            WriteAssemblyPluginProject(tempRoot, "boundaryinput", $$"""
                using SyntheticEnterprise.Contracts.Plugins;

                public sealed class BoundaryInputPlugin : IExternalGenerationAssemblyPlugin
                {
                    public string Capability => "BoundaryAssembly";

                    public ExternalPluginExecutionResponse Execute(ExternalPluginExecutionRequest request)
                    {
                        File.WriteAllText("{{sourceMarkerPath}}", "executed");
                        return new ExternalPluginExecutionResponse { Executed = true };
                    }
                }
                """);
            BuildAssemblyPlugin(tempRoot, "boundaryinput");
            File.WriteAllText(Path.Combine(tempRoot, "boundaryinput.generator.json"), """
                {
                  "capability": "BoundaryAssembly",
                  "displayName": "Boundary Assembly Plugin",
                  "executionMode": "DotNetAssembly",
                  "entryPoint": "bin/Debug/net8.0/boundaryinput.dll",
                  "security": {
                    "dataOnly": true,
                    "requestedCapabilities": [ "GenerateData" ]
                  }
                }
                """);

            using var services = new ServiceCollection()
                .AddSyntheticEnterpriseCore()
                .BuildServiceProvider();
            var manifest = Assert.Single(
                services.GetRequiredService<IGenerationPluginRegistry>().GetDiscoveredManifests(new[] { tempRoot }),
                candidate => candidate.Capability == "BoundaryAssembly");
            var scenario = PopulationScenario("Boundary Assembly Population", 16);
            var world = services.GetRequiredService<IWorldGenerator>()
                .Generate(new GenerationContext { Scenario = scenario }, new CatalogSet())
                .World;
            var context = new GenerationContext { Scenario = scenario };
            var inputBytes = GetAssemblyInputPayloadBytes(manifest, world, context);

            var result = new OutOfProcessAssemblyExternalPluginHostAdapter().Execute(
                manifest,
                world,
                new GenerationContext
                {
                    Scenario = scenario,
                    ExternalPlugins = new ExternalPluginExecutionSettings { MaxInputPayloadBytes = inputBytes }
                },
                new CatalogSet());

            Assert.True(result.Executed, string.Join(Environment.NewLine, result.Warnings));
            Assert.True(File.Exists(executionMarker));
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
    public void AssemblyHost_Does_Not_Append_Truncation_Notice_When_Warning_Limit_Is_Zero()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            WriteAssemblyPluginProject(tempRoot, "zerowarnings", """
                using SyntheticEnterprise.Contracts.Models;
                using SyntheticEnterprise.Contracts.Plugins;

                public sealed class ZeroWarningsPlugin : IExternalGenerationAssemblyPlugin
                {
                    public string Capability => "ZeroWarnings";

                    public ExternalPluginExecutionResponse Execute(ExternalPluginExecutionRequest request)
                        => new()
                        {
                            Executed = true,
                            Records = new()
                            {
                                new PluginGeneratedRecord { RecordType = "One" },
                                new PluginGeneratedRecord { RecordType = "Two" }
                            },
                            Warnings = new() { "plugin-warning" }
                        };
                }
                """);
            BuildAssemblyPlugin(tempRoot, "zerowarnings");
            File.WriteAllText(Path.Combine(tempRoot, "zerowarnings.generator.json"), """
                {
                  "capability": "ZeroWarnings",
                  "displayName": "Zero Warnings",
                  "executionMode": "DotNetAssembly",
                  "entryPoint": "bin/Debug/net8.0/zerowarnings.dll"
                }
                """);

            using var services = new ServiceCollection()
                .AddSyntheticEnterpriseCore()
                .BuildServiceProvider();
            var manifest = Assert.Single(
                services.GetRequiredService<IGenerationPluginRegistry>().GetDiscoveredManifests(new[] { tempRoot }),
                candidate => candidate.Capability == "ZeroWarnings");
            var scenario = MinimalScenario("Zero Warnings Co");
            var world = services.GetRequiredService<IWorldGenerator>()
                .Generate(new GenerationContext { Scenario = scenario }, new CatalogSet())
                .World;

            var result = new OutOfProcessAssemblyExternalPluginHostAdapter().Execute(
                manifest,
                world,
                new GenerationContext
                {
                    Scenario = scenario,
                    ExternalPlugins = new ExternalPluginExecutionSettings
                    {
                        MaxGeneratedRecords = 1,
                        MaxWarningCount = 0,
                        MaxOutputPayloadBytes = 1024 * 1024
                    }
                },
                new CatalogSet());

            Assert.True(result.Executed, string.Join(Environment.NewLine, result.Warnings));
            Assert.Single(result.Records);
            Assert.Empty(result.Warnings);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void AssemblyHost_Does_Not_Add_Truncation_Notice_Past_Exact_Response_Byte_Boundary()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            var pluginWarning = new string('W', 2048);
            WriteAssemblyPluginProject(tempRoot, "exactoutput", $$"""
                using SyntheticEnterprise.Contracts.Models;
                using SyntheticEnterprise.Contracts.Plugins;

                public sealed class ExactOutputPlugin : IExternalGenerationAssemblyPlugin
                {
                    public string Capability => "ExactOutput";

                    public ExternalPluginExecutionResponse Execute(ExternalPluginExecutionRequest request)
                        => new()
                        {
                            Executed = true,
                            Records = new()
                            {
                                new PluginGeneratedRecord { RecordType = "One" },
                                new PluginGeneratedRecord { RecordType = "Two" }
                            },
                            Warnings = new() { "{{pluginWarning}}" }
                        };
                }
                """);
            BuildAssemblyPlugin(tempRoot, "exactoutput");
            File.WriteAllText(Path.Combine(tempRoot, "exactoutput.generator.json"), """
                {
                  "capability": "ExactOutput",
                  "displayName": "Exact Output",
                  "executionMode": "DotNetAssembly",
                  "entryPoint": "bin/Debug/net8.0/exactoutput.dll"
                }
                """);

            using var services = new ServiceCollection()
                .AddSyntheticEnterpriseCore()
                .BuildServiceProvider();
            var manifest = Assert.Single(
                services.GetRequiredService<IGenerationPluginRegistry>().GetDiscoveredManifests(new[] { tempRoot }),
                candidate => candidate.Capability == "ExactOutput");
            var scenario = MinimalScenario("Exact Output Co");
            var world = services.GetRequiredService<IWorldGenerator>()
                .Generate(new GenerationContext { Scenario = scenario }, new CatalogSet())
                .World;
            var rawResponse = new ExternalPluginExecutionResponse
            {
                Executed = true,
                Records = new()
                {
                    new PluginGeneratedRecord { RecordType = "One" },
                    new PluginGeneratedRecord { RecordType = "Two" }
                },
                Warnings = new() { pluginWarning }
            };
            var exactResponseBytes = Math.Max(
                1024,
                JsonSerializer.SerializeToUtf8Bytes(
                    rawResponse,
                    new JsonSerializerOptions(PluginJsonOptions) { WriteIndented = true }).Length);

            var result = new OutOfProcessAssemblyExternalPluginHostAdapter().Execute(
                manifest,
                world,
                new GenerationContext
                {
                    Scenario = scenario,
                    ExternalPlugins = new ExternalPluginExecutionSettings
                    {
                        MaxGeneratedRecords = 1,
                        MaxWarningCount = 1,
                        MaxOutputPayloadBytes = exactResponseBytes
                    }
                },
                new CatalogSet());

            Assert.True(result.Executed, string.Join(Environment.NewLine, result.Warnings));
            Assert.Single(result.Records);
            Assert.True(result.Warnings.Count <= 1);
            Assert.DoesNotContain(result.Warnings, warning => warning.Contains("Generated records were truncated", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
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

    [Fact]
    public void AssemblyHost_Bundled_Module_Candidates_Include_Root_Level_Executable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var method = typeof(OutOfProcessAssemblyExternalPluginHostAdapter)
            .GetMethod("ResolveBundledHostCandidates", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var candidates = method!
            .Invoke(null, new object[] { @"C:\modules\SyntheticEnterprise.PowerShell\0.7.0" }) as System.Collections.IEnumerable;
        Assert.NotNull(candidates);

        var hostPaths = candidates!
            .Cast<object>()
            .Select(candidate => candidate.GetType().GetProperty("HostPath")!.GetValue(candidate)?.ToString())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToList();

        Assert.Contains(@"C:\modules\SyntheticEnterprise.PowerShell\0.7.0\SyntheticEnterprise.PluginHost.exe", hostPaths, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void PowerShellHost_Exposes_Generation_Time_Provenance_Across_Independent_Discoveries()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            File.WriteAllText(Path.Combine(tempRoot, "provenance.generator.json"), """
                {
                  "capability": "PowerShellProvenance",
                  "displayName": "PowerShell Provenance",
                  "executionMode": "PowerShellScript",
                  "entryPoint": "provenance.plugin.ps1"
                }
                """);
            File.WriteAllText(Path.Combine(tempRoot, "provenance.plugin.ps1"), """
                New-PluginResult -Records @(
                  (New-PluginRecord -RecordType 'Provenance' -AssociatedEntityType 'Company' -AssociatedEntityId $InputWorld.Companies[0].Id -Properties @{ ReceivedProvenance = $PluginManifest.Provenance.DiscoveredAtUtc })
                ) -Warnings @()
                """);

            using var services = new ServiceCollection()
                .AddSyntheticEnterpriseCore()
                .BuildServiceProvider();
            var registry = services.GetRequiredService<IGenerationPluginRegistry>();
            var firstManifest = Assert.Single(registry.GetDiscoveredManifests(new[] { tempRoot }));
            Thread.Sleep(20);
            var secondManifest = Assert.Single(registry.GetDiscoveredManifests(new[] { tempRoot }));
            var generatedAt = new DateTimeOffset(2026, 8, 14, 12, 30, 0, TimeSpan.Zero);
            var scenario = MinimalScenario("PowerShell Provenance Co");
            var context = new GenerationContext { Scenario = scenario, Seed = 17, GeneratedAt = generatedAt };
            var world = services.GetRequiredService<IWorldGenerator>().Generate(context, new CatalogSet()).World;
            var adapter = new RestrictedPowerShellExternalPluginHostAdapter(new FixedIdFactory());

            var firstResult = adapter.Execute(firstManifest, world, context, new CatalogSet());
            var secondResult = adapter.Execute(secondManifest, world, context, new CatalogSet());

            Assert.NotEqual(firstManifest.Provenance.DiscoveredAtUtc, secondManifest.Provenance.DiscoveredAtUtc);
            Assert.True(firstResult.Executed, string.Join(Environment.NewLine, firstResult.Warnings));
            Assert.True(secondResult.Executed, string.Join(Environment.NewLine, secondResult.Warnings));
            Assert.Equal(generatedAt.ToString("O"), Assert.Single(firstResult.Records).Properties["ReceivedProvenance"]);
            Assert.Equal(generatedAt.ToString("O"), Assert.Single(secondResult.Records).Properties["ReceivedProvenance"]);
            Assert.Equal(SerializeExecutionOutcome(firstResult), SerializeExecutionOutcome(secondResult));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void PowerShellHost_Detaches_Nested_Parameter_Defaults_From_Discovery_Manifest()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            File.WriteAllText(Path.Combine(tempRoot, "mutable-default.generator.json"), """
                {
                  "capability": "MutableDefault",
                  "displayName": "Mutable Default",
                  "executionMode": "PowerShellScript",
                  "entryPoint": "mutable-default.plugin.ps1"
                }
                """);
            File.WriteAllText(Path.Combine(tempRoot, "mutable-default.plugin.ps1"), """
                $defaultValue = $PluginManifest.Parameters[0].DefaultValue
                $nestedValues = $defaultValue['Nested']
                $beforeValue = [string]$nestedValues[0]
                $beforeStatus = [string]$nestedValues[1]['Status']
                $nestedValues[0] = 'plugin-mutated'
                $nestedValues[1]['Status'] = 'plugin-mutated'
                $PluginManifest.Provenance.LocalDataHashes['plugin-injected'] = 'plugin-mutated'

                New-PluginResult -Records @(
                  (New-PluginRecord -RecordType 'MutableDefault' -AssociatedEntityType 'Company' -AssociatedEntityId $InputWorld.Companies[0].Id -Properties @{
                    BeforeValue = $beforeValue
                    BeforeStatus = $beforeStatus
                    AfterValue = [string]$nestedValues[0]
                    AfterStatus = [string]$nestedValues[1]['Status']
                  })
                ) -Warnings @()
                """);

            using var services = new ServiceCollection()
                .AddSyntheticEnterpriseCore()
                .BuildServiceProvider();
            var discovered = Assert.Single(
                services.GetRequiredService<IGenerationPluginRegistry>().GetDiscoveredManifests(new[] { tempRoot }));
            var nestedStatus = new Dictionary<string, object?> { ["Status"] = "trusted" };
            var nestedValues = new List<object?> { "original", nestedStatus };
            var mutableDefault = new Dictionary<string, object?> { ["Nested"] = nestedValues };
            var discoveryManifest = new GenerationPluginManifest
            {
                Capability = discovered.Capability,
                DisplayName = discovered.DisplayName,
                Description = discovered.Description,
                PluginKind = discovered.PluginKind,
                ExecutionMode = discovered.ExecutionMode,
                SourcePath = discovered.SourcePath,
                EntryPoint = discovered.EntryPoint,
                LocalDataPaths = discovered.LocalDataPaths,
                Dependencies = discovered.Dependencies,
                Parameters = new()
                {
                    new PluginParameterDescriptor
                    {
                        Name = "Options",
                        TypeName = "System.Collections.Generic.Dictionary",
                        DefaultValue = mutableDefault
                    }
                },
                Security = discovered.Security,
                Provenance = discovered.Provenance,
                Metadata = discovered.Metadata
            };
            var originalDiscoveredAt = discoveryManifest.Provenance.DiscoveredAtUtc;
            var scenario = MinimalScenario("Mutable Default Co");
            var context = new GenerationContext
            {
                Scenario = scenario,
                Seed = 23,
                GeneratedAt = new DateTimeOffset(2026, 8, 14, 13, 0, 0, TimeSpan.Zero)
            };
            var world = services.GetRequiredService<IWorldGenerator>().Generate(context, new CatalogSet()).World;
            var adapter = new RestrictedPowerShellExternalPluginHostAdapter(new FixedIdFactory());

            var firstResult = adapter.Execute(discoveryManifest, world, context, new CatalogSet());
            var secondResult = adapter.Execute(discoveryManifest, world, context, new CatalogSet());

            Assert.True(firstResult.Executed, string.Join(Environment.NewLine, firstResult.Warnings));
            Assert.True(secondResult.Executed, string.Join(Environment.NewLine, secondResult.Warnings));
            Assert.Equal("original", nestedValues[0] as string);
            Assert.Equal("trusted", nestedStatus["Status"] as string);
            Assert.Equal(originalDiscoveredAt, discoveryManifest.Provenance.DiscoveredAtUtc);
            Assert.DoesNotContain("plugin-injected", discoveryManifest.Provenance.LocalDataHashes.Keys);
            foreach (var result in new[] { firstResult, secondResult })
            {
                var record = Assert.Single(result.Records);
                Assert.Equal("original", record.Properties["BeforeValue"]);
                Assert.Equal("trusted", record.Properties["BeforeStatus"]);
                Assert.Equal("plugin-mutated", record.Properties["AfterValue"]);
                Assert.Equal("plugin-mutated", record.Properties["AfterStatus"]);
            }
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void AssemblyHost_Exposes_Generation_Time_Provenance_Across_Independent_Discoveries()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            WriteAssemblyPluginProject(tempRoot, "assemblyprovenance", """
                using SyntheticEnterprise.Contracts.Models;
                using SyntheticEnterprise.Contracts.Plugins;

                public sealed class AssemblyProvenancePlugin : IExternalGenerationAssemblyPlugin
                {
                    public string Capability => "AssemblyProvenance";

                    public ExternalPluginExecutionResponse Execute(ExternalPluginExecutionRequest request)
                        => new()
                        {
                            Executed = true,
                            Records = new()
                            {
                                new PluginGeneratedRecord
                                {
                                    RecordType = "Provenance",
                                    Properties = new() { ["ReceivedProvenance"] = request.Manifest.Provenance.DiscoveredAtUtc }
                                }
                            }
                        };
                }
                """);
            BuildAssemblyPlugin(tempRoot, "assemblyprovenance");
            File.WriteAllText(Path.Combine(tempRoot, "assemblyprovenance.generator.json"), """
                {
                  "capability": "AssemblyProvenance",
                  "displayName": "Assembly Provenance",
                  "executionMode": "DotNetAssembly",
                  "entryPoint": "bin/Debug/net8.0/assemblyprovenance.dll"
                }
                """);

            using var services = new ServiceCollection()
                .AddSyntheticEnterpriseCore()
                .BuildServiceProvider();
            var registry = services.GetRequiredService<IGenerationPluginRegistry>();
            var firstManifest = Assert.Single(registry.GetDiscoveredManifests(new[] { tempRoot }));
            Thread.Sleep(20);
            var secondManifest = Assert.Single(registry.GetDiscoveredManifests(new[] { tempRoot }));
            var generatedAt = new DateTimeOffset(2026, 8, 14, 12, 30, 0, TimeSpan.Zero);
            var scenario = MinimalScenario("Assembly Provenance Co");
            var context = new GenerationContext { Scenario = scenario, Seed = 17, GeneratedAt = generatedAt };
            var world = services.GetRequiredService<IWorldGenerator>().Generate(context, new CatalogSet()).World;
            var adapter = new OutOfProcessAssemblyExternalPluginHostAdapter();

            var firstResult = adapter.Execute(firstManifest, world, context, new CatalogSet());
            var secondResult = adapter.Execute(secondManifest, world, context, new CatalogSet());

            Assert.NotEqual(firstManifest.Provenance.DiscoveredAtUtc, secondManifest.Provenance.DiscoveredAtUtc);
            Assert.True(firstResult.Executed, string.Join(Environment.NewLine, firstResult.Warnings));
            Assert.True(secondResult.Executed, string.Join(Environment.NewLine, secondResult.Warnings));
            Assert.Equal(generatedAt.ToString("O"), Assert.Single(firstResult.Records).Properties["ReceivedProvenance"]);
            Assert.Equal(generatedAt.ToString("O"), Assert.Single(secondResult.Records).Properties["ReceivedProvenance"]);
            Assert.Equal(SerializeExecutionOutcome(firstResult), SerializeExecutionOutcome(secondResult));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"datagen-external-plugin-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string SerializeExecutionOutcome(ExternalPluginExecutionResult result)
        => JsonSerializer.Serialize(
            new
            {
                result.Executed,
                result.Records,
                result.Warnings
            },
            PluginJsonOptions);

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

    [Fact]
    public void AssemblyHost_Rejects_Entry_Point_Replaced_Before_Verified_Staging()
    {
        var tempRoot = CreateTempDirectory();
        var hostTempRoot = Path.Combine(tempRoot, "host-temp");

        try
        {
            WriteAssemblyPluginProject(tempRoot, "swapwindow", """
                using SyntheticEnterprise.Contracts.Plugins;

                public sealed class SwapWindowPlugin : IExternalGenerationAssemblyPlugin
                {
                    public string Capability => "SwapWindow";

                    public ExternalPluginExecutionResponse Execute(ExternalPluginExecutionRequest request)
                        => new() { Executed = true };
                }
                """);
            BuildAssemblyPlugin(tempRoot, "swapwindow");
            File.WriteAllText(Path.Combine(tempRoot, "swapwindow.generator.json"), """
                {
                  "capability": "SwapWindow",
                  "displayName": "Swap Window",
                  "executionMode": "DotNetAssembly",
                  "entryPoint": "bin/Debug/net8.0/swapwindow.dll"
                }
                """);

            using var services = new ServiceCollection()
                .AddSyntheticEnterpriseCore()
                .BuildServiceProvider();
            var manifest = Assert.Single(
                services.GetRequiredService<IGenerationPluginRegistry>().GetDiscoveredManifests(new[] { tempRoot }),
                candidate => candidate.Capability == "SwapWindow");
            var scenario = MinimalScenario("Swap Window Co");
            var world = services.GetRequiredService<IWorldGenerator>()
                .Generate(new GenerationContext { Scenario = scenario }, new CatalogSet())
                .World;
            var manager = new FixedTemporaryDirectoryManager(hostTempRoot);
            var stager = new EntryPointReplacingBeforeStagingStager(manifest.EntryPoint!);
            var result = new OutOfProcessAssemblyExternalPluginHostAdapter(manager, stager).Execute(
                manifest,
                world,
                new GenerationContext
                {
                    Scenario = scenario,
                    ExternalPlugins = new ExternalPluginExecutionSettings()
                },
                new CatalogSet());

            Assert.False(result.Executed);
            Assert.Contains(result.Warnings, warning => warning.Contains("hash no longer matches discovered provenance", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(hostTempRoot))
            {
                Directory.Delete(hostTempRoot, recursive: true);
            }

            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void AssemblyHost_Executes_Verified_Staged_Copy_When_Original_Is_Replaced_Before_Launch()
    {
        var tempRoot = CreateTempDirectory();
        var hostTempRoot = Path.Combine(tempRoot, "host-temp");

        try
        {
            WriteAssemblyPluginProject(tempRoot, "stagedswap", """
                using SyntheticEnterprise.Contracts.Plugins;

                public sealed class StagedSwapPlugin : IExternalGenerationAssemblyPlugin
                {
                    public string Capability => "StagedSwap";

                    public ExternalPluginExecutionResponse Execute(ExternalPluginExecutionRequest request)
                        => new() { Executed = true, Warnings = new() { "verified-staged-copy" } };
                }
                """);
            BuildAssemblyPlugin(tempRoot, "stagedswap");
            File.WriteAllText(Path.Combine(tempRoot, "stagedswap.generator.json"), """
                {
                  "capability": "StagedSwap",
                  "displayName": "Staged Swap",
                  "executionMode": "DotNetAssembly",
                  "entryPoint": "bin/Debug/net8.0/stagedswap.dll"
                }
                """);

            using var services = new ServiceCollection()
                .AddSyntheticEnterpriseCore()
                .BuildServiceProvider();
            var manifest = Assert.Single(
                services.GetRequiredService<IGenerationPluginRegistry>().GetDiscoveredManifests(new[] { tempRoot }),
                candidate => candidate.Capability == "StagedSwap");
            var scenario = MinimalScenario("Staged Swap Co");
            var world = services.GetRequiredService<IWorldGenerator>()
                .Generate(new GenerationContext { Scenario = scenario }, new CatalogSet())
                .World;
            var manager = new FixedTemporaryDirectoryManager(hostTempRoot);
            var stager = new EntryPointReplacingAssemblyStager(manifest.EntryPoint!);

            var result = new OutOfProcessAssemblyExternalPluginHostAdapter(manager, stager).Execute(
                manifest,
                world,
                new GenerationContext { Scenario = scenario },
                new CatalogSet());

            Assert.True(stager.SourceReplacedAfterStaging);
            Assert.True(result.Executed, string.Join(Environment.NewLine, result.Warnings));
            Assert.Contains("verified-staged-copy", result.Warnings);
        }
        finally
        {
            if (Directory.Exists(hostTempRoot))
            {
                Directory.Delete(hostTempRoot, recursive: true);
            }

            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AssemblyHost_Rejects_Companion_Dll_Tamper_And_PostValidation_Swap(bool swapDuringStaging)
    {
        var tempRoot = CreateTempDirectory();
        var hostTempRoot = Path.Combine(tempRoot, "host-temp");

        try
        {
            WriteAssemblyPluginProject(tempRoot, "companiontrust", """
                using SyntheticEnterprise.Contracts.Plugins;

                public sealed class CompanionTrustPlugin : IExternalGenerationAssemblyPlugin
                {
                    public string Capability => "CompanionTrust";

                    public ExternalPluginExecutionResponse Execute(ExternalPluginExecutionRequest request)
                        => new() { Executed = true, Warnings = new() { "plugin-executed" } };
                }
                """);
            BuildAssemblyPlugin(tempRoot, "companiontrust");
            var outputDirectory = Path.Combine(tempRoot, "bin", "Debug", "net8.0");
            var companionPath = Path.Combine(outputDirectory, "companion.dll");
            File.Copy(Path.Combine(outputDirectory, "companiontrust.dll"), companionPath);
            File.WriteAllText(Path.Combine(tempRoot, "companiontrust.generator.json"), """
                {
                  "capability": "CompanionTrust",
                  "displayName": "Companion Trust",
                  "executionMode": "DotNetAssembly",
                  "entryPoint": "bin/Debug/net8.0/companiontrust.dll"
                }
                """);

            using var services = new ServiceCollection()
                .AddSyntheticEnterpriseCore()
                .BuildServiceProvider();
            var manifest = Assert.Single(
                services.GetRequiredService<IGenerationPluginRegistry>().GetDiscoveredManifests(new[] { tempRoot }),
                candidate => candidate.Capability == "CompanionTrust");
            var scenario = MinimalScenario("Companion Trust Co");
            var world = services.GetRequiredService<IWorldGenerator>()
                .Generate(new GenerationContext { Scenario = scenario }, new CatalogSet())
                .World;
            var manager = new FixedTemporaryDirectoryManager(hostTempRoot);
            IExternalPluginAssemblyStager stager;
            if (swapDuringStaging)
            {
                stager = new FileReplacingBeforeStagingStager(companionPath);
            }
            else
            {
                File.WriteAllText(companionPath, "tampered-after-discovery");
                stager = new FileSystemExternalPluginAssemblyStager();
            }

            var result = new OutOfProcessAssemblyExternalPluginHostAdapter(manager, stager).Execute(
                manifest,
                world,
                new GenerationContext { Scenario = scenario },
                new CatalogSet());

            Assert.False(result.Executed);
            Assert.Contains(result.Warnings, warning => warning.Contains("staged package hash", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(result.Warnings, warning => warning.Contains("plugin-executed", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(hostTempRoot))
            {
                Directory.Delete(hostTempRoot, recursive: true);
            }

            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void AssemblyHost_Rejects_Catalog_Swapped_After_Initial_Provenance_Validation()
    {
        var tempRoot = CreateTempDirectory();
        var hostTempRoot = Path.Combine(tempRoot, "host-temp");

        try
        {
            WriteAssemblyPluginProject(tempRoot, "assemblycatalogswap", """
                using SyntheticEnterprise.Contracts.Plugins;

                public sealed class AssemblyCatalogSwapPlugin : IExternalGenerationAssemblyPlugin
                {
                    public string Capability => "AssemblyCatalogSwap";

                    public ExternalPluginExecutionResponse Execute(ExternalPluginExecutionRequest request)
                        => new() { Executed = true, Warnings = new() { "plugin-executed" } };
                }
                """);
            BuildAssemblyPlugin(tempRoot, "assemblycatalogswap");
            var catalogPath = Path.Combine(tempRoot, "swap.txt");
            File.WriteAllText(catalogPath, "approved");
            File.WriteAllText(Path.Combine(tempRoot, "assemblycatalogswap.generator.json"), """
                {
                  "capability": "AssemblyCatalogSwap",
                  "displayName": "Assembly Catalog Swap",
                  "executionMode": "DotNetAssembly",
                  "entryPoint": "bin/Debug/net8.0/assemblycatalogswap.dll",
                  "localDataPaths": [ "swap.txt" ],
                  "security": {
                    "dataOnly": true,
                    "requestedCapabilities": [ "GenerateData", "ReadPluginData" ]
                  }
                }
                """);

            using var services = new ServiceCollection()
                .AddSyntheticEnterpriseCore()
                .BuildServiceProvider();
            var manifest = Assert.Single(
                services.GetRequiredService<IGenerationPluginRegistry>().GetDiscoveredManifests(new[] { tempRoot }),
                candidate => candidate.Capability == "AssemblyCatalogSwap");
            var scenario = MinimalScenario("Assembly Catalog Swap Co");
            var world = services.GetRequiredService<IWorldGenerator>()
                .Generate(new GenerationContext { Scenario = scenario }, new CatalogSet())
                .World;
            var provider = new CatalogReplacingProvider(catalogPath);

            var result = new OutOfProcessAssemblyExternalPluginHostAdapter(
                new FixedTemporaryDirectoryManager(hostTempRoot),
                new FileSystemExternalPluginAssemblyStager(),
                provider).Execute(
                    manifest,
                    world,
                    new GenerationContext { Scenario = scenario },
                    new CatalogSet());

            Assert.True(provider.ReplacedBeforeLoad);
            Assert.False(result.Executed);
            Assert.Contains(result.Warnings, warning => warning.Contains("catalog hash no longer matches", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(result.Warnings, warning => warning.Contains("plugin-executed", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(hostTempRoot))
            {
                Directory.Delete(hostTempRoot, recursive: true);
            }

            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AssemblyHost_CleanupFailure_FailsClosed_After_Deleting_Request_And_Preserves_Primary_Context(bool pluginSucceeds)
    {
        var tempRoot = CreateTempDirectory();
        var hostTempRoot = Path.Combine(tempRoot, "cleanup-host-temp");

        try
        {
            var response = pluginSucceeds
                ? "new ExternalPluginExecutionResponse { Executed = true }"
                : "new ExternalPluginExecutionResponse { Executed = false, Warnings = new() { \"primary-failure\" } }";
            WriteAssemblyPluginProject(tempRoot, "cleanup", $$"""
                using SyntheticEnterprise.Contracts.Plugins;

                public sealed class CleanupPlugin : IExternalGenerationAssemblyPlugin
                {
                    public string Capability => "Cleanup";

                    public ExternalPluginExecutionResponse Execute(ExternalPluginExecutionRequest request)
                        => {{response}};
                }
                """);
            BuildAssemblyPlugin(tempRoot, "cleanup");
            File.WriteAllText(Path.Combine(tempRoot, "cleanup.generator.json"), """
                {
                  "capability": "Cleanup",
                  "displayName": "Cleanup",
                  "executionMode": "DotNetAssembly",
                  "entryPoint": "bin/Debug/net8.0/cleanup.dll"
                }
                """);

            using var services = new ServiceCollection()
                .AddSyntheticEnterpriseCore()
                .BuildServiceProvider();
            var manifest = Assert.Single(
                services.GetRequiredService<IGenerationPluginRegistry>().GetDiscoveredManifests(new[] { tempRoot }),
                candidate => candidate.Capability == "Cleanup");
            var scenario = MinimalScenario("Cleanup Co");
            var world = services.GetRequiredService<IWorldGenerator>()
                .Generate(new GenerationContext { Scenario = scenario }, new CatalogSet())
                .World;
            var manager = new ResponseCleanupFailureManager(hostTempRoot);

            var result = new OutOfProcessAssemblyExternalPluginHostAdapter(manager).Execute(
                manifest,
                world,
                new GenerationContext { Scenario = scenario },
                new CatalogSet());

            Assert.False(result.Executed);
            Assert.True(manager.RequestDeletedBeforeFailure);
            Assert.False(File.Exists(Path.Combine(hostTempRoot, "request.json")));
            Assert.Contains(result.Warnings, warning => warning.Contains("portable cleanup failure", StringComparison.Ordinal));
            if (!pluginSucceeds)
            {
                Assert.Contains(result.Warnings, warning => warning.Contains("primary-failure", StringComparison.Ordinal));
            }
        }
        finally
        {
            if (Directory.Exists(hostTempRoot))
            {
                Directory.Delete(hostTempRoot, recursive: true);
            }

            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static int GetPowerShellInputPayloadBytes(
        GenerationPluginManifest manifest,
        SyntheticEnterpriseWorld world,
        GenerationContext context)
    {
        var request = CreatePluginRequestMetadata(manifest, context);
        var catalogs = ExternalPluginCatalogLoader.LoadPluginCatalogs(manifest, context.ExternalPlugins);
        return JsonSerializer.SerializeToUtf8Bytes(world, PluginJsonOptions).Length
               + JsonSerializer.SerializeToUtf8Bytes(request, PluginJsonOptions).Length
               + JsonSerializer.SerializeToUtf8Bytes(catalogs, PluginJsonOptions).Length;
    }

    private static int GetAssemblyInputPayloadBytes(
        GenerationPluginManifest manifest,
        SyntheticEnterpriseWorld world,
        GenerationContext context)
    {
        var request = new ExternalPluginExecutionRequest
        {
            Manifest = manifest,
            InputWorld = world,
            Request = CreatePluginRequestMetadata(manifest, context),
            PluginCatalogs = ExternalPluginCatalogLoader.LoadPluginCatalogs(manifest, context.ExternalPlugins)
        };
        return JsonSerializer.SerializeToUtf8Bytes(request, PluginJsonOptions).Length;
    }

    private static ExternalPluginRequestMetadata CreatePluginRequestMetadata(GenerationPluginManifest manifest, GenerationContext context)
        => new()
        {
            Capability = manifest.Capability,
            ScenarioName = context.Scenario.Name,
            Seed = context.Seed,
            GeneratedAt = context.GeneratedAt,
            Metadata = new Dictionary<string, string?>(context.Metadata, StringComparer.OrdinalIgnoreCase),
            PluginSettings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        };

    private static GenerationPluginManifest CreateCatalogManifest(
        string rootPath,
        string capability,
        params string[] catalogPaths)
    {
        var manifestPath = Path.Combine(rootPath, $"{capability}.generator.json");
        File.WriteAllText(manifestPath, "{}");
        var hashes = catalogPaths.ToDictionary(
            path => path,
            path => ExternalPluginPathSecurity.ComputeVerifiedPackageFileHash(
                        manifestPath,
                        path,
                        long.MaxValue,
                        out var warning)
                    ?? throw new InvalidOperationException(warning),
            StringComparer.OrdinalIgnoreCase);
        return new GenerationPluginManifest
        {
            Capability = capability,
            SourcePath = manifestPath,
            LocalDataPaths = catalogPaths.ToList(),
            Provenance = new PluginProvenance { LocalDataHashes = hashes }
        };
    }

    private static string CreateCatalogContent(string extension, int utf8Bytes)
    {
        var (prefix, suffix) = extension switch
        {
            ".json" => ("{\"value\":\"", "\"}"),
            ".csv" => ("Value\n", string.Empty),
            _ => (string.Empty, string.Empty)
        };
        var contentLength = utf8Bytes - prefix.Length - suffix.Length;
        Assert.True(contentLength >= 0);
        var content = prefix + new string('X', contentLength) + suffix;
        Assert.Equal(utf8Bytes, System.Text.Encoding.UTF8.GetByteCount(content));
        return content;
    }

    private static ExternalPluginExecutionResult ExecutePowerShellStreamOverflowPlugin(
        string capability,
        string script,
        int maxOutputPayloadBytes,
        int maxGeneratedRecords = 5000,
        int maxWarningCount = 100)
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            File.WriteAllText(Path.Combine(tempRoot, "overflow.generator.json"), $$"""
                {
                  "capability": "{{capability}}",
                  "displayName": "{{capability}}",
                  "executionMode": "PowerShellScript",
                  "entryPoint": "overflow.plugin.ps1",
                  "security": {
                    "dataOnly": true,
                    "requestedCapabilities": [ "GenerateData", "EmitDiagnostics" ]
                  }
                }
                """);
            File.WriteAllText(Path.Combine(tempRoot, "overflow.plugin.ps1"), script);

            using var services = new ServiceCollection()
                .AddSyntheticEnterpriseCore()
                .BuildServiceProvider();
            var manifest = Assert.Single(
                services.GetRequiredService<IGenerationPluginRegistry>().GetDiscoveredManifests(new[] { tempRoot }),
                candidate => candidate.Capability == capability);
            var scenario = MinimalScenario($"{capability} Co");
            var world = services.GetRequiredService<IWorldGenerator>()
                .Generate(new GenerationContext { Scenario = scenario }, new CatalogSet())
                .World;

            return services.GetServices<IExternalPluginHostAdapter>()
                .OfType<RestrictedPowerShellExternalPluginHostAdapter>()
                .Single()
                .Execute(
                    manifest,
                    world,
                    new GenerationContext
                    {
                        Scenario = scenario,
                        ExternalPlugins = new ExternalPluginExecutionSettings
                        {
                            ExecutionTimeoutSeconds = 10,
                            MaxOutputPayloadBytes = maxOutputPayloadBytes,
                            MaxGeneratedRecords = maxGeneratedRecords,
                            MaxWarningCount = maxWarningCount
                        }
                    },
                    new CatalogSet());
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static bool TryCreateDirectorySymbolicLink(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
        {
            return false;
        }
    }

    private class FixedTemporaryDirectoryManager : FileSystemExternalPluginTemporaryDirectoryManager
    {
        private readonly string _rootPath;

        public FixedTemporaryDirectoryManager(string rootPath)
        {
            _rootPath = rootPath;
        }

        public override string CreateDirectory()
        {
            Directory.CreateDirectory(_rootPath);
            return _rootPath;
        }
    }

    private sealed class FixedIdFactory : IIdFactory
    {
        public string Next(string entityType) => $"{entityType}-fixed";
    }

    private sealed class ResponseCleanupFailureManager : FixedTemporaryDirectoryManager
    {
        public ResponseCleanupFailureManager(string rootPath)
            : base(rootPath)
        {
        }

        public bool RequestDeletedBeforeFailure { get; private set; }

        protected override void DeleteFileCore(string path)
        {
            if (string.Equals(Path.GetFileName(path), "request.json", StringComparison.OrdinalIgnoreCase))
            {
                base.DeleteFileCore(path);
                RequestDeletedBeforeFailure = true;
                return;
            }

            if (string.Equals(Path.GetFileName(path), "response.json", StringComparison.OrdinalIgnoreCase))
            {
                Assert.True(RequestDeletedBeforeFailure);
                throw new IOException("portable cleanup failure");
            }

            base.DeleteFileCore(path);
        }
    }

    private sealed class EntryPointReplacingAssemblyStager : IExternalPluginAssemblyStager
    {
        private readonly FileSystemExternalPluginAssemblyStager _inner = new();
        private readonly string _sourceEntryPoint;

        public EntryPointReplacingAssemblyStager(string sourceEntryPoint)
        {
            _sourceEntryPoint = sourceEntryPoint;
        }

        public bool SourceReplacedAfterStaging { get; private set; }

        public StagedExternalPluginAssembly Stage(GenerationPluginManifest manifest, string temporaryRoot)
        {
            var staged = _inner.Stage(manifest, temporaryRoot);
            File.WriteAllText(_sourceEntryPoint, "replaced-after-verified-stage");
            SourceReplacedAfterStaging = true;
            return staged;
        }
    }

    private sealed class EntryPointReplacingBeforeStagingStager : IExternalPluginAssemblyStager
    {
        private readonly FileSystemExternalPluginAssemblyStager _inner = new();
        private readonly string _sourceEntryPoint;

        public EntryPointReplacingBeforeStagingStager(string sourceEntryPoint)
        {
            _sourceEntryPoint = sourceEntryPoint;
        }

        public StagedExternalPluginAssembly Stage(GenerationPluginManifest manifest, string temporaryRoot)
        {
            File.WriteAllText(_sourceEntryPoint, "replaced-before-verified-stage");
            return _inner.Stage(manifest, temporaryRoot);
        }
    }

    private sealed class FileReplacingBeforeStagingStager : IExternalPluginAssemblyStager
    {
        private readonly FileSystemExternalPluginAssemblyStager _inner = new();
        private readonly string _sourcePath;

        public FileReplacingBeforeStagingStager(string sourcePath)
        {
            _sourcePath = sourcePath;
        }

        public StagedExternalPluginAssembly Stage(GenerationPluginManifest manifest, string temporaryRoot)
        {
            File.WriteAllText(_sourcePath, "swapped-after-initial-provenance-check");
            return _inner.Stage(manifest, temporaryRoot);
        }
    }

    private sealed class CatalogReplacingProvider : IExternalPluginCatalogProvider
    {
        private readonly AuthenticatedExternalPluginCatalogProvider _inner = new();
        private readonly string _catalogPath;

        public CatalogReplacingProvider(string catalogPath)
        {
            _catalogPath = catalogPath;
        }

        public bool ReplacedBeforeLoad { get; private set; }

        public CatalogSet Load(GenerationPluginManifest manifest, ExternalPluginExecutionSettings settings)
        {
            File.WriteAllText(_catalogPath, "swapped-after-adapter-validation");
            ReplacedBeforeLoad = true;
            return _inner.Load(manifest, settings);
        }
    }

    private static ScenarioDefinition PopulationScenario(string companyName, int employeeCount)
        => new()
        {
            Name = companyName,
            Infrastructure = new InfrastructureProfile
            {
                IncludeServers = true,
                IncludeWorkstations = true,
                IncludeNetworkAssets = false,
                IncludeTelephony = false,
                IncludeRepresentativeManagementObservations = true,
                RepresentativeManagementObservationCount = 1,
                RepresentativeManagementHistoryObservationCount = 0,
                ManagementObservationPopulationCoveragePercentage = 100
            },
            Companies = new()
            {
                new ScenarioCompanyDefinition
                {
                    Name = companyName,
                    Industry = "Technology",
                    EmployeeCount = employeeCount,
                    OfficeCount = 1,
                    ServerCount = Math.Max(1, employeeCount / 16),
                    Countries = new() { "United States" }
                }
            }
        };

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
