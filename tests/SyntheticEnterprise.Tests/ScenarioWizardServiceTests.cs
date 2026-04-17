using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Plugins;
using SyntheticEnterprise.Contracts.Scenarios;
using SyntheticEnterprise.Core.Scenarios;
using SyntheticEnterprise.PowerShell.Cmdlets;

namespace SyntheticEnterprise.Tests;

public sealed class ScenarioWizardServiceTests
{
    [Fact]
    public void Run_Builds_Scenario_From_Wizard_Selections()
    {
        var prompter = new FakeScenarioWizardPrompter
        {
            SelectedTemplate = ScenarioTemplateKind.GlobalSaaS,
            SelectedOverlays = new[] { ScenarioOverlayKind.IdentityHeavy, ScenarioOverlayKind.MultiRegionExpansion },
            TextResponses =
            {
                ["Scenario name"] = "Wizard Built Scenario",
                ["Scenario description"] = "Built from an interactive helper.",
                ["Industry profile"] = "Software",
                ["Geography profile"] = "Global"
            },
            IntResponses =
            {
                ["Company count"] = 3,
                ["Minimum employee count"] = 900,
                ["Maximum employee count"] = 1900,
                ["Office count"] = 9
            },
            DoubleResponses =
            {
                ["Stale account rate"] = 0.04
            },
            BoolResponses =
            {
                ["Include hybrid directory"] = true,
                ["Include Microsoft 365 style groups"] = true,
                ["Include external workforce"] = true,
                ["Include Entra B2B guests"] = true,
                ["Include servers"] = true,
                ["Include workstations"] = true,
                ["Include network assets"] = true,
                ["Include telephony"] = false,
                ["Include databases"] = true,
                ["Include file shares"] = true,
                ["Include collaboration sites"] = true,
                ["Use this scenario?"] = true
            }
        };

        var service = new ScenarioWizardService(
            new ScenarioTemplateRegistry(),
            new ScenarioValidator(),
            prompter);

        var result = service.Run();

        Assert.True(result.Confirmed);
        Assert.Equal("Wizard Built Scenario", result.Scenario.Name);
        Assert.Equal("Built from an interactive helper.", result.Scenario.Description);
        Assert.Equal(ScenarioTemplateKind.GlobalSaaS, result.Scenario.Template);
        Assert.Equal(ScenarioDeviationProfiles.Realistic, result.Scenario.DeviationProfile);
        Assert.Equal("Software", result.Scenario.IndustryProfile);
        Assert.Equal("Global", result.Scenario.GeographyProfile);
        Assert.Equal(3, result.Scenario.CompanyCount);
        Assert.Equal(9, result.Scenario.OfficeCount);
        Assert.Equal(900, result.Scenario.EmployeeSize!.Minimum);
        Assert.Equal(1900, result.Scenario.EmployeeSize!.Maximum);
        Assert.Contains(ScenarioOverlayKind.IdentityHeavy, result.Scenario.Overlays);
        Assert.Contains(ScenarioOverlayKind.MultiRegionExpansion, result.Scenario.Overlays);
        Assert.NotNull(prompter.LastValidation);
        Assert.Equal(ScenarioWizardCompletionAction.ReturnScenario, result.CompletionAction);
    }

    [Fact]
    public void Run_Respects_Cancelation_At_Confirmation()
    {
        var prompter = new FakeScenarioWizardPrompter
        {
            SelectedTemplate = ScenarioTemplateKind.RegionalManufacturer,
            SelectedOverlays = Array.Empty<ScenarioOverlayKind>(),
            BoolResponses =
            {
                ["Include hybrid directory"] = true,
                ["Include Microsoft 365 style groups"] = true,
                ["Include external workforce"] = true,
                ["Include Entra B2B guests"] = true,
                ["Include servers"] = true,
                ["Include workstations"] = true,
                ["Include network assets"] = true,
                ["Include telephony"] = true,
                ["Include databases"] = true,
                ["Include file shares"] = true,
                ["Include collaboration sites"] = true,
                ["Use this scenario?"] = false
            }
        };

        var service = new ScenarioWizardService(
            new ScenarioTemplateRegistry(),
            new ScenarioValidator(),
            prompter);

        var result = service.Run();

        Assert.False(result.Confirmed);
        Assert.Equal(ScenarioTemplateKind.RegionalManufacturer, result.Scenario.Template);
    }

