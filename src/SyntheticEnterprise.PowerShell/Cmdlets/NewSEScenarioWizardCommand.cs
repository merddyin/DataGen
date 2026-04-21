namespace SyntheticEnterprise.PowerShell.Cmdlets;

using System.IO;
using System.Management.Automation;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Plugins;
using SyntheticEnterprise.Contracts.Scenarios;
using SyntheticEnterprise.Core.DependencyInjection;
using SyntheticEnterprise.Core.Abstractions;
using SyntheticEnterprise.Core.Plugins;
using SyntheticEnterprise.Core.Scenarios;

[Cmdlet(VerbsCommon.New, "SEScenarioWizard")]
[OutputType(typeof(ScenarioEnvelope))]
public sealed class NewSEScenarioWizardCommand : PSCmdlet
{
    [Parameter(Mandatory = true, ParameterSetName = "Path")]
    public string? ScenarioPath { get; set; }

    [Parameter(Mandatory = true, ParameterSetName = "Object", ValueFromPipeline = true)]
    public object? Scenario { get; set; }

    [Parameter(Mandatory = false)]
    public string? CatalogRootPath { get; set; }

    [Parameter(Mandatory = false)]
    public int? Seed { get; set; }

    [Parameter(Mandatory = false)]
    public string? OutputPath { get; set; }

    [Parameter(Mandatory = false)]
    public ScenarioWizardEditSection? StartSection { get; set; }

    [Parameter(Mandatory = false)]
    public SwitchParameter SkipSectionReview { get; set; }

    [Parameter(Mandatory = false)]
    public SwitchParameter ReviewOnly { get; set; }

    protected override void ProcessRecord()
    {
        using var services = BuildServices();
        var wizard = new ScenarioWizardService(
            services.GetRequiredService<IScenarioTemplateRegistry>(),
            services.GetRequiredService<IScenarioArchetypeRegistry>(),
            services.GetRequiredService<IScenarioValidator>(),
            new SpectreScenarioWizardPrompter());

        ScenarioEnvelope? initialScenario = ParameterSetName switch
        {
            "Path" => ScenarioWizardEnvelopeInput.LoadFromPath(ScenarioPath!),
            "Object" => ScenarioWizardEnvelopeInput.FromObject(Scenario is PSObject psObject ? psObject.BaseObject : Scenario!),
            _ => null
        };
        var existingScenarioLabel = ParameterSetName switch
        {
            "Path" => $"Editing existing scenario from {Path.GetFullPath(ScenarioPath!)}",
            "Object" => "Editing existing scenario from pipeline/object input",
            _ => null
        };

        var initialOutputPath = OutputPath ?? (ParameterSetName == "Path" ? ScenarioPath : null);

        var result = wizard.Run(
            initialScenario,
            initialOutputPath,
            existingScenarioLabel,
            new ScenarioWizardRunOptions
            {
                StartSection = StartSection,
                SkipSectionReview = SkipSectionReview.IsPresent,
                ReviewOnly = ReviewOnly.IsPresent
            });
        if (!result.Confirmed)
        {
            WriteVerbose("Scenario wizard canceled.");
            return;
        }

        if (result.CompletionAction is ScenarioWizardCompletionAction.SaveScenario or ScenarioWizardCompletionAction.SaveScenarioAndGenerateWorld)
        {
            var path = result.OutputPath ?? initialOutputPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                ThrowTerminatingError(new ErrorRecord(
                    new InvalidOperationException("Wizard selected a save action but no output path was provided."),
                    "ScenarioWizardMissingOutputPath",
                    ErrorCategory.InvalidArgument,
                    targetObject: null));
            }

            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(fullPath, JsonSerializer.Serialize(result.Scenario, ScenarioWizardJson.Options));
        }

        if (result.CompletionAction is ScenarioWizardCompletionAction.GenerateWorld or ScenarioWizardCompletionAction.SaveScenarioAndGenerateWorld)
        {
            var catalogLoader = services.GetRequiredService<ICatalogLoader>();
            var resolver = services.GetRequiredService<IScenarioDefaultsResolver>();
            var hydrator = services.GetRequiredService<IScenarioPluginProfileHydrator>();
            var pluginBindingService = services.GetRequiredService<IExternalPluginScenarioBindingService>();
            var worldGenerator = services.GetRequiredService<IWorldGenerator>();

            var catalogs = string.IsNullOrWhiteSpace(CatalogRootPath)
                ? catalogLoader.LoadDefault()
                : catalogLoader.LoadFromPath(CatalogRootPath);

            var scenario = hydrator.Hydrate(resolver.Resolve(result.Scenario)).Scenario;
            var pluginSettings = pluginBindingService.Bind(scenario.ExternalPlugins);
            var generationResult = worldGenerator.Generate(
                new GenerationContext
                {
                    Scenario = scenario,
                    Seed = Seed,
                    Metadata = new Dictionary<string, string?>
                    {
                        ["CatalogRootPath"] = CatalogRootPath
                    },
                    ExternalPlugins = pluginSettings
                },
                catalogs);

            WriteObject(generationResult);
            return;
        }

        WriteObject(result.Scenario);
    }

    private static ServiceProvider BuildServices()
        => new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();
}

public sealed class ScenarioWizardExecutionResult
{
    public required ScenarioEnvelope Scenario { get; init; }
    public required ScenarioValidationResult Validation { get; init; }
    public bool Confirmed { get; init; }
    public ScenarioWizardCompletionAction CompletionAction { get; init; } = ScenarioWizardCompletionAction.ReturnScenario;
    public string? OutputPath { get; init; }
}

public sealed class ScenarioWizardRunOptions
{
    public ScenarioWizardEditSection? StartSection { get; init; }
    public bool SkipSectionReview { get; init; }
    public bool ReviewOnly { get; init; }
}

public enum ScenarioWizardCompletionAction
{
    ReturnScenario,
    SaveScenario,
    GenerateWorld,
    SaveScenarioAndGenerateWorld
}

public enum ScenarioWizardEditSection
{
    Finish,
    BasicDetails,
    Realism,
    Identity,
    Applications,
    Infrastructure,
    Repositories,
    Cmdb,
    ObservedData,
    Plugins
}

public interface IScenarioWizardPrompter
{
    void ShowEditingExistingScenario(string label);
    ScenarioArchetypeDescriptor SelectArchetype(IReadOnlyList<ScenarioArchetypeDescriptor> archetypes);
    IReadOnlyCollection<ScenarioOverlayKind> SelectOverlays(IReadOnlyList<ScenarioOverlayKind> overlays, IReadOnlyCollection<ScenarioOverlayKind> recommendedOverlays);
    IReadOnlyCollection<ScenarioPackSelection> SelectPacks(
        IReadOnlyList<ScenarioPackSelection> availablePacks,
        IReadOnlyCollection<ScenarioPackSelection> recommendedPacks,
        IReadOnlyCollection<ScenarioPackSelection> currentPacks);
    string PromptText(string prompt, string defaultValue);
    int PromptInt(string prompt, int defaultValue, int minimumValue);
    double PromptDouble(string prompt, double defaultValue, double minimumValue, double maximumValue);
    bool PromptBool(string prompt, bool defaultValue);
    string SelectDeviationProfile(string currentValue);
    void ShowPluginGuidance(IReadOnlyCollection<GenerationPluginCapabilityContribution> contributions, IReadOnlyCollection<ScenarioPluginAuthoringHint> authoringHints);
    void ShowSummary(ScenarioEnvelope scenario, ScenarioValidationResult validation);
    void ShowChangeSummary(ScenarioEnvelope baseline, ScenarioEnvelope scenario);
    ScenarioWizardEditSection SelectEditSection(ScenarioEnvelope scenario, bool includePlugins);
    ScenarioWizardCompletionAction SelectCompletionAction(bool canSaveScenario);
    string PromptPath(string prompt, string? defaultValue);
    bool Confirm(string prompt, bool defaultValue);
}

public sealed class ScenarioWizardValidationState
{
    public required ScenarioEnvelope Scenario { get; init; }
    public required ScenarioValidationResult Validation { get; init; }
}

public sealed class ScenarioWizardService
{
    private readonly IScenarioTemplateRegistry _templateRegistry;
    private readonly IScenarioArchetypeRegistry _archetypeRegistry;
    private readonly IScenarioValidator _validator;
    private readonly IScenarioWizardPrompter _prompter;