    [Fact]
    public void Run_Can_Configure_External_Plugins_From_Discovered_Hints()
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

            var prompter = new FakeScenarioWizardPrompter
            {
                SelectedTemplate = ScenarioTemplateKind.RegionalManufacturer,
                SelectedOverlays = Array.Empty<ScenarioOverlayKind>(),
                TextResponses =
                {
                    ["Plugin root paths (comma-separated)"] = tempRoot,
                    ["Enabled plugin capabilities (comma-separated)"] = "TaxIdentifiers",
                    ["TaxIdentifiers.Region"] = "EU"
                },
                BoolResponses =
                {
                    ["Configure external plugins"] = true,
                    ["Include hybrid directory"] = true,
                    ["Include Microsoft 365 style groups"] = true,
                    ["Include external workforce"] = true,
                    ["Include Entra B2B guests"] = true,
                    ["Include servers"] = true,
                    ["Include workstations"] = true,
                    ["Include network assets"] = true,
                    ["Include telephony"] = true,
                    ["Include databases"] = true,
                    ["Include file shares"] = true,
                    ["Include collaboration sites"] = true,
                    ["Use this scenario?"] = true
                }
            };

            var service = new ScenarioWizardService(
                new ScenarioTemplateRegistry(),
                new ScenarioValidator(),
                prompter);

            var result = service.Run();

            Assert.True(result.Confirmed);
            Assert.Equal(tempRoot, Assert.Single(result.Scenario.ExternalPlugins!.PluginRootPaths));
            Assert.Equal("TaxIdentifiers", Assert.Single(result.Scenario.ExternalPlugins.EnabledCapabilities));
            var configuration = Assert.Single(result.Scenario.ExternalPlugins.CapabilityConfigurations);
            Assert.Equal("TaxIdentifiers", configuration.Capability);
            Assert.Equal("EU", configuration.Settings["Region"]);
            Assert.NotNull(prompter.LastValidation);
            Assert.Contains(prompter.LastValidation!.AuthoringHints, hint => hint.Capability == "TaxIdentifiers");
            Assert.NotNull(prompter.LastContributions);
            Assert.Contains(prompter.LastContributions!, contribution => contribution.Capability == "TaxIdentifiers");
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
    public void Run_Can_Edit_Applications_Cmdb_And_Observed_Data()
    {
        var existingScenario = new ScenarioEnvelope
        {
            Name = "Expanded Scenario",
            Description = "Expanded settings.",
            Template = ScenarioTemplateKind.RegionalManufacturer,
            CompanyCount = 1,
            IndustryProfile = "Manufacturing",
            GeographyProfile = "Regional-US",
            EmployeeSize = new SizeBand
            {
                Minimum = 1000,
                Maximum = 2000
            },
            Identity = new IdentityProfile(),
            Applications = new ApplicationProfile
            {
                IncludeApplications = true,
                BaseApplicationCount = 6,
                IncludeLineOfBusinessApplications = true,
                IncludeSaaSApplications = true
            },
            Infrastructure = new InfrastructureProfile(),
            Repositories = new RepositoryProfile(),
            Cmdb = new CmdbProfile
            {
                IncludeConfigurationManagement = false,
                IncludeBusinessServices = true,
                IncludeCloudServices = true,
                IncludeAutoDiscoveryRecords = true,
                IncludeServiceCatalogRecords = true,
                IncludeSpreadsheetImportRecords = true
            },
            ObservedData = new ObservedDataProfile
            {
                IncludeObservedViews = true,
                CoverageRatio = 0.70
            },
            OfficeCount = 4
        };

        var prompter = new FakeScenarioWizardPrompter
        {
            BoolResponses =
            {
                ["Include applications"] = true,
                ["Include line-of-business applications"] = true,
                ["Include SaaS applications"] = false,
                ["Override CMDB deviation profile"] = true,
                ["Include configuration management"] = true,
                ["Include business services"] = true,
                ["Include cloud services"] = false,
                ["Include auto-discovery records"] = true,
                ["Include service catalog records"] = false,
                ["Include spreadsheet import records"] = true,
                ["Include observed views"] = true,
                ["Use this scenario?"] = true
            },
            IntResponses =
            {
                ["Base application count"] = 14
            },
            DoubleResponses =
            {
                ["Observed data coverage ratio"] = 0.88
            },
            SelectedDeviationProfile = ScenarioDeviationProfiles.Clean
        };

        var service = new ScenarioWizardService(
            new ScenarioTemplateRegistry(),
            new ScenarioValidator(),
            prompter);

        var result = service.Run(
            existingScenario,
            options: new ScenarioWizardRunOptions
            {
                StartSection = ScenarioWizardEditSection.Applications,
                SkipSectionReview = true
            });

        Assert.True(result.Confirmed);
        Assert.Equal(14, result.Scenario.Applications!.BaseApplicationCount);
        Assert.False(result.Scenario.Applications.IncludeSaaSApplications);
        Assert.True(result.Scenario.Cmdb!.IncludeConfigurationManagement);
        Assert.False(result.Scenario.Cmdb.IncludeCloudServices);
        Assert.Equal(ScenarioDeviationProfiles.Clean, result.Scenario.Cmdb.DeviationProfile);
        Assert.True(result.Scenario.ObservedData!.IncludeObservedViews);
        Assert.Equal(0.88, result.Scenario.ObservedData.CoverageRatio);
    }

    [Fact]
    public void Run_Can_Select_Save_And_Generate_Action_With_Path()
    {
        var prompter = new FakeScenarioWizardPrompter
        {
            SelectedTemplate = ScenarioTemplateKind.RegionalManufacturer,
            SelectedCompletionAction = ScenarioWizardCompletionAction.SaveScenarioAndGenerateWorld,
            PromptedPath = @"C:\temp\scenario.json",
            SelectedOverlays = Array.Empty<ScenarioOverlayKind>(),
            BoolResponses =
            {
                ["Configure external plugins"] = false,
                ["Include hybrid directory"] = true,
                ["Include Microsoft 365 style groups"] = true,
                ["Include external workforce"] = true,
                ["Include Entra B2B guests"] = true,
                ["Include servers"] = true,
                ["Include workstations"] = true,
                ["Include network assets"] = true,
                ["Include telephony"] = true,
                ["Include databases"] = true,
                ["Include file shares"] = true,
                ["Include collaboration sites"] = true,
                ["Use this scenario?"] = true
            }
        };

        var service = new ScenarioWizardService(
            new ScenarioTemplateRegistry(),
            new ScenarioValidator(),
            prompter);

        var result = service.Run();

        Assert.True(result.Confirmed);
        Assert.Equal(ScenarioWizardCompletionAction.SaveScenarioAndGenerateWorld, result.CompletionAction);
        Assert.Equal(@"C:\temp\scenario.json", result.OutputPath);
    }