    public ScenarioWizardService(
        IScenarioTemplateRegistry templateRegistry,
        IScenarioArchetypeRegistry archetypeRegistry,
        IScenarioValidator validator,
        IScenarioWizardPrompter prompter)
    {
        _templateRegistry = templateRegistry;
        _archetypeRegistry = archetypeRegistry;
        _validator = validator;
        _prompter = prompter;
    }

    public ScenarioWizardExecutionResult Run(
        ScenarioEnvelope? initialScenario = null,
        string? initialOutputPath = null,
        string? existingScenarioLabel = null,
        ScenarioWizardRunOptions? options = null)
    {
        options ??= new ScenarioWizardRunOptions();

        var archetypes = _archetypeRegistry.GetArchetypes()
            .OrderBy(archetype => archetype.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var templates = _templateRegistry.GetTemplates()
            .OrderBy(template => template.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ScenarioArchetypeDescriptor? selectedArchetype = null;
        ScenarioTemplateDescriptor? selectedTemplate = null;
        ScenarioEnvelope template;
        if (initialScenario is null)
        {
            selectedArchetype = _prompter.SelectArchetype(archetypes);
            template = _archetypeRegistry.CreateArchetype(selectedArchetype.Kind);
        }
        else
        {
            _prompter.ShowEditingExistingScenario(existingScenarioLabel
                ?? $"Editing existing scenario '{initialScenario.Name}'.");
            selectedArchetype = initialScenario.Archetype is { } existingArchetypeKind
                ? archetypes.FirstOrDefault(archetypeDescriptor => archetypeDescriptor.Kind == existingArchetypeKind)
                : null;
            selectedTemplate = initialScenario.Template is { } existingTemplateKind
                ? templates.FirstOrDefault(templateDescriptor => templateDescriptor.Kind == existingTemplateKind)
                : null;
            template = MergeWithTemplateDefaults(initialScenario);
        }

        template = EnsureDeviationProfile(template);

        var scenario = options.ReviewOnly
            ? template
            : PromptAllSections(template, selectedArchetype, selectedTemplate, options.StartSection);
        var validationState = ValidateScenario(scenario);
        scenario = validationState.Scenario;
        var validation = validationState.Validation;
        var summaryShown = false;
        while (!options.ReviewOnly && !options.SkipSectionReview)
        {
            _prompter.ShowSummary(scenario, validation);
            summaryShown = true;
            var editSection = _prompter.SelectEditSection(scenario, includePlugins: true);
            if (editSection == ScenarioWizardEditSection.Finish)
            {
                break;
            }

            scenario = editSection switch
            {
                ScenarioWizardEditSection.BasicDetails => PromptBasicSection(scenario, template, selectedArchetype, selectedTemplate),
                ScenarioWizardEditSection.Realism => PromptRealismSection(scenario),
                ScenarioWizardEditSection.Identity => PromptIdentitySection(scenario),
                ScenarioWizardEditSection.Applications => PromptApplicationsSection(scenario),
                ScenarioWizardEditSection.Infrastructure => PromptInfrastructureSection(scenario),
                ScenarioWizardEditSection.Repositories => PromptRepositorySection(scenario),
                ScenarioWizardEditSection.Cmdb => PromptCmdbSection(scenario),
                ScenarioWizardEditSection.ObservedData => PromptObservedDataSection(scenario),
                ScenarioWizardEditSection.Plugins => PromptPluginSection(scenario),
                _ => scenario
            };

            validationState = ValidateScenario(scenario);
            scenario = validationState.Scenario;
            validation = validationState.Validation;
        }

        if (!summaryShown || options.ReviewOnly || options.SkipSectionReview)
        {
            _prompter.ShowSummary(scenario, validation);
        }
        _prompter.ShowChangeSummary(template, scenario);
        var completionAction = _prompter.SelectCompletionAction(canSaveScenario: true);
        string? selectedOutputPath = initialOutputPath;
        if (completionAction is ScenarioWizardCompletionAction.SaveScenario or ScenarioWizardCompletionAction.SaveScenarioAndGenerateWorld)
        {
            selectedOutputPath = _prompter.PromptPath("Scenario output path", initialOutputPath);
        }

        return new ScenarioWizardExecutionResult
        {
            Scenario = scenario,
            Validation = validation,
            CompletionAction = completionAction,
            OutputPath = selectedOutputPath,
            Confirmed = _prompter.Confirm(
                validation.IsValid
                    ? "Use this scenario?"
                    : "Scenario has validation errors or warnings. Use it anyway?",
                validation.IsValid)
        };
    }

    private ScenarioEnvelope PromptAllSections(
        ScenarioEnvelope template,
        ScenarioArchetypeDescriptor? selectedArchetype,
        ScenarioTemplateDescriptor? selectedTemplate,
        ScenarioWizardEditSection? startSection)
    {
        var scenario = template;
        var orderedSections = new[]
        {
            ScenarioWizardEditSection.BasicDetails,
            ScenarioWizardEditSection.Realism,
            ScenarioWizardEditSection.Identity,
            ScenarioWizardEditSection.Applications,
            ScenarioWizardEditSection.Infrastructure,
            ScenarioWizardEditSection.Repositories,
            ScenarioWizardEditSection.Cmdb,
            ScenarioWizardEditSection.ObservedData,
            ScenarioWizardEditSection.Plugins
        };

        var startIndex = startSection is null or ScenarioWizardEditSection.Finish
            ? 0
            : Array.IndexOf(orderedSections, startSection.Value);
        if (startIndex < 0)
        {
            startIndex = 0;
        }

        for (var index = startIndex; index < orderedSections.Length; index++)
        {
            scenario = orderedSections[index] switch
            {
                ScenarioWizardEditSection.BasicDetails => PromptBasicSection(scenario, template, selectedArchetype, selectedTemplate),
                ScenarioWizardEditSection.Realism => PromptRealismSection(scenario),
                ScenarioWizardEditSection.Identity => PromptIdentitySection(scenario),
                ScenarioWizardEditSection.Applications => PromptApplicationsSection(scenario),
                ScenarioWizardEditSection.Infrastructure => PromptInfrastructureSection(scenario),
                ScenarioWizardEditSection.Repositories => PromptRepositorySection(scenario),
                ScenarioWizardEditSection.Cmdb => PromptCmdbSection(scenario),
                ScenarioWizardEditSection.ObservedData => PromptObservedDataSection(scenario),
                ScenarioWizardEditSection.Plugins => PromptPluginSection(scenario),
                _ => scenario
            };
        }

        return scenario;
    }

    private ScenarioEnvelope PromptBasicSection(
        ScenarioEnvelope scenario,
        ScenarioEnvelope template,
        ScenarioArchetypeDescriptor? selectedArchetype,
        ScenarioTemplateDescriptor? selectedTemplate)
    {
        var employeeSize = scenario.EmployeeSize ?? template.EmployeeSize ?? new SizeBand();
        IReadOnlyCollection<ScenarioOverlayKind> recommendedOverlays = selectedArchetype is not null
            ? selectedArchetype.RecommendedOverlays
            : selectedTemplate is not null
                ? selectedTemplate.RecommendedOverlays
                : Array.Empty<ScenarioOverlayKind>();
        IReadOnlyCollection<ScenarioPackSelection> baseRecommendedPacks = selectedArchetype is not null
            ? selectedArchetype.RecommendedPacks
            : selectedTemplate is not null
                ? selectedTemplate.RecommendedPacks
                : Array.Empty<ScenarioPackSelection>();
        var overlays = _prompter.SelectOverlays(
                Enum.GetValues<ScenarioOverlayKind>(),
                recommendedOverlays)
            .ToList();
        var availablePacks = BuildAvailablePacks(selectedArchetype, selectedTemplate, scenario);
        var currentPacks = scenario.Packs?.EnabledPacks ?? new List<ScenarioPackSelection>();
        var recommendedPacks = BuildRecommendedPacks(baseRecommendedPacks, overlays);
        var selectedPacks = _prompter.SelectPacks(availablePacks, recommendedPacks, currentPacks)
            .Select(ClonePackSelection)
            .ToList();
        var promptProfileDetails = selectedArchetype is null
            || _prompter.PromptBool(
                "Customize industry and geography defaults",
                !MatchesArchetypeProfiles(scenario, template));
        var minimumEmployeeCount = _prompter.PromptInt("Minimum employee count", Math.Max(1, employeeSize.Minimum), 1);
        var maximumEmployeeCount = _prompter.PromptInt("Maximum employee count", Math.Max(minimumEmployeeCount, employeeSize.Maximum), minimumEmployeeCount);

        return new ScenarioEnvelope
        {
            Name = _prompter.PromptText("Scenario name", scenario.Name),
            Description = _prompter.PromptText("Scenario description", scenario.Description),
            Archetype = scenario.Archetype ?? template.Archetype,
            Template = scenario.Template,
            Overlays = overlays,
            CompanyCount = _prompter.PromptInt("Company count", scenario.CompanyCount ?? template.CompanyCount ?? 1, 1),
            IndustryProfile = promptProfileDetails
                ? _prompter.PromptText("Industry profile", scenario.IndustryProfile ?? template.IndustryProfile ?? "General")
                : scenario.IndustryProfile ?? template.IndustryProfile ?? "General",
            GeographyProfile = promptProfileDetails
                ? _prompter.PromptText("Geography profile", scenario.GeographyProfile ?? template.GeographyProfile ?? "Regional-US")
                : scenario.GeographyProfile ?? template.GeographyProfile ?? "Regional-US",
            DeviationProfile = scenario.DeviationProfile ?? template.DeviationProfile ?? ScenarioDeviationProfiles.Realistic,
            EmployeeSize = new SizeBand
            {
                Minimum = minimumEmployeeCount,
                Maximum = maximumEmployeeCount
            },
            Identity = scenario.Identity,
            Applications = scenario.Applications,
            Infrastructure = scenario.Infrastructure,
            Repositories = scenario.Repositories,
            Cmdb = scenario.Cmdb,
            ObservedData = scenario.ObservedData,
            Timeline = scenario.Timeline ?? template.Timeline,
            Packs = new ScenarioPackProfile
            {
                DiscoveryMode = scenario.Packs?.DiscoveryMode ?? template.Packs?.DiscoveryMode ?? "Bundled",
                IncludeBundledPacks = scenario.Packs?.IncludeBundledPacks ?? template.Packs?.IncludeBundledPacks ?? true,
                PackRootPaths = scenario.Packs?.PackRootPaths.ToList() ?? template.Packs?.PackRootPaths.ToList() ?? new List<string>(),
                EnabledPacks = selectedPacks
            },
            ExternalPlugins = scenario.ExternalPlugins,
            Anomalies = scenario.Anomalies.ToList(),
            Companies = scenario.Companies.ToList(),
            OfficeCount = _prompter.PromptInt("Office count", scenario.OfficeCount ?? template.OfficeCount ?? 1, 1)
        };
    }

    private ScenarioEnvelope PromptRealismSection(ScenarioEnvelope scenario)
    {
        var currentDeviationProfile = scenario.DeviationProfile ?? ScenarioDeviationProfiles.Realistic;
        return CloneScenario(
            scenario,
            deviationProfile: _prompter.SelectDeviationProfile(currentDeviationProfile));
    }

    private ScenarioEnvelope PromptIdentitySection(ScenarioEnvelope scenario)
    {
        var identity = scenario.Identity ?? new IdentityProfile();
        return CloneScenario(
            scenario,
            identity: new IdentityProfile
            {
                IncludeHybridDirectory = _prompter.PromptBool("Include hybrid directory", identity.IncludeHybridDirectory),
                IncludeM365StyleGroups = _prompter.PromptBool("Include Microsoft 365 style groups", identity.IncludeM365StyleGroups),
                IncludeAdministrativeTiers = identity.IncludeAdministrativeTiers,
                IncludeExternalWorkforce = _prompter.PromptBool("Include external workforce", identity.IncludeExternalWorkforce),
                IncludeB2BGuests = _prompter.PromptBool("Include Entra B2B guests", identity.IncludeB2BGuests),
                ContractorRatio = identity.ContractorRatio,
                ManagedServiceProviderRatio = identity.ManagedServiceProviderRatio,
                GuestUserRatio = identity.GuestUserRatio,
                StaleAccountRate = _prompter.PromptDouble("Stale account rate", identity.StaleAccountRate, 0, 1)
            });
    }

    private ScenarioEnvelope PromptApplicationsSection(ScenarioEnvelope scenario)
    {
        var applications = scenario.Applications ?? new ApplicationProfile();
        return CloneScenario(
            scenario,
            applications: new ApplicationProfile
            {
                IncludeApplications = _prompter.PromptBool("Include applications", applications.IncludeApplications),
                BaseApplicationCount = _prompter.PromptInt("Base application count", applications.BaseApplicationCount, 0),
                IncludeLineOfBusinessApplications = _prompter.PromptBool("Include line-of-business applications", applications.IncludeLineOfBusinessApplications),
                IncludeSaaSApplications = _prompter.PromptBool("Include SaaS applications", applications.IncludeSaaSApplications)
            });
    }

    private ScenarioEnvelope PromptInfrastructureSection(ScenarioEnvelope scenario)
    {
        var infrastructure = scenario.Infrastructure ?? new InfrastructureProfile();
        return CloneScenario(
            scenario,
            infrastructure: new InfrastructureProfile
            {
                IncludeServers = _prompter.PromptBool("Include servers", infrastructure.IncludeServers),
                IncludeWorkstations = _prompter.PromptBool("Include workstations", infrastructure.IncludeWorkstations),
                IncludeNetworkAssets = _prompter.PromptBool("Include network assets", infrastructure.IncludeNetworkAssets),
                IncludeTelephony = _prompter.PromptBool("Include telephony", infrastructure.IncludeTelephony)
            });
    }

    private ScenarioEnvelope PromptRepositorySection(ScenarioEnvelope scenario)
    {
        var repositories = scenario.Repositories ?? new RepositoryProfile();
        return CloneScenario(
            scenario,
            repositories: new RepositoryProfile
            {
                IncludeDatabases = _prompter.PromptBool("Include databases", repositories.IncludeDatabases),
                IncludeFileShares = _prompter.PromptBool("Include file shares", repositories.IncludeFileShares),
                IncludeCollaborationSites = _prompter.PromptBool("Include collaboration sites", repositories.IncludeCollaborationSites)
            });
    }

    private ScenarioEnvelope PromptCmdbSection(ScenarioEnvelope scenario)
    {
        var cmdb = scenario.Cmdb ?? new CmdbProfile();
        var overrideDeviationProfile = _prompter.PromptBool(
            "Override CMDB deviation profile",
            !string.IsNullOrWhiteSpace(cmdb.DeviationProfile));
        var cmdbDeviationProfile = overrideDeviationProfile
            ? _prompter.SelectDeviationProfile(cmdb.DeviationProfile ?? scenario.DeviationProfile ?? ScenarioDeviationProfiles.Realistic)
            : null;

        return CloneScenario(
            scenario,
            cmdb: new CmdbProfile
            {
                IncludeConfigurationManagement = _prompter.PromptBool("Include configuration management", cmdb.IncludeConfigurationManagement),
                IncludeBusinessServices = _prompter.PromptBool("Include business services", cmdb.IncludeBusinessServices),
                IncludeCloudServices = _prompter.PromptBool("Include cloud services", cmdb.IncludeCloudServices),
                IncludeAutoDiscoveryRecords = _prompter.PromptBool("Include auto-discovery records", cmdb.IncludeAutoDiscoveryRecords),
                IncludeServiceCatalogRecords = _prompter.PromptBool("Include service catalog records", cmdb.IncludeServiceCatalogRecords),
                IncludeSpreadsheetImportRecords = _prompter.PromptBool("Include spreadsheet import records", cmdb.IncludeSpreadsheetImportRecords),
                DeviationProfile = cmdbDeviationProfile
            });
    }

    private ScenarioEnvelope PromptObservedDataSection(ScenarioEnvelope scenario)
    {
        var observedData = scenario.ObservedData ?? new ObservedDataProfile();
        return CloneScenario(
            scenario,
            observedData: new ObservedDataProfile
            {
                IncludeObservedViews = _prompter.PromptBool("Include observed views", observedData.IncludeObservedViews),
                CoverageRatio = _prompter.PromptDouble("Observed data coverage ratio", observedData.CoverageRatio, 0, 1)
            });
    }

    private ScenarioEnvelope PromptPluginSection(ScenarioEnvelope scenario)
    {
        var pluginProfile = scenario.ExternalPlugins ?? new ExternalPluginScenarioProfile();
        var configurePlugins = _prompter.PromptBool(
            "Configure external plugins",
            pluginProfile.PluginRootPaths.Count > 0 || pluginProfile.EnabledCapabilities.Count > 0);

        var configuredPluginProfile = configurePlugins
            ? new ExternalPluginScenarioProfile
            {
                PluginRootPaths = ParseCsv(_prompter.PromptText(
                    "Plugin root paths (comma-separated)",
                    string.Join(", ", pluginProfile.PluginRootPaths))),
                EnabledCapabilities = ParseCsv(_prompter.PromptText(
                    "Enabled plugin capabilities (comma-separated)",
                    string.Join(", ", pluginProfile.EnabledCapabilities))),
                CapabilityConfigurations = pluginProfile.CapabilityConfigurations
                    .Select(CloneConfiguration)
                    .ToList(),
                AllowAssemblyPlugins = pluginProfile.AllowAssemblyPlugins,
                ExecutionTimeoutSeconds = pluginProfile.ExecutionTimeoutSeconds,
                MaxGeneratedRecords = pluginProfile.MaxGeneratedRecords,
                MaxWarningCount = pluginProfile.MaxWarningCount,
                MaxDiagnosticEntries = pluginProfile.MaxDiagnosticEntries,
                MaxDiagnosticCharacters = pluginProfile.MaxDiagnosticCharacters,
                MaxInputPayloadBytes = pluginProfile.MaxInputPayloadBytes,
                MaxOutputPayloadBytes = pluginProfile.MaxOutputPayloadBytes,
                RequireContentHashAllowList = pluginProfile.RequireContentHashAllowList,
                RequireAssemblyHashApproval = pluginProfile.RequireAssemblyHashApproval,
                AllowedContentHashes = pluginProfile.AllowedContentHashes.ToList()
            }
            : new ExternalPluginScenarioProfile
            {
                AllowAssemblyPlugins = pluginProfile.AllowAssemblyPlugins,
                ExecutionTimeoutSeconds = pluginProfile.ExecutionTimeoutSeconds,
                MaxGeneratedRecords = pluginProfile.MaxGeneratedRecords,
                MaxWarningCount = pluginProfile.MaxWarningCount,
                MaxDiagnosticEntries = pluginProfile.MaxDiagnosticEntries,
                MaxDiagnosticCharacters = pluginProfile.MaxDiagnosticCharacters,
                MaxInputPayloadBytes = pluginProfile.MaxInputPayloadBytes,
                MaxOutputPayloadBytes = pluginProfile.MaxOutputPayloadBytes,
                RequireContentHashAllowList = pluginProfile.RequireContentHashAllowList,
                RequireAssemblyHashApproval = pluginProfile.RequireAssemblyHashApproval,
                AllowedContentHashes = pluginProfile.AllowedContentHashes.ToList()
            };

        return CloneScenario(scenario, externalPlugins: configuredPluginProfile);
    }

    private ScenarioWizardValidationState ValidateScenario(ScenarioEnvelope scenario)
    {
        var validation = _validator.Validate(scenario);
        if (scenario.ExternalPlugins is not { PluginRootPaths.Count: > 0, EnabledCapabilities.Count: > 0 }
            || validation.AuthoringHints.Count == 0)
        {
            return new ScenarioWizardValidationState
            {
                Scenario = scenario,
                Validation = validation
            };
        }

        _prompter.ShowPluginGuidance(validation.Contributions, validation.AuthoringHints);
        var currentPluginProfile = scenario.ExternalPlugins;
        var updatedScenario = CloneScenario(
            scenario,
            externalPlugins: new ExternalPluginScenarioProfile
            {
                PluginRootPaths = currentPluginProfile.PluginRootPaths.ToList(),
                EnabledCapabilities = currentPluginProfile.EnabledCapabilities.ToList(),
                CapabilityConfigurations = BuildCapabilityConfigurations(
                    currentPluginProfile,
                    validation.AuthoringHints),
                AllowAssemblyPlugins = currentPluginProfile.AllowAssemblyPlugins,
                ExecutionTimeoutSeconds = currentPluginProfile.ExecutionTimeoutSeconds,
                MaxGeneratedRecords = currentPluginProfile.MaxGeneratedRecords,
                MaxWarningCount = currentPluginProfile.MaxWarningCount,
                MaxDiagnosticEntries = currentPluginProfile.MaxDiagnosticEntries,
                MaxDiagnosticCharacters = currentPluginProfile.MaxDiagnosticCharacters,
                MaxInputPayloadBytes = currentPluginProfile.MaxInputPayloadBytes,
                MaxOutputPayloadBytes = currentPluginProfile.MaxOutputPayloadBytes,
                RequireContentHashAllowList = currentPluginProfile.RequireContentHashAllowList,
                RequireAssemblyHashApproval = currentPluginProfile.RequireAssemblyHashApproval,
                AllowedContentHashes = currentPluginProfile.AllowedContentHashes.ToList()
            });

        return new ScenarioWizardValidationState
        {
            Scenario = updatedScenario,
            Validation = _validator.Validate(updatedScenario)
        };
    }

    private ScenarioEnvelope MergeWithTemplateDefaults(ScenarioEnvelope scenario)
    {
        if (scenario.Template is not { } templateKind)
        {
            return scenario;
        }

        var template = _templateRegistry.CreateTemplate(templateKind);
        return new ScenarioEnvelope
        {
            Name = ChooseString(scenario.Name, "Default", template.Name),
            Description = ChooseString(scenario.Description, "Synthetic enterprise scenario", template.Description),
            Archetype = scenario.Archetype ?? template.Archetype,
            Template = scenario.Template ?? template.Template,
            Overlays = scenario.Overlays.Count > 0 ? scenario.Overlays.ToList() : template.Overlays.ToList(),
            CompanyCount = scenario.CompanyCount ?? template.CompanyCount,
            IndustryProfile = ChooseNullableString(scenario.IndustryProfile, template.IndustryProfile),
            GeographyProfile = ChooseNullableString(scenario.GeographyProfile, template.GeographyProfile),
            DeviationProfile = ChooseNullableString(scenario.DeviationProfile, template.DeviationProfile),
            EmployeeSize = scenario.EmployeeSize ?? template.EmployeeSize,
            Identity = scenario.Identity ?? template.Identity,
            Applications = scenario.Applications ?? template.Applications,
            Infrastructure = scenario.Infrastructure ?? template.Infrastructure,
            Repositories = scenario.Repositories ?? template.Repositories,
            Cmdb = scenario.Cmdb ?? template.Cmdb,
            ObservedData = scenario.ObservedData ?? template.ObservedData,
            Timeline = scenario.Timeline ?? template.Timeline,
            Packs = scenario.Packs ?? template.Packs,
            ExternalPlugins = scenario.ExternalPlugins ?? template.ExternalPlugins,
            Anomalies = scenario.Anomalies.Count > 0 ? scenario.Anomalies.ToList() : template.Anomalies.ToList(),
            Companies = scenario.Companies.Count > 0 ? scenario.Companies.ToList() : template.Companies.ToList(),
            OfficeCount = scenario.OfficeCount ?? template.OfficeCount
        };
    }

    private static string ChooseString(string? value, string sentinel, string fallback)
        => string.IsNullOrWhiteSpace(value) || string.Equals(value, sentinel, StringComparison.Ordinal)
            ? fallback
            : value;

    private static string? ChooseNullableString(string? value, string? fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static ScenarioEnvelope CloneScenario(
        ScenarioEnvelope scenario,
        string? deviationProfile = null,
        IdentityProfile? identity = null,
        ApplicationProfile? applications = null,
        InfrastructureProfile? infrastructure = null,
        RepositoryProfile? repositories = null,
        CmdbProfile? cmdb = null,
        ObservedDataProfile? observedData = null,
        ExternalPluginScenarioProfile? externalPlugins = null)
        => new()
        {
            Name = scenario.Name,
            Description = scenario.Description,
            Archetype = scenario.Archetype,
            Template = scenario.Template,
            Overlays = scenario.Overlays.ToList(),
            CompanyCount = scenario.CompanyCount,
            IndustryProfile = scenario.IndustryProfile,
            GeographyProfile = scenario.GeographyProfile,
            DeviationProfile = deviationProfile ?? scenario.DeviationProfile ?? ScenarioDeviationProfiles.Realistic,
            EmployeeSize = scenario.EmployeeSize,
            Identity = identity ?? scenario.Identity,
            Applications = applications ?? scenario.Applications,
            Infrastructure = infrastructure ?? scenario.Infrastructure,
            Repositories = repositories ?? scenario.Repositories,
            Cmdb = cmdb ?? scenario.Cmdb,
            ObservedData = observedData ?? scenario.ObservedData,
            Timeline = scenario.Timeline,
            Packs = scenario.Packs,
            ExternalPlugins = externalPlugins ?? scenario.ExternalPlugins,
            Anomalies = scenario.Anomalies.ToList(),
            Companies = scenario.Companies.ToList(),
            OfficeCount = scenario.OfficeCount
        };

    private static bool MatchesArchetypeProfiles(ScenarioEnvelope scenario, ScenarioEnvelope template)
        => string.Equals(
                scenario.IndustryProfile ?? template.IndustryProfile,
                template.IndustryProfile,
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                scenario.GeographyProfile ?? template.GeographyProfile,
                template.GeographyProfile,
                StringComparison.OrdinalIgnoreCase);

    private static List<ScenarioPackSelection> BuildAvailablePacks(
        ScenarioArchetypeDescriptor? selectedArchetype,
        ScenarioTemplateDescriptor? selectedTemplate,
        ScenarioEnvelope scenario)
    {
        var packs = new Dictionary<string, ScenarioPackSelection>(StringComparer.OrdinalIgnoreCase);

        foreach (var pack in selectedArchetype?.RecommendedPacks ?? Enumerable.Empty<ScenarioPackSelection>())
        {
            packs[pack.PackId] = ClonePackSelection(pack);
        }

        foreach (var pack in selectedTemplate?.RecommendedPacks ?? Enumerable.Empty<ScenarioPackSelection>())
        {
            packs[pack.PackId] = ClonePackSelection(pack);
        }

        foreach (var pack in scenario.Packs?.EnabledPacks ?? Enumerable.Empty<ScenarioPackSelection>())
        {
            packs[pack.PackId] = ClonePackSelection(pack);
        }

        foreach (var pack in GetKnownBundledPacks())
        {
            if (!packs.ContainsKey(pack.PackId))
            {
                packs[pack.PackId] = ClonePackSelection(pack);
            }
        }

        return packs.Values
            .OrderBy(pack => pack.PackId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<ScenarioPackSelection> BuildRecommendedPacks(
        IReadOnlyCollection<ScenarioPackSelection> baselineRecommendations,
        IReadOnlyCollection<ScenarioOverlayKind> overlays)
    {
        var recommended = baselineRecommendations
            .Select(ClonePackSelection)
            .ToDictionary(pack => pack.PackId, StringComparer.OrdinalIgnoreCase);

        foreach (var overlay in overlays)
        {
            switch (overlay)
            {
                case ScenarioOverlayKind.FastGrowth:
                    recommended["FirstParty.ITSM"] = CreatePack("FirstParty.ITSM", ("TicketCount", "20"));
                    break;
                case ScenarioOverlayKind.PostMerger:
                    recommended["FirstParty.BusinessOps"] = CreatePack("FirstParty.BusinessOps", ("RequestCount", "14"));
                    recommended["FirstParty.ITSM"] = CreatePack("FirstParty.ITSM", ("TicketCount", "18"));
                    break;
                case ScenarioOverlayKind.ComplianceHeavy:
                case ScenarioOverlayKind.UnderGoverned:
                case ScenarioOverlayKind.Modernization:
                    if (!recommended.ContainsKey("FirstParty.SecOps"))
                    {
                        recommended["FirstParty.SecOps"] = CreatePack("FirstParty.SecOps", ("AlertCount", "10"));
                    }
                    break;
            }
        }

        return recommended.Values
            .OrderBy(pack => pack.PackId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<ScenarioPackSelection> GetKnownBundledPacks() => new[]
    {
        CreatePack("FirstParty.BusinessOps", ("RequestCount", "6")),
        CreatePack("FirstParty.ITSM", ("TicketCount", "12")),
        CreatePack("FirstParty.SecOps", ("AlertCount", "8"))
    };

    private static ScenarioPackSelection CreatePack(string packId, params (string Key, string Value)[] settings)
    {
        var selection = new ScenarioPackSelection
        {
            PackId = packId
        };

        foreach (var (key, value) in settings)
        {
            selection.Settings[key] = value;
        }

        return selection;
    }

    private static ScenarioPackSelection ClonePackSelection(ScenarioPackSelection selection)
        => new()
        {
            PackId = selection.PackId,
            Enabled = selection.Enabled,
            Settings = new Dictionary<string, string?>(selection.Settings, StringComparer.OrdinalIgnoreCase)
        };

    private static ScenarioEnvelope EnsureDeviationProfile(ScenarioEnvelope scenario)
        => string.IsNullOrWhiteSpace(scenario.DeviationProfile)
            ? CloneScenario(scenario, deviationProfile: ScenarioDeviationProfiles.Realistic)
            : scenario;

    private List<ExternalPluginCapabilityConfiguration> BuildCapabilityConfigurations(
        ExternalPluginScenarioProfile profile,
        IReadOnlyCollection<ScenarioPluginAuthoringHint> hints)
    {
        var existingConfigurations = profile.CapabilityConfigurations
            .ToDictionary(configuration => configuration.Capability, CloneConfiguration, StringComparer.OrdinalIgnoreCase);

        foreach (var hint in hints)
        {
            if (!existingConfigurations.TryGetValue(hint.Capability, out var configuration))
            {
                configuration = new ExternalPluginCapabilityConfiguration
                {
                    Capability = hint.Capability
                };
                existingConfigurations[hint.Capability] = configuration;
            }

            foreach (var parameter in hint.Parameters)
            {
                var defaultValue = hint.SuggestedSettings.TryGetValue(parameter.Name, out var suggestedValue)
                    ? suggestedValue ?? string.Empty
                    : string.Empty;

                var enteredValue = _prompter.PromptText(
                    $"{hint.Capability}.{parameter.Name}",
                    defaultValue);

                if (string.IsNullOrWhiteSpace(enteredValue))
                {
                    configuration.Settings.Remove(parameter.Name);
                    continue;
                }

                configuration.Settings[parameter.Name] = enteredValue;
            }
        }

        return existingConfigurations.Values
            .OrderBy(configuration => configuration.Capability, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ExternalPluginCapabilityConfiguration CloneConfiguration(ExternalPluginCapabilityConfiguration configuration)
        => new()
        {
            Capability = configuration.Capability,
            Settings = new Dictionary<string, string?>(configuration.Settings, StringComparer.OrdinalIgnoreCase)
        };

    private static List<string> ParseCsv(string value)
        => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}

public sealed class SpectreScenarioWizardPrompter : IScenarioWizardPrompter
{
    public void ShowEditingExistingScenario(string label)
    {
        AnsiConsole.Write(new Rule("[yellow]Scenario Wizard[/]"));
        AnsiConsole.Write(new Panel(Escape(label)).Header("[yellow]Edit Existing Scenario[/]"));
    }

    public ScenarioArchetypeDescriptor SelectArchetype(IReadOnlyList<ScenarioArchetypeDescriptor> archetypes)
    {
        AnsiConsole.Write(new Rule("[yellow]Scenario Wizard[/]"));
        return AnsiConsole.Prompt(
            new SelectionPrompt<ScenarioArchetypeDescriptor>()
                .Title("Choose a [green]scenario archetype[/]:")
                .UseConverter(archetype => $"{archetype.Name} - {archetype.Description}")
                .AddChoices(archetypes));
    }

    public IReadOnlyCollection<ScenarioOverlayKind> SelectOverlays(IReadOnlyList<ScenarioOverlayKind> overlays, IReadOnlyCollection<ScenarioOverlayKind> recommendedOverlays)
    {
        var recommended = recommendedOverlays.Count == 0
            ? "none"
            : string.Join(", ", recommendedOverlays);

        return AnsiConsole.Prompt(
            new MultiSelectionPrompt<ScenarioOverlayKind>()
                .Title($"Choose optional [green]overlays[/] ([grey]recommended: {recommended}[/]):")
                .NotRequired()
                .InstructionsText("[grey](Press [blue]<space>[/] to toggle, [green]<enter>[/] to accept)[/]")
                .UseConverter(overlay => overlay.ToString())
                .AddChoices(overlays));
    }

    public IReadOnlyCollection<ScenarioPackSelection> SelectPacks(
        IReadOnlyList<ScenarioPackSelection> availablePacks,
        IReadOnlyCollection<ScenarioPackSelection> recommendedPacks,
        IReadOnlyCollection<ScenarioPackSelection> currentPacks)
    {
        var selectedPackIds = currentPacks.Count > 0
            ? currentPacks.Select(pack => pack.PackId).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : recommendedPacks.Select(pack => pack.PackId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var recommendedNames = recommendedPacks.Count == 0
            ? "none"
            : string.Join(", ", recommendedPacks.Select(pack => pack.PackId));

        var prompt = new MultiSelectionPrompt<ScenarioPackSelection>()
            .Title($"Choose optional [green]domain packs[/] ([grey]recommended: {recommendedNames}[/]):")
            .NotRequired()
            .InstructionsText("[grey](Press [blue]<space>[/] to toggle, [green]<enter>[/] to accept)[/]")
            .UseConverter(pack => $"{pack.PackId} ({FormatPackSettings(pack)})")
            .AddChoices(availablePacks);

        foreach (var pack in availablePacks.Where(pack => selectedPackIds.Contains(pack.PackId)))
        {
            prompt.Select(pack);
        }

        return AnsiConsole.Prompt(prompt)
            .Select(ClonePackSelection)
            .ToList();
    }

    public string PromptText(string prompt, string defaultValue)
        => AnsiConsole.Prompt(
            new TextPrompt<string>($"[green]{Escape(prompt)}[/]")
                .DefaultValue(defaultValue)
                .AllowEmpty());

    public int PromptInt(string prompt, int defaultValue, int minimumValue)
        => AnsiConsole.Prompt(
            new TextPrompt<int>($"[green]{Escape(prompt)}[/]")
                .DefaultValue(Math.Max(defaultValue, minimumValue))
                .Validate(value => value >= minimumValue
                    ? ValidationResult.Success()
                    : ValidationResult.Error($"Value must be at least {minimumValue}.")));

    public double PromptDouble(string prompt, double defaultValue, double minimumValue, double maximumValue)
        => AnsiConsole.Prompt(
            new TextPrompt<double>($"[green]{Escape(prompt)}[/]")
                .DefaultValue(defaultValue)
                .Validate(value => value >= minimumValue && value <= maximumValue
                    ? ValidationResult.Success()
                    : ValidationResult.Error($"Value must be between {minimumValue} and {maximumValue}.")));

    public bool PromptBool(string prompt, bool defaultValue)
        => AnsiConsole.Prompt(
            new ConfirmationPrompt($"[green]{Escape(prompt)}[/]")
            {
                DefaultValue = defaultValue
            });

    public string SelectDeviationProfile(string currentValue)
    {
        var normalizedCurrent = ScenarioDeviationProfiles.All.Contains(currentValue, StringComparer.OrdinalIgnoreCase)
            ? ScenarioDeviationProfiles.All.First(profile => profile.Equals(currentValue, StringComparison.OrdinalIgnoreCase))
            : ScenarioDeviationProfiles.Realistic;
        var orderedChoices = new[] { normalizedCurrent }
            .Concat(ScenarioDeviationProfiles.All.Where(profile => !profile.Equals(normalizedCurrent, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Choose a [green]deviation profile[/]:")
                    .UseConverter(profile => profile switch
                    {
                        "Clean" => "Clean - rich enterprise, minimal realistic flaws",
                        "Realistic" => "Realistic - balanced enterprise messiness",
                        "Aggressive" => "Aggressive - heavier drift and inconsistency",
                        _ => profile
                    })
                    .AddChoices(orderedChoices)
                    .HighlightStyle(new Style(foreground: Color.Green)))
            .Trim();
    }

    public void ShowPluginGuidance(IReadOnlyCollection<GenerationPluginCapabilityContribution> contributions, IReadOnlyCollection<ScenarioPluginAuthoringHint> authoringHints)
    {
        if (contributions.Count == 0 && authoringHints.Count == 0)
        {
            return;
        }

        var table = new Table()
            .Border(TableBorder.Minimal)
            .AddColumn("Capability")
            .AddColumn("Details");

        foreach (var contribution in contributions)
        {
            var hint = authoringHints.FirstOrDefault(item => item.Capability.Equals(contribution.Capability, StringComparison.OrdinalIgnoreCase));
            var parameters = contribution.Parameters.Count == 0
                ? "no parameters"
                : string.Join(", ", contribution.Parameters.Select(parameter => parameter.Name));
            var defaults = hint is null || hint.SuggestedSettings.Count == 0
                ? "no defaults"
                : string.Join(", ", hint.SuggestedSettings.Select(setting => $"{setting.Key}={setting.Value}"));

            table.AddRow(
                Escape(contribution.Capability),
                Escape($"{contribution.DisplayName} | params: {parameters} | defaults: {defaults}"));
        }

        AnsiConsole.Write(new Panel(table).Header("[yellow]Plugin Guidance[/]"));
    }

    public void ShowSummary(ScenarioEnvelope scenario, ScenarioValidationResult validation)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Setting")
            .AddColumn("Value");

        table.AddRow("Name", scenario.Name);
        table.AddRow("Archetype", scenario.Archetype?.ToString() ?? "Custom");
        table.AddRow("Template", scenario.Template?.ToString() ?? "Custom");
        table.AddRow("Industry", scenario.IndustryProfile ?? "General");
        table.AddRow("Geography", scenario.GeographyProfile ?? "Regional-US");
        table.AddRow("Deviation profile", scenario.DeviationProfile ?? ScenarioDeviationProfiles.Realistic);
        table.AddRow("Companies", (scenario.CompanyCount ?? 1).ToString());
        table.AddRow("Employee range", $"{scenario.EmployeeSize?.Minimum ?? 0} - {scenario.EmployeeSize?.Maximum ?? 0}");
        table.AddRow(
            "Applications",
            scenario.Applications?.IncludeApplications == true
                ? $"enabled ({scenario.Applications.BaseApplicationCount} base)"
                : "disabled");
        table.AddRow(
            "CMDB",
            scenario.Cmdb?.IncludeConfigurationManagement == true
                ? $"enabled ({scenario.Cmdb.DeviationProfile ?? scenario.DeviationProfile ?? ScenarioDeviationProfiles.Realistic})"
                : "disabled");
        table.AddRow(
            "Observed data",
            scenario.ObservedData?.IncludeObservedViews == true
                ? $"enabled ({scenario.ObservedData.CoverageRatio:0.00} coverage)"
                : "disabled");
        table.AddRow("Overlays", scenario.Overlays.Count == 0 ? "None" : string.Join(", ", scenario.Overlays));
        table.AddRow(
            "Packs",
            scenario.Packs is { EnabledPacks.Count: > 0 }
                ? string.Join(", ", scenario.Packs.EnabledPacks.Select(pack => pack.PackId))
                : "None");
        table.AddRow(
            "Plugins",
            scenario.ExternalPlugins is { PluginRootPaths.Count: > 0, EnabledCapabilities.Count: > 0 }
                ? string.Join(", ", scenario.ExternalPlugins.EnabledCapabilities)
                : "None");
        table.AddRow("Validation", validation.IsValid ? "[green]Valid[/]" : "[red]Has errors[/]");

        AnsiConsole.Write(new Panel(table).Header("[yellow]Scenario Preview[/]"));

        if (validation.Messages.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]No validation messages.[/]");
            return;
        }

        var messageTable = new Table()
            .Border(TableBorder.Minimal)
            .AddColumn("Severity")
            .AddColumn("Message");

        foreach (var message in validation.Messages)
        {
            var severity = message.Severity switch
            {
                ScenarioValidationSeverity.Error => "[red]Error[/]",
                ScenarioValidationSeverity.Warning => "[yellow]Warning[/]",
                _ => "[grey]Info[/]"
            };

            messageTable.AddRow(severity, Escape(message.Message));
        }

        AnsiConsole.Write(messageTable);
    }

    public void ShowChangeSummary(ScenarioEnvelope baseline, ScenarioEnvelope scenario)
    {
        var changes = BuildChangeLines(baseline, scenario);
        if (changes.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No changes from the starting scenario.[/]");
            return;
        }

        var list = new Rows(changes.Select(change => new Markup($"[green]-[/] {Escape(change)}")).ToArray());
        AnsiConsole.Write(new Panel(list).Header("[yellow]Changes[/]"));
    }

    public ScenarioWizardEditSection SelectEditSection(ScenarioEnvelope scenario, bool includePlugins)
    {
        var choices = includePlugins
            ? new[]
            {
                ScenarioWizardEditSection.Finish,
                ScenarioWizardEditSection.BasicDetails,
                ScenarioWizardEditSection.Realism,
                ScenarioWizardEditSection.Identity,
                ScenarioWizardEditSection.Applications,
                ScenarioWizardEditSection.Infrastructure,
                ScenarioWizardEditSection.Repositories,
                ScenarioWizardEditSection.Cmdb,
                ScenarioWizardEditSection.ObservedData,
                ScenarioWizardEditSection.Plugins
            }
            : new[]
            {
                ScenarioWizardEditSection.Finish,
                ScenarioWizardEditSection.BasicDetails,
                ScenarioWizardEditSection.Realism,
                ScenarioWizardEditSection.Identity,
                ScenarioWizardEditSection.Applications,
                ScenarioWizardEditSection.Infrastructure,
                ScenarioWizardEditSection.Repositories,
                ScenarioWizardEditSection.Cmdb,
                ScenarioWizardEditSection.ObservedData
            };

        return AnsiConsole.Prompt(
            new SelectionPrompt<ScenarioWizardEditSection>()
                .Title("Review a section before continuing:")
                .UseConverter(section => DescribeSection(section, scenario))
                .AddChoices(choices));
    }

    public ScenarioWizardCompletionAction SelectCompletionAction(bool canSaveScenario)
    {
        var prompt = new SelectionPrompt<ScenarioWizardCompletionAction>()
            .Title("Choose what to do next:")
            .UseConverter(action => action switch
            {
                ScenarioWizardCompletionAction.ReturnScenario => "Return scenario only",
                ScenarioWizardCompletionAction.SaveScenario => "Save scenario",
                ScenarioWizardCompletionAction.GenerateWorld => "Generate world now",
                ScenarioWizardCompletionAction.SaveScenarioAndGenerateWorld => "Save scenario and generate world",
                _ => action.ToString()
            })
            .AddChoices(canSaveScenario
                ? new[]
                {
                    ScenarioWizardCompletionAction.ReturnScenario,
                    ScenarioWizardCompletionAction.SaveScenario,
                    ScenarioWizardCompletionAction.GenerateWorld,
                    ScenarioWizardCompletionAction.SaveScenarioAndGenerateWorld
                }
                : new[]
                {
                    ScenarioWizardCompletionAction.ReturnScenario,
                    ScenarioWizardCompletionAction.GenerateWorld
                });

        return AnsiConsole.Prompt(prompt);
    }

    public ScenarioWizardEditSection SelectEditSection(bool includePlugins)
    {
        var choices = includePlugins
            ? new[]
            {
                ScenarioWizardEditSection.Finish,
                ScenarioWizardEditSection.BasicDetails,
                ScenarioWizardEditSection.Realism,
                ScenarioWizardEditSection.Identity,
                ScenarioWizardEditSection.Applications,
                ScenarioWizardEditSection.Infrastructure,
                ScenarioWizardEditSection.Repositories,
                ScenarioWizardEditSection.Cmdb,
                ScenarioWizardEditSection.ObservedData,
                ScenarioWizardEditSection.Plugins
            }
            : new[]
            {
                ScenarioWizardEditSection.Finish,
                ScenarioWizardEditSection.BasicDetails,
                ScenarioWizardEditSection.Realism,
                ScenarioWizardEditSection.Identity,
                ScenarioWizardEditSection.Infrastructure,
                ScenarioWizardEditSection.Applications,
                ScenarioWizardEditSection.Repositories,
                ScenarioWizardEditSection.Cmdb,
                ScenarioWizardEditSection.ObservedData
            };

        return AnsiConsole.Prompt(
            new SelectionPrompt<ScenarioWizardEditSection>()
                .Title("Review a section before continuing:")
                .UseConverter(section => section switch
                {
                    ScenarioWizardEditSection.Finish => "Continue",
                    ScenarioWizardEditSection.BasicDetails => "Edit basic details",
                    ScenarioWizardEditSection.Realism => "Edit realism settings",
                    ScenarioWizardEditSection.Identity => "Edit identity settings",
                    ScenarioWizardEditSection.Applications => "Edit application settings",
                    ScenarioWizardEditSection.Infrastructure => "Edit infrastructure settings",
                    ScenarioWizardEditSection.Repositories => "Edit repository settings",
                    ScenarioWizardEditSection.Cmdb => "Edit CMDB settings",
                    ScenarioWizardEditSection.ObservedData => "Edit observed-data settings",
                    ScenarioWizardEditSection.Plugins => "Edit plugin settings",
                    _ => section.ToString()
                })
                .AddChoices(choices));
    }

    public string PromptPath(string prompt, string? defaultValue)
        => AnsiConsole.Prompt(
            new TextPrompt<string>($"[green]{Escape(prompt)}[/]")
                .DefaultValue(defaultValue ?? "scenario.json")
                .Validate(value => string.IsNullOrWhiteSpace(value)
                    ? ValidationResult.Error("A path is required.")
                    : ValidationResult.Success()));

    public bool Confirm(string prompt, bool defaultValue)
        => AnsiConsole.Prompt(
            new ConfirmationPrompt($"[green]{Escape(prompt)}[/]")
            {
                DefaultValue = defaultValue
            });

    private static string DescribeSection(ScenarioWizardEditSection section, ScenarioEnvelope scenario)
        => section switch
        {
            ScenarioWizardEditSection.Finish => "Continue",
            ScenarioWizardEditSection.BasicDetails => $"Edit basic details ({scenario.Name}, {scenario.IndustryProfile}, {scenario.GeographyProfile})",
            ScenarioWizardEditSection.Realism => $"Edit realism settings (deviation: {scenario.DeviationProfile ?? ScenarioDeviationProfiles.Realistic})",
            ScenarioWizardEditSection.Identity => $"Edit identity settings (hybrid: {YesNo(scenario.Identity?.IncludeHybridDirectory)}, stale: {scenario.Identity?.StaleAccountRate:0.00})",
            ScenarioWizardEditSection.Applications => $"Edit application settings (enabled: {YesNo(scenario.Applications?.IncludeApplications)}, base: {scenario.Applications?.BaseApplicationCount ?? 0})",
            ScenarioWizardEditSection.Infrastructure => $"Edit infrastructure settings (servers: {YesNo(scenario.Infrastructure?.IncludeServers)}, telephony: {YesNo(scenario.Infrastructure?.IncludeTelephony)})",
            ScenarioWizardEditSection.Repositories => $"Edit repository settings (db: {YesNo(scenario.Repositories?.IncludeDatabases)}, files: {YesNo(scenario.Repositories?.IncludeFileShares)}, collab: {YesNo(scenario.Repositories?.IncludeCollaborationSites)})",
            ScenarioWizardEditSection.Cmdb => $"Edit CMDB settings (enabled: {YesNo(scenario.Cmdb?.IncludeConfigurationManagement)}, deviation: {scenario.Cmdb?.DeviationProfile ?? scenario.DeviationProfile ?? ScenarioDeviationProfiles.Realistic})",
            ScenarioWizardEditSection.ObservedData => $"Edit observed-data settings (enabled: {YesNo(scenario.ObservedData?.IncludeObservedViews)}, coverage: {scenario.ObservedData?.CoverageRatio ?? 0:0.00})",
            ScenarioWizardEditSection.Plugins => $"Edit plugin settings ({PluginSummary(scenario.ExternalPlugins)})",
            _ => section.ToString()
        };

    private static List<string> BuildChangeLines(ScenarioEnvelope baseline, ScenarioEnvelope scenario)
    {
        var changes = new List<string>();

        AddChange(changes, baseline.Name, scenario.Name, "Name");
        AddChange(changes, baseline.Description, scenario.Description, "Description");
        AddChange(changes, baseline.Archetype?.ToString(), scenario.Archetype?.ToString(), "Archetype");
        AddChange(changes, baseline.IndustryProfile, scenario.IndustryProfile, "Industry");
        AddChange(changes, baseline.GeographyProfile, scenario.GeographyProfile, "Geography");
        AddChange(changes, baseline.DeviationProfile, scenario.DeviationProfile, "Deviation profile");
        AddChange(changes, (baseline.CompanyCount ?? 1).ToString(), (scenario.CompanyCount ?? 1).ToString(), "Company count");
        AddChange(changes, (baseline.OfficeCount ?? 1).ToString(), (scenario.OfficeCount ?? 1).ToString(), "Office count");
        AddChange(
            changes,
            $"{baseline.EmployeeSize?.Minimum ?? 0}-{baseline.EmployeeSize?.Maximum ?? 0}",
            $"{scenario.EmployeeSize?.Minimum ?? 0}-{scenario.EmployeeSize?.Maximum ?? 0}",
            "Employee range");
        AddChange(
            changes,
            string.Join(", ", baseline.Overlays.OrderBy(item => item)),
            string.Join(", ", scenario.Overlays.OrderBy(item => item)),
            "Overlays");
        AddChange(
            changes,
            PackSummary(baseline.Packs),
            PackSummary(scenario.Packs),
            "Packs");
        AddChange(
            changes,
            $"{YesNo(baseline.Identity?.IncludeHybridDirectory)}, stale {baseline.Identity?.StaleAccountRate:0.00}",
            $"{YesNo(scenario.Identity?.IncludeHybridDirectory)}, stale {scenario.Identity?.StaleAccountRate:0.00}",
            "Identity");
        AddChange(
            changes,
            $"{YesNo(baseline.Applications?.IncludeApplications)}, base {baseline.Applications?.BaseApplicationCount ?? 0}, lob {YesNo(baseline.Applications?.IncludeLineOfBusinessApplications)}, saas {YesNo(baseline.Applications?.IncludeSaaSApplications)}",
            $"{YesNo(scenario.Applications?.IncludeApplications)}, base {scenario.Applications?.BaseApplicationCount ?? 0}, lob {YesNo(scenario.Applications?.IncludeLineOfBusinessApplications)}, saas {YesNo(scenario.Applications?.IncludeSaaSApplications)}",
            "Applications");
        AddChange(
            changes,
            $"{YesNo(baseline.Infrastructure?.IncludeServers)}, telephony {YesNo(baseline.Infrastructure?.IncludeTelephony)}",
            $"{YesNo(scenario.Infrastructure?.IncludeServers)}, telephony {YesNo(scenario.Infrastructure?.IncludeTelephony)}",
            "Infrastructure");
        AddChange(
            changes,
            $"db {YesNo(baseline.Repositories?.IncludeDatabases)}, files {YesNo(baseline.Repositories?.IncludeFileShares)}, collab {YesNo(baseline.Repositories?.IncludeCollaborationSites)}",
            $"db {YesNo(scenario.Repositories?.IncludeDatabases)}, files {YesNo(scenario.Repositories?.IncludeFileShares)}, collab {YesNo(scenario.Repositories?.IncludeCollaborationSites)}",
            "Repositories");
        AddChange(
            changes,
            $"enabled {YesNo(baseline.Cmdb?.IncludeConfigurationManagement)}, services {YesNo(baseline.Cmdb?.IncludeBusinessServices)}, cloud {YesNo(baseline.Cmdb?.IncludeCloudServices)}, deviation {baseline.Cmdb?.DeviationProfile ?? baseline.DeviationProfile ?? ScenarioDeviationProfiles.Realistic}",
            $"enabled {YesNo(scenario.Cmdb?.IncludeConfigurationManagement)}, services {YesNo(scenario.Cmdb?.IncludeBusinessServices)}, cloud {YesNo(scenario.Cmdb?.IncludeCloudServices)}, deviation {scenario.Cmdb?.DeviationProfile ?? scenario.DeviationProfile ?? ScenarioDeviationProfiles.Realistic}",
            "CMDB");
        AddChange(
            changes,
            $"enabled {YesNo(baseline.ObservedData?.IncludeObservedViews)}, coverage {baseline.ObservedData?.CoverageRatio ?? 0:0.00}",
            $"enabled {YesNo(scenario.ObservedData?.IncludeObservedViews)}, coverage {scenario.ObservedData?.CoverageRatio ?? 0:0.00}",
            "Observed data");
        AddChange(changes, PluginSummary(baseline.ExternalPlugins), PluginSummary(scenario.ExternalPlugins), "Plugins");

        return changes;
    }

    private static void AddChange(List<string> changes, string? before, string? after, string label)
    {
        before ??= string.Empty;
        after ??= string.Empty;
        if (string.Equals(before, after, StringComparison.Ordinal))
        {
            return;
        }

        changes.Add($"{label}: {before} -> {after}");
    }

    private static string YesNo(bool? value) => value == true ? "yes" : "no";

    private static string PluginSummary(ExternalPluginScenarioProfile? profile)
        => profile is { PluginRootPaths.Count: > 0, EnabledCapabilities.Count: > 0 }
            ? string.Join(", ", profile.EnabledCapabilities.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
            : "none";

    private static string Escape(string value) => Markup.Escape(value);

    private static string PackSummary(ScenarioPackProfile? profile)
        => profile is { EnabledPacks.Count: > 0 }
            ? string.Join(", ", profile.EnabledPacks.Select(pack => pack.PackId).OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
            : "none";

    private static string FormatPackSettings(ScenarioPackSelection pack)
        => pack.Settings.Count == 0
            ? "default settings"
            : string.Join(", ", pack.Settings.Select(setting => $"{setting.Key}={setting.Value}"));

    private static ScenarioPackSelection ClonePackSelection(ScenarioPackSelection selection)
        => new()
        {
            PackId = selection.PackId,
            Enabled = selection.Enabled,
            Settings = new Dictionary<string, string?>(selection.Settings, StringComparer.OrdinalIgnoreCase)
        };
}

public static class ScenarioWizardJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
}

internal static class ScenarioWizardEnvelopeInput
{
    public static ScenarioEnvelope LoadFromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Scenario path is required.", nameof(path));
        }

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Scenario file not found.", fullPath);
        }

        return FromJson(File.ReadAllText(fullPath));
    }

    public static ScenarioEnvelope FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("Scenario JSON is required.", nameof(json));
        }

        var envelope = JsonSerializer.Deserialize<ScenarioEnvelope>(json, ScenarioWizardJson.Options);
        if (envelope is not null)
        {
            return envelope;
        }

        var definition = JsonSerializer.Deserialize<ScenarioDefinition>(json, ScenarioWizardJson.Options);
        if (definition is null)
        {
            throw new InvalidOperationException("Scenario input could not be parsed.");
        }

        return FromDefinition(definition);
    }

    public static ScenarioEnvelope FromObject(object input)
    {
        return input switch
        {
            ScenarioEnvelope envelope => envelope,
            ScenarioDefinition definition => FromDefinition(definition),
            string json => FromJson(json),
            _ => FromJson(JsonSerializer.Serialize(input, ScenarioWizardJson.Options))
        };
    }

    private static ScenarioEnvelope FromDefinition(ScenarioDefinition definition)
        => new()
        {
            Name = definition.Name,
            Description = definition.Description,
            CompanyCount = definition.CompanyCount > 0 ? definition.CompanyCount : definition.Companies.Count,
            IndustryProfile = definition.IndustryProfile,
            GeographyProfile = definition.GeographyProfile,
            DeviationProfile = definition.DeviationProfile,
            EmployeeSize = definition.EmployeeSize,
            Identity = definition.Identity,
            Applications = definition.Applications,
            Infrastructure = definition.Infrastructure,
            Repositories = definition.Repositories,
            Cmdb = definition.Cmdb,
            ObservedData = definition.ObservedData,
            ExternalPlugins = definition.ExternalPlugins,
            Anomalies = definition.Anomalies.ToList(),
            Companies = definition.Companies.ToList(),
            OfficeCount = definition.Companies.Count > 0 ? definition.Companies.Max(company => company.OfficeCount) : null
        };
}