    [Fact]
    public void Run_Can_Edit_Existing_Scenario_Envelope()
    {
        var existingScenario = new ScenarioEnvelope
        {
            Name = "Existing Scenario",
            Description = "Loaded from disk.",
            Template = ScenarioTemplateKind.HealthcareNetwork,
            Overlays = new() { ScenarioOverlayKind.RemoteWorkforce },
            CompanyCount = 2,
            IndustryProfile = "Healthcare",
            GeographyProfile = "North-America",
            EmployeeSize = new SizeBand
            {
                Minimum = 1200,
                Maximum = 2200
            },
            Identity = new IdentityProfile
            {
                IncludeHybridDirectory = true,
                IncludeM365StyleGroups = true,
                IncludeAdministrativeTiers = true,
                IncludeExternalWorkforce = true,
                IncludeB2BGuests = false,
                ContractorRatio = 0.06,
                ManagedServiceProviderRatio = 0.01,
                GuestUserRatio = 0.02,
                StaleAccountRate = 0.07
            },
            Infrastructure = new InfrastructureProfile
            {
                IncludeServers = true,
                IncludeWorkstations = true,
                IncludeNetworkAssets = true,
                IncludeTelephony = false
            },
            Repositories = new RepositoryProfile
            {
                IncludeDatabases = true,
                IncludeFileShares = false,
                IncludeCollaborationSites = true
            },
            OfficeCount = 6
        };

        var prompter = new FakeScenarioWizardPrompter
        {
            SelectedOverlays = new[] { ScenarioOverlayKind.RemoteWorkforce, ScenarioOverlayKind.MultiRegionExpansion },
            TextResponses =
            {
                ["Scenario name"] = "Edited Scenario",
                ["Scenario description"] = "Edited interactively.",
                ["Industry profile"] = "Healthcare",
                ["Geography profile"] = "Global"
            },
            IntResponses =
            {
                ["Company count"] = 4,
                ["Minimum employee count"] = 1500,
                ["Maximum employee count"] = 2600,
                ["Office count"] = 10
            },
            DoubleResponses =
            {
                ["Stale account rate"] = 0.05
            },
            BoolResponses =
            {
                ["Configure external plugins"] = false,
                ["Include hybrid directory"] = true,
                ["Include Microsoft 365 style groups"] = true,
                ["Include external workforce"] = true,
                ["Include Entra B2B guests"] = false,
                ["Include servers"] = true,
                ["Include workstations"] = true,
                ["Include network assets"] = true,
                ["Include telephony"] = false,
                ["Include databases"] = true,
                ["Include file shares"] = false,
                ["Include collaboration sites"] = true,
                ["Use this scenario?"] = true
            }
        };

        var service = new ScenarioWizardService(
            new ScenarioTemplateRegistry(),
            new ScenarioValidator(),
            prompter);

        var result = service.Run(existingScenario);

        Assert.True(result.Confirmed);
        Assert.Equal(0, prompter.SelectTemplateCalls);
        Assert.Contains("Editing existing scenario", prompter.LastEditingLabel);
        Assert.Equal("Edited Scenario", result.Scenario.Name);
        Assert.Equal("Edited interactively.", result.Scenario.Description);
        Assert.Equal(ScenarioTemplateKind.HealthcareNetwork, result.Scenario.Template);
        Assert.Equal(4, result.Scenario.CompanyCount);
        Assert.Equal("Global", result.Scenario.GeographyProfile);
        Assert.Equal(1500, result.Scenario.EmployeeSize!.Minimum);
        Assert.Equal(2600, result.Scenario.EmployeeSize!.Maximum);
        Assert.Contains(ScenarioOverlayKind.RemoteWorkforce, result.Scenario.Overlays);
        Assert.Contains(ScenarioOverlayKind.MultiRegionExpansion, result.Scenario.Overlays);
    }

    [Fact]
    public void Run_Uses_Initial_Output_Path_When_Save_Action_Selected()
    {
        var prompter = new FakeScenarioWizardPrompter
        {
            SelectedTemplate = ScenarioTemplateKind.RegionalManufacturer,
            SelectedCompletionAction = ScenarioWizardCompletionAction.SaveScenario,
            PromptedPath = @"C:\existing\scenario.json",
            SelectedOverlays = Array.Empty<ScenarioOverlayKind>(),
            BoolResponses =
            {
                ["Configure external plugins"] = false,
                ["Include hybrid directory"] = true,
                ["Include Microsoft 365 style groups"] = true,
                ["Include external workforce"] = true,
                ["Include Entra B2B guests"] = true,
                ["Include servers"] = true,
                ["Include workstations"] = true,
                ["Include network assets"] = true,
                ["Include telephony"] = true,
                ["Include databases"] = true,
                ["Include file shares"] = true,
                ["Include collaboration sites"] = true,
                ["Use this scenario?"] = true
            }
        };

        var service = new ScenarioWizardService(
            new ScenarioTemplateRegistry(),
            new ScenarioValidator(),
            prompter);

        var result = service.Run(initialOutputPath: @"C:\existing\scenario.json");

        Assert.True(result.Confirmed);
        Assert.Equal(ScenarioWizardCompletionAction.SaveScenario, result.CompletionAction);
        Assert.Equal(@"C:\existing\scenario.json", result.OutputPath);
    }

    [Fact]
    public void Run_Can_Revisit_Identity_Section_Before_Finishing()
    {
        var prompter = new FakeScenarioWizardPrompter
        {
            SelectedTemplate = ScenarioTemplateKind.RegionalManufacturer,
            SelectedOverlays = Array.Empty<ScenarioOverlayKind>(),
            EditSectionSelections = new Queue<ScenarioWizardEditSection>(new[]
            {
                ScenarioWizardEditSection.Identity,
                ScenarioWizardEditSection.Finish
            }),
            BoolResponses =
            {
                ["Configure external plugins"] = false,
                ["Include hybrid directory"] = true,
                ["Include Microsoft 365 style groups"] = true,
                ["Include external workforce"] = true,
                ["Include Entra B2B guests"] = true,
                ["Include servers"] = true,
                ["Include workstations"] = true,
                ["Include network assets"] = true,
                ["Include telephony"] = true,
                ["Include databases"] = true,
                ["Include file shares"] = true,
                ["Include collaboration sites"] = true,
                ["Use this scenario?"] = true
            }
        };

        var staleAccountResponses = new Queue<double>(new[] { 0.03, 0.08 });
        prompter.OnPromptDouble = prompt =>
            string.Equals(prompt, "Stale account rate", StringComparison.OrdinalIgnoreCase) && staleAccountResponses.Count > 0
                ? staleAccountResponses.Dequeue()
                : null;

        var service = new ScenarioWizardService(
            new ScenarioTemplateRegistry(),
            new ScenarioValidator(),
            prompter);

        var result = service.Run();

        Assert.True(result.Confirmed);
        Assert.Equal(0.08, result.Scenario.Identity!.StaleAccountRate);
        Assert.NotEmpty(prompter.LastChangeSummary);
    }

    [Fact]
    public void Run_Can_Start_From_Identity_Section_For_Targeted_Edit()
    {
        var existingScenario = new ScenarioEnvelope
        {
            Name = "Existing Scenario",
            Description = "Existing description.",
            Template = ScenarioTemplateKind.RegionalManufacturer,
            CompanyCount = 2,
            IndustryProfile = "Manufacturing",
            GeographyProfile = "Regional-US",
            EmployeeSize = new SizeBand
            {
                Minimum = 1000,
                Maximum = 2000
            },
            Identity = new IdentityProfile
            {
                IncludeHybridDirectory = true,
                IncludeM365StyleGroups = true,
                IncludeAdministrativeTiers = true,
                IncludeExternalWorkforce = true,
                IncludeB2BGuests = true,
                ContractorRatio = 0.06,
                ManagedServiceProviderRatio = 0.01,
                GuestUserRatio = 0.02,
                StaleAccountRate = 0.03
            },
            Infrastructure = new InfrastructureProfile(),
            Repositories = new RepositoryProfile(),
            OfficeCount = 4
        };

        var prompter = new FakeScenarioWizardPrompter
        {
            EditSectionSelections = new Queue<ScenarioWizardEditSection>(new[]
            {
                ScenarioWizardEditSection.Finish
            }),
            BoolResponses =
            {
                ["Configure external plugins"] = false,
                ["Include hybrid directory"] = true,
                ["Include Microsoft 365 style groups"] = true,
                ["Include external workforce"] = true,
                ["Include Entra B2B guests"] = true,
                ["Include servers"] = true,
                ["Include workstations"] = true,
                ["Include network assets"] = true,
                ["Include telephony"] = true,
                ["Include databases"] = true,
                ["Include file shares"] = true,
                ["Include collaboration sites"] = true,
                ["Use this scenario?"] = true
            },
            DoubleResponses =
            {
                ["Stale account rate"] = 0.09
            }
        };

        var service = new ScenarioWizardService(
            new ScenarioTemplateRegistry(),
            new ScenarioValidator(),
            prompter);

        var result = service.Run(
            existingScenario,
            options: new ScenarioWizardRunOptions
            {
                StartSection = ScenarioWizardEditSection.Identity
            });

        Assert.True(result.Confirmed);
        Assert.Equal("Existing Scenario", result.Scenario.Name);
        Assert.Equal("Existing description.", result.Scenario.Description);
        Assert.Equal(2, result.Scenario.CompanyCount);
        Assert.Equal(0.09, result.Scenario.Identity!.StaleAccountRate);
    }

    [Fact]
    public void Run_Can_Start_From_Realism_Section_For_Targeted_Edit()
    {
        var existingScenario = new ScenarioEnvelope
        {
            Name = "Existing Scenario",
            Description = "Existing description.",
            Template = ScenarioTemplateKind.RegionalManufacturer,
            DeviationProfile = ScenarioDeviationProfiles.Realistic,
            CompanyCount = 2,
            IndustryProfile = "Manufacturing",
            GeographyProfile = "Regional-US",
            EmployeeSize = new SizeBand
            {
                Minimum = 1000,
                Maximum = 2000
            },
            Identity = new IdentityProfile(),
            Infrastructure = new InfrastructureProfile(),
            Repositories = new RepositoryProfile(),
            OfficeCount = 4
        };

        var prompter = new FakeScenarioWizardPrompter
        {
            SelectedDeviationProfile = ScenarioDeviationProfiles.Clean,
            EditSectionSelections = new Queue<ScenarioWizardEditSection>(new[]
            {
                ScenarioWizardEditSection.Finish
            }),
            BoolResponses =
            {
                ["Use this scenario?"] = true
            }
        };

        var service = new ScenarioWizardService(
            new ScenarioTemplateRegistry(),
            new ScenarioValidator(),
            prompter);

        var result = service.Run(
            existingScenario,
            options: new ScenarioWizardRunOptions
            {
                StartSection = ScenarioWizardEditSection.Realism
            });

        Assert.True(result.Confirmed);
        Assert.Equal(ScenarioDeviationProfiles.Clean, result.Scenario.DeviationProfile);
    }

    [Fact]
    public void Run_Can_Skip_Section_Review_For_Targeted_Edit()
    {
        var existingScenario = new ScenarioEnvelope
        {
            Name = "Existing Scenario",
            Description = "Existing description.",
            Template = ScenarioTemplateKind.RegionalManufacturer,
            CompanyCount = 1,
            IndustryProfile = "Manufacturing",
            GeographyProfile = "Regional-US",
            EmployeeSize = new SizeBand
            {
                Minimum = 1000,
                Maximum = 2000
            },
            Identity = new IdentityProfile
            {
                IncludeHybridDirectory = true,
                IncludeM365StyleGroups = true,
                IncludeAdministrativeTiers = true,
                IncludeExternalWorkforce = true,
                IncludeB2BGuests = true,
                ContractorRatio = 0.06,
                ManagedServiceProviderRatio = 0.01,
                GuestUserRatio = 0.02,
                StaleAccountRate = 0.03
            },
            Infrastructure = new InfrastructureProfile(),
            Repositories = new RepositoryProfile(),
            OfficeCount = 4
        };

        var prompter = new FakeScenarioWizardPrompter
        {
            BoolResponses =
            {
                ["Configure external plugins"] = false,
                ["Include hybrid directory"] = true,
                ["Include Microsoft 365 style groups"] = true,
                ["Include external workforce"] = true,
                ["Include Entra B2B guests"] = true,
                ["Include servers"] = true,
                ["Include workstations"] = true,
                ["Include network assets"] = true,
                ["Include telephony"] = true,
                ["Include databases"] = true,
                ["Include file shares"] = true,
                ["Include collaboration sites"] = true,
                ["Use this scenario?"] = true
            },
            DoubleResponses =
            {
                ["Stale account rate"] = 0.11
            }
        };

        var service = new ScenarioWizardService(
            new ScenarioTemplateRegistry(),
            new ScenarioValidator(),
            prompter);

        var result = service.Run(
            existingScenario,
            options: new ScenarioWizardRunOptions
            {
                StartSection = ScenarioWizardEditSection.Identity,
                SkipSectionReview = true
            });

        Assert.True(result.Confirmed);
        Assert.Equal(0.11, result.Scenario.Identity!.StaleAccountRate);
        Assert.Equal(0, prompter.SelectEditSectionCalls);
    }

    [Fact]
    public void Run_Can_Review_Existing_Scenario_Without_Prompting_Edit_Sections()
    {
        var existingScenario = new ScenarioEnvelope
        {
            Name = "Review Scenario",
            Description = "Review only.",
            Template = ScenarioTemplateKind.GlobalSaaS,
            CompanyCount = 2,
            IndustryProfile = "Software",
            GeographyProfile = "Global",
            EmployeeSize = new SizeBand
            {
                Minimum = 800,
                Maximum = 1800
            },
            Identity = new IdentityProfile(),
            Infrastructure = new InfrastructureProfile(),
            Repositories = new RepositoryProfile(),
            OfficeCount = 6
        };

        var prompter = new FakeScenarioWizardPrompter
        {
            BoolResponses =
            {
                ["Use this scenario?"] = true
            }
        };

        var service = new ScenarioWizardService(
            new ScenarioTemplateRegistry(),
            new ScenarioValidator(),
            prompter);

        var result = service.Run(
            existingScenario,
            options: new ScenarioWizardRunOptions
            {
                ReviewOnly = true
            });

        Assert.True(result.Confirmed);
        Assert.Equal("Review Scenario", result.Scenario.Name);
        Assert.Equal(0, prompter.SelectEditSectionCalls);
        Assert.NotEmpty(prompter.LastChangeSummary);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"datagen-scenario-wizard-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeScenarioWizardPrompter : IScenarioWizardPrompter
    {
        public ScenarioTemplateKind SelectedTemplate { get; init; }
        public IReadOnlyCollection<ScenarioOverlayKind> SelectedOverlays { get; init; } = Array.Empty<ScenarioOverlayKind>();
        public Dictionary<string, string> TextResponses { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> IntResponses { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, double> DoubleResponses { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, bool> BoolResponses { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string SelectedDeviationProfile { get; set; } = ScenarioDeviationProfiles.Realistic;
        public ScenarioValidationResult? LastValidation { get; private set; }
        public IReadOnlyCollection<GenerationPluginCapabilityContribution>? LastContributions { get; private set; }
        public IReadOnlyCollection<ScenarioPluginAuthoringHint>? LastHints { get; private set; }
        public IReadOnlyCollection<string> LastChangeSummary { get; private set; } = Array.Empty<string>();
        public ScenarioWizardCompletionAction SelectedCompletionAction { get; init; } = ScenarioWizardCompletionAction.ReturnScenario;
        public string PromptedPath { get; init; } = "scenario.json";
        public int SelectTemplateCalls { get; private set; }
        public int SelectEditSectionCalls { get; private set; }
        public string LastEditingLabel { get; private set; } = string.Empty;
        public Queue<ScenarioWizardEditSection> EditSectionSelections { get; init; } = new();
        public Func<string, string?>? OnPromptText { get; set; }
        public Func<string, int?>? OnPromptInt { get; set; }
        public Func<string, double?>? OnPromptDouble { get; set; }
        public Func<string, bool?>? OnPromptBool { get; set; }

        public void ShowEditingExistingScenario(string label)
            => LastEditingLabel = label;

        public ScenarioTemplateDescriptor SelectTemplate(IReadOnlyList<ScenarioTemplateDescriptor> templates)
        {
            SelectTemplateCalls++;
            return Assert.Single(templates, template => template.Kind == SelectedTemplate);
        }

        public IReadOnlyCollection<ScenarioOverlayKind> SelectOverlays(IReadOnlyList<ScenarioOverlayKind> overlays, IReadOnlyCollection<ScenarioOverlayKind> recommendedOverlays)
            => SelectedOverlays;

        public string PromptText(string prompt, string defaultValue)
            => OnPromptText?.Invoke(prompt)
                ?? (TextResponses.TryGetValue(prompt, out var value) ? value : defaultValue);

        public int PromptInt(string prompt, int defaultValue, int minimumValue)
            => OnPromptInt?.Invoke(prompt)
                ?? (IntResponses.TryGetValue(prompt, out var value) ? value : defaultValue);

        public double PromptDouble(string prompt, double defaultValue, double minimumValue, double maximumValue)
            => OnPromptDouble?.Invoke(prompt)
                ?? (DoubleResponses.TryGetValue(prompt, out var value) ? value : defaultValue);

        public bool PromptBool(string prompt, bool defaultValue)
            => OnPromptBool?.Invoke(prompt)
                ?? (BoolResponses.TryGetValue(prompt, out var value) ? value : defaultValue);

        public string SelectDeviationProfile(string currentValue)
            => SelectedDeviationProfile;

        public void ShowPluginGuidance(IReadOnlyCollection<GenerationPluginCapabilityContribution> contributions, IReadOnlyCollection<ScenarioPluginAuthoringHint> authoringHints)
        {
            LastContributions = contributions;
            LastHints = authoringHints;
        }

        public void ShowSummary(ScenarioEnvelope scenario, ScenarioValidationResult validation)
            => LastValidation = validation;

        public void ShowChangeSummary(ScenarioEnvelope baseline, ScenarioEnvelope scenario)
            => LastChangeSummary = new[] { baseline.Name, scenario.Name };

        public ScenarioWizardEditSection SelectEditSection(ScenarioEnvelope scenario, bool includePlugins)
        {
            SelectEditSectionCalls++;
            return EditSectionSelections.Count > 0 ? EditSectionSelections.Dequeue() : ScenarioWizardEditSection.Finish;
        }

        public ScenarioWizardCompletionAction SelectCompletionAction(bool canSaveScenario)
            => SelectedCompletionAction;

        public string PromptPath(string prompt, string? defaultValue)
            => PromptedPath;

        public bool Confirm(string prompt, bool defaultValue)
            => BoolResponses.TryGetValue(prompt, out var value) ? value : defaultValue;
    }
}
