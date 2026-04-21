namespace SyntheticEnterprise.Core.Scenarios;

using SyntheticEnterprise.Contracts.Abstractions;
using System.Text.Json;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Plugins;
using SyntheticEnterprise.Contracts.Scenarios;
using SyntheticEnterprise.Core.Plugins;

public interface IScenarioTemplateRegistry
{
    IReadOnlyCollection<ScenarioTemplateDescriptor> GetTemplates();
    ScenarioEnvelope CreateTemplate(ScenarioTemplateKind kind);
}

public interface IScenarioOverlayService
{
    ScenarioMergeResult ApplyOverlays(object baseScenario, IReadOnlyCollection<ScenarioOverlayKind> overlays);
}

public interface IScenarioDefaultsResolver
{
    ScenarioDefinition Resolve(object authoredScenario);
}

public interface IScenarioValidator
{
    ScenarioValidationResult Validate(object scenario);
}

public interface IScenarioPluginContributionResolver
{
    ScenarioPluginContributionResolution Resolve(ScenarioDefinition scenario);
}

public interface IScenarioPluginProfileHydrator
{
    ScenarioPluginHydrationResult Hydrate(ScenarioDefinition scenario);
}

public sealed class ScenarioPluginContributionResolution
{
    public List<GenerationPluginCapabilityContribution> Contributions { get; init; } = new();
    public List<ScenarioPluginAuthoringHint> AuthoringHints { get; init; } = new();
    public List<ScenarioValidationMessage> Messages { get; init; } = new();
}

public sealed class ScenarioPluginHydrationResult
{
    public required ScenarioDefinition Scenario { get; init; }
    public List<ScenarioValidationMessage> Messages { get; init; } = new();
}

public sealed class ScenarioTemplateRegistry : IScenarioTemplateRegistry
{
    public IReadOnlyCollection<ScenarioTemplateDescriptor> GetTemplates() => new[]
    {
        new ScenarioTemplateDescriptor
        {
            Kind = ScenarioTemplateKind.RegionalManufacturer,
            Name = "Regional Manufacturer",
            Description = "A balanced regional operating company with hybrid identity, on-prem infrastructure, and shared repositories.",
            RecommendedOverlays = new List<ScenarioOverlayKind> { ScenarioOverlayKind.LegacyInfrastructure, ScenarioOverlayKind.HighAnomalyDensity }
        },
        new ScenarioTemplateDescriptor
        {
            Kind = ScenarioTemplateKind.GlobalSaaS,
            Name = "Global SaaS",
            Description = "A collaboration-heavy, identity-forward software company with global offices and cloud bias.",
            RecommendedOverlays = new List<ScenarioOverlayKind> { ScenarioOverlayKind.IdentityHeavy, ScenarioOverlayKind.RemoteWorkforce, ScenarioOverlayKind.MultiRegionExpansion }
        },
        new ScenarioTemplateDescriptor
        {
            Kind = ScenarioTemplateKind.HealthcareNetwork,
            Name = "Healthcare Network",
            Description = "A regulated enterprise with distributed sites, mixed device populations, and sensitive repositories.",
            RecommendedOverlays = new List<ScenarioOverlayKind> { ScenarioOverlayKind.LegacyInfrastructure, ScenarioOverlayKind.HighAnomalyDensity }
        }
    };

    public ScenarioEnvelope CreateTemplate(ScenarioTemplateKind kind) => kind switch
    {
        ScenarioTemplateKind.GlobalSaaS => new ScenarioEnvelope
        {
            Name = "Global SaaS",
            Description = "A multi-region SaaS operator.",
            Template = kind,
            DeviationProfile = ScenarioDeviationProfiles.Realistic,
            CompanyCount = 2,
            IndustryProfile = "Software",
            GeographyProfile = "Global",
            EmployeeSize = new SizeBand { Minimum = 800, Maximum = 1800 },
            OfficeCount = 6,
            Identity = new IdentityProfile { IncludeHybridDirectory = true, IncludeM365StyleGroups = true, StaleAccountRate = 0.02 },
            Applications = new ApplicationProfile { IncludeApplications = true, BaseApplicationCount = 8, IncludeLineOfBusinessApplications = true, IncludeSaaSApplications = true },
            Infrastructure = new InfrastructureProfile { IncludeServers = true, IncludeWorkstations = true, IncludeNetworkAssets = true, IncludeTelephony = false },
            Repositories = new RepositoryProfile { IncludeDatabases = true, IncludeFileShares = true, IncludeCollaborationSites = true },
            Cmdb = new CmdbProfile(),
            ObservedData = new ObservedDataProfile(),
            Packs = new ScenarioPackProfile(),
            ExternalPlugins = new ExternalPluginScenarioProfile()
        },
        ScenarioTemplateKind.HealthcareNetwork => new ScenarioEnvelope
        {
            Name = "Healthcare Network",
            Description = "A distributed healthcare organization.",
            Template = kind,
            DeviationProfile = ScenarioDeviationProfiles.Realistic,
            CompanyCount = 1,
            IndustryProfile = "Healthcare",
            GeographyProfile = "Regional-US",
            EmployeeSize = new SizeBand { Minimum = 2200, Maximum = 4200 },
            OfficeCount = 8,
            Identity = new IdentityProfile { IncludeHybridDirectory = true, IncludeM365StyleGroups = true, StaleAccountRate = 0.05 },
            Applications = new ApplicationProfile { IncludeApplications = true, BaseApplicationCount = 7, IncludeLineOfBusinessApplications = true, IncludeSaaSApplications = true },
            Infrastructure = new InfrastructureProfile { IncludeServers = true, IncludeWorkstations = true, IncludeNetworkAssets = true, IncludeTelephony = true },
            Repositories = new RepositoryProfile { IncludeDatabases = true, IncludeFileShares = true, IncludeCollaborationSites = true },
            Cmdb = new CmdbProfile(),
            ObservedData = new ObservedDataProfile(),
            Packs = new ScenarioPackProfile(),
            ExternalPlugins = new ExternalPluginScenarioProfile()
        },
        _ => new ScenarioEnvelope
        {
            Name = "Regional Manufacturer",
            Description = "A regionally focused manufacturer with conventional enterprise layers.",
            Template = kind,
            DeviationProfile = ScenarioDeviationProfiles.Realistic,
            CompanyCount = 1,
            IndustryProfile = "Manufacturing",
            GeographyProfile = "Regional-US",
            EmployeeSize = new SizeBand { Minimum = 1800, Maximum = 2600 },
            OfficeCount = 4,
            Identity = new IdentityProfile { IncludeHybridDirectory = true, IncludeM365StyleGroups = true, StaleAccountRate = 0.04 },
            Applications = new ApplicationProfile { IncludeApplications = true, BaseApplicationCount = 6, IncludeLineOfBusinessApplications = true, IncludeSaaSApplications = true },
            Infrastructure = new InfrastructureProfile { IncludeServers = true, IncludeWorkstations = true, IncludeNetworkAssets = true, IncludeTelephony = true },
            Repositories = new RepositoryProfile { IncludeDatabases = true, IncludeFileShares = true, IncludeCollaborationSites = true },
            Cmdb = new CmdbProfile(),
            ObservedData = new ObservedDataProfile(),
            Packs = new ScenarioPackProfile(),
            ExternalPlugins = new ExternalPluginScenarioProfile()
        }
    };
}

public sealed class ScenarioOverlayService : IScenarioOverlayService
{
    public ScenarioMergeResult ApplyOverlays(object baseScenario, IReadOnlyCollection<ScenarioOverlayKind> overlays)
    {
        var envelope = ScenarioSerializationHelper.ToEnvelope(baseScenario);
        var applied = new List<string>();
        var current = envelope;

        foreach (var overlay in overlays)
        {
            current = overlay switch
            {
                ScenarioOverlayKind.IdentityHeavy => current.WithOverlay(identity: current.Identity ?? new IdentityProfile { StaleAccountRate = 0.05 }),
                ScenarioOverlayKind.RemoteWorkforce => current.WithOverlay(infrastructure: (current.Infrastructure ?? new InfrastructureProfile()) with { IncludeTelephony = false }),
                ScenarioOverlayKind.LegacyInfrastructure => current.WithOverlay(infrastructure: (current.Infrastructure ?? new InfrastructureProfile()) with { IncludeTelephony = true, IncludeServers = true }),
                ScenarioOverlayKind.HighAnomalyDensity => current.WithOverlay(anomalies: InflateAnomalies(current.Anomalies, 1.5)),
                ScenarioOverlayKind.MultiRegionExpansion => current.WithOverlay(geographyProfile: "Global", officeCount: Math.Max(current.OfficeCount ?? 3, 8)),
                _ => current
            };

            applied.Add($"Overlay:{overlay}");
        }

        return new ScenarioMergeResult
        {
            Scenario = current,
            AppliedSources = applied
        };
    }

    private static List<AnomalyProfile> InflateAnomalies(IReadOnlyCollection<AnomalyProfile> anomalies, double factor)
    {
        if (anomalies.Count == 0)
        {
            return new List<AnomalyProfile>
            {
                new() { Name = "DefaultMessyHybrid", Category = "Identity", Intensity = factor, Weight = factor }
            };
        }

        return anomalies
            .Select(a => a with { Intensity = a.Intensity * factor, Weight = a.Weight * factor })
            .ToList();
    }
}

public sealed class ScenarioDefaultsResolver : IScenarioDefaultsResolver
{
    private readonly IScenarioTemplateRegistry _templateRegistry;
    private readonly IScenarioOverlayService _overlayService;

    public ScenarioDefaultsResolver()
        : this(new ScenarioTemplateRegistry(), new ScenarioOverlayService())
    {
    }

    public ScenarioDefaultsResolver(
        IScenarioTemplateRegistry templateRegistry,
        IScenarioOverlayService overlayService)
    {
        _templateRegistry = templateRegistry;
        _overlayService = overlayService;
    }

    public ScenarioDefinition Resolve(object authoredScenario)
    {
        var envelope = ScenarioSerializationHelper.ToEnvelope(authoredScenario);

        if (envelope.Template is { } template)
        {
            envelope = Merge(_templateRegistry.CreateTemplate(template), envelope);
        }

        if (envelope.Overlays.Count > 0)
        {
            envelope = _overlayService.ApplyOverlays(envelope, envelope.Overlays).Scenario;
        }

        var scenario = new ScenarioDefinition
        {
            Name = envelope.Name,
            Description = envelope.Description,
            CompanyCount = Math.Max(1, envelope.CompanyCount ?? envelope.Companies.Count),
            IndustryProfile = envelope.IndustryProfile ?? "General",
            GeographyProfile = envelope.GeographyProfile ?? "Regional-US",
            DeviationProfile = ResolveDeviationProfile(envelope.DeviationProfile),
            EmployeeSize = envelope.EmployeeSize ?? new SizeBand(),
            Identity = envelope.Identity ?? new IdentityProfile(),
            Applications = envelope.Applications ?? new ApplicationProfile(),
            Infrastructure = envelope.Infrastructure ?? new InfrastructureProfile(),
            Repositories = envelope.Repositories ?? new RepositoryProfile(),
            Cmdb = envelope.Cmdb ?? new CmdbProfile(),
            ObservedData = envelope.ObservedData ?? new ObservedDataProfile(),
            Packs = envelope.Packs ?? new ScenarioPackProfile(),
            ExternalPlugins = envelope.ExternalPlugins ?? new ExternalPluginScenarioProfile(),
            Anomalies = NormalizeAnomalies(envelope.Anomalies),
            Companies = envelope.Companies.Count > 0
                ? envelope.Companies.ToList()
                : MaterializeCompanies(envelope)
        };

        return scenario with { CompanyCount = scenario.Companies.Count };
    }

    private static ScenarioEnvelope Merge(ScenarioEnvelope template, ScenarioEnvelope overlay)
    {
        return new ScenarioEnvelope
        {
            Name = overlay.Name != "Default" ? overlay.Name : template.Name,
            Description = overlay.Description != "Synthetic enterprise scenario" ? overlay.Description : template.Description,
            Template = overlay.Template ?? template.Template,
            Overlays = overlay.Overlays.Count > 0 ? overlay.Overlays.ToList() : template.Overlays.ToList(),
            CompanyCount = overlay.CompanyCount ?? template.CompanyCount,
            IndustryProfile = overlay.IndustryProfile ?? template.IndustryProfile,
            GeographyProfile = overlay.GeographyProfile ?? template.GeographyProfile,
            DeviationProfile = overlay.DeviationProfile ?? template.DeviationProfile,
            EmployeeSize = overlay.EmployeeSize ?? template.EmployeeSize,
            Identity = overlay.Identity ?? template.Identity,
            Applications = overlay.Applications ?? template.Applications,
            Infrastructure = overlay.Infrastructure ?? template.Infrastructure,
            Repositories = overlay.Repositories ?? template.Repositories,
            Cmdb = overlay.Cmdb ?? template.Cmdb,
            ObservedData = overlay.ObservedData ?? template.ObservedData,
            Packs = overlay.Packs ?? template.Packs,
            ExternalPlugins = overlay.ExternalPlugins ?? template.ExternalPlugins,
            Anomalies = overlay.Anomalies.Count > 0 ? overlay.Anomalies.ToList() : template.Anomalies.ToList(),
            Companies = overlay.Companies.Count > 0 ? overlay.Companies.ToList() : template.Companies.ToList(),
            OfficeCount = overlay.OfficeCount ?? template.OfficeCount
        };
    }

    private static List<ScenarioCompanyDefinition> MaterializeCompanies(ScenarioEnvelope envelope)
    {
        var companyCount = Math.Max(1, envelope.CompanyCount ?? 1);
        var employeeCount = ResolveEmployeeCount(envelope.EmployeeSize);
        var countries = ResolveCountries(envelope.GeographyProfile);
        var officeCount = Math.Max(1, envelope.OfficeCount ?? ResolveOfficeCount(companyCount, countries.Count));
        var companyBaseName = string.IsNullOrWhiteSpace(envelope.Name) ? "Synthetic Enterprise" : envelope.Name;

        return Enumerable.Range(1, companyCount)
            .Select(index => new ScenarioCompanyDefinition
            {
                Name = companyCount == 1 ? companyBaseName : $"{companyBaseName} {index}",
                Industry = envelope.IndustryProfile ?? "General",
                EmployeeCount = employeeCount,
                BusinessUnitCount = Math.Clamp(employeeCount / 600, 3, 8),
                DepartmentCountPerBusinessUnit = employeeCount > 2000 ? 4 : 3,
                TeamCountPerDepartment = employeeCount > 1200 ? 3 : 2,
                OfficeCount = officeCount,
                AddressMode = "CatalogBacked",
                IncludeGeocodes = true,
                SharedMailboxCount = Math.Max(3, employeeCount / 450),
                ServiceAccountCount = Math.Max(4, employeeCount / 250),
                IncludePrivilegedAccounts = true,
                WorkstationCoverageRatio = 0.92,
                ServerCount = Math.Max(6, employeeCount / 80),
                NetworkAssetCountPerOffice = 6,
                TelephonyAssetCountPerOffice = envelope.Infrastructure?.IncludeTelephony == false ? 0 : 18,
                DatabaseCount = Math.Max(4, employeeCount / 140),
                FileShareCount = Math.Max(4, employeeCount / 180),
                CollaborationSiteCount = Math.Max(8, employeeCount / 60),
                Countries = countries
            })
            .ToList();
    }

    private static List<AnomalyProfile> NormalizeAnomalies(IReadOnlyCollection<AnomalyProfile> anomalies)
    {
        return anomalies.Select(a => a with
        {
            Intensity = a.Intensity <= 0 ? a.Weight : a.Intensity,
            Weight = a.Weight <= 0 ? a.Intensity : a.Weight
        }).ToList();
    }

    private static string ResolveDeviationProfile(string? deviationProfile)
        => string.IsNullOrWhiteSpace(deviationProfile)
            ? ScenarioDeviationProfiles.Realistic
            : deviationProfile;

    private static int ResolveEmployeeCount(SizeBand? sizeBand)
    {
        if (sizeBand is null)
        {
            return 250;
        }

        if (sizeBand.Minimum <= 0 && sizeBand.Maximum <= 0)
        {
            return 250;
        }

        if (sizeBand.Maximum <= sizeBand.Minimum)
        {
            return Math.Max(1, sizeBand.Minimum);
        }

        return (sizeBand.Minimum + sizeBand.Maximum) / 2;
    }

    private static List<string> ResolveCountries(string? geographyProfile)
    {
        return geographyProfile?.Trim().ToLowerInvariant() switch
        {
            "global" => new List<string> { "United States", "United Kingdom", "Germany", "India", "Japan" },
            "north-america" => new List<string> { "United States", "Canada", "Mexico" },
            "regional-us" => new List<string> { "United States" },
            _ => new List<string> { "United States" }
        };
    }

    private static int ResolveOfficeCount(int companyCount, int countryCount)
        => Math.Max(2, Math.Max(companyCount, countryCount) * 2);
}

public sealed class ScenarioValidator : IScenarioValidator
{
    private readonly IScenarioDefaultsResolver _resolver;
    private readonly IScenarioPluginProfileHydrator _pluginProfileHydrator;
    private readonly IScenarioPluginContributionResolver _pluginContributionResolver;

    public ScenarioValidator()
        : this(
            new ScenarioDefaultsResolver(),
            new ScenarioPluginProfileHydrator(
                new ScenarioPackProfileResolver(new FirstPartyPackPathResolver()),
                new ExternalPluginScenarioBindingService(),
                new ExternalPluginCapabilityResolver(
                    new GenerationPluginRegistry(
                        Array.Empty<IWorldGenerationPlugin>(),
                        new FileSystemExternalGenerationPluginCatalog(
                            new GenerationPluginManifestValidator(new DataOnlyGenerationPluginSecurityPolicy()),
                            new DataOnlyGenerationPluginSecurityPolicy(),
                            new AllowListExternalPluginTrustPolicy())))),
            new ScenarioPluginContributionResolver(
                new ScenarioPackProfileResolver(new FirstPartyPackPathResolver()),
                new ExternalPluginScenarioBindingService(),
                new ExternalPluginCapabilityResolver(
                    new GenerationPluginRegistry(
                        Array.Empty<IWorldGenerationPlugin>(),
                        new FileSystemExternalGenerationPluginCatalog(
                            new GenerationPluginManifestValidator(new DataOnlyGenerationPluginSecurityPolicy()),
                            new DataOnlyGenerationPluginSecurityPolicy(),
                            new AllowListExternalPluginTrustPolicy())))))
    {
    }

    public ScenarioValidator(
        IScenarioDefaultsResolver resolver,
        IScenarioPluginProfileHydrator pluginProfileHydrator,
        IScenarioPluginContributionResolver pluginContributionResolver)
    {
        _resolver = resolver;
        _pluginProfileHydrator = pluginProfileHydrator;
        _pluginContributionResolver = pluginContributionResolver;
    }

    public ScenarioValidationResult Validate(object scenario)
    {
        var resolved = _resolver.Resolve(scenario);
        var hydration = _pluginProfileHydrator.Hydrate(resolved);
        var messages = hydration.Messages.ToList();
        resolved = hydration.Scenario;
        var contributionResolution = _pluginContributionResolver.Resolve(resolved);

        if (resolved.CompanyCount <= 0)
        {
            messages.Add(new ScenarioValidationMessage("company-count", ScenarioValidationSeverity.Error, "$.companyCount", "Scenario must resolve to at least one company."));
        }

        if (resolved.EmployeeSize.Maximum > 0 && resolved.EmployeeSize.Maximum < resolved.EmployeeSize.Minimum)
        {
            messages.Add(new ScenarioValidationMessage("employee-size-range", ScenarioValidationSeverity.Error, "$.employeeSize", "EmployeeSize maximum must be greater than or equal to minimum."));
        }

        if (resolved.Companies.Count == 0)
        {
            messages.Add(new ScenarioValidationMessage("companies-empty", ScenarioValidationSeverity.Error, "$.companies", "Scenario must resolve at least one company definition."));
        }

        if (!ScenarioDeviationProfiles.All.Contains(resolved.DeviationProfile, StringComparer.OrdinalIgnoreCase))
        {
            messages.Add(new ScenarioValidationMessage(
                "deviation-profile",
                ScenarioValidationSeverity.Error,
                "$.deviationProfile",
                $"DeviationProfile must be one of: {string.Join(", ", ScenarioDeviationProfiles.All)}."));
        }

        if (!string.IsNullOrWhiteSpace(resolved.Cmdb.DeviationProfile)
            && !ScenarioDeviationProfiles.All.Contains(resolved.Cmdb.DeviationProfile, StringComparer.OrdinalIgnoreCase))
        {
            messages.Add(new ScenarioValidationMessage(
                "cmdb-deviation-profile",
                ScenarioValidationSeverity.Error,
                "$.cmdb.deviationProfile",
                $"CMDB deviation profile override must be one of: {string.Join(", ", ScenarioDeviationProfiles.All)}."));
        }

        foreach (var company in resolved.Companies)
        {
            if (company.EmployeeCount <= 0)
            {
                messages.Add(new ScenarioValidationMessage("employee-count", ScenarioValidationSeverity.Error, "$.companies[].employeeCount", $"Company '{company.Name}' must have a positive employee count."));
            }

            if (company.Countries.Count == 0)
            {
                messages.Add(new ScenarioValidationMessage("countries-empty", ScenarioValidationSeverity.Warning, "$.companies[].countries", $"Company '{company.Name}' resolved with no countries and will fall back to default geography."));
            }
        }

        messages.AddRange(contributionResolution.Messages);

        return new ScenarioValidationResult
        {
            IsValid = messages.All(m => m.Severity != ScenarioValidationSeverity.Error),
            Messages = messages,
            Contributions = contributionResolution.Contributions,
            AuthoringHints = contributionResolution.AuthoringHints,
            ResolvedScenario = resolved
        };
    }
}

public sealed class ScenarioPluginProfileHydrator : IScenarioPluginProfileHydrator
{
    private readonly IScenarioPackProfileResolver _packProfileResolver;
    private readonly IExternalPluginScenarioBindingService _bindingService;
    private readonly IExternalPluginCapabilityResolver _capabilityResolver;

    public ScenarioPluginProfileHydrator(
        IScenarioPackProfileResolver packProfileResolver,
        IExternalPluginScenarioBindingService bindingService,
        IExternalPluginCapabilityResolver capabilityResolver)
    {
        _packProfileResolver = packProfileResolver;
        _bindingService = bindingService;
        _capabilityResolver = capabilityResolver;
    }

    public ScenarioPluginHydrationResult Hydrate(ScenarioDefinition scenario)
    {
        var profile = _packProfileResolver.Resolve(scenario);
        if (profile.PluginRootPaths.Count == 0 || profile.EnabledCapabilities.Count == 0)
        {
            return new ScenarioPluginHydrationResult
            {
                Scenario = scenario
            };
        }

        var executionSettings = _bindingService.Bind(profile);
        if (!executionSettings.Enabled)
        {
            return new ScenarioPluginHydrationResult
            {
                Scenario = scenario
            };
        }

        var plan = _capabilityResolver.Resolve(new GenerationContext
        {
            Scenario = scenario,
            ExternalPlugins = executionSettings
        });

        if (plan.Contributions.Count == 0)
        {
            return new ScenarioPluginHydrationResult
            {
                Scenario = scenario
            };
        }

        var messages = new List<ScenarioValidationMessage>();
        var configurations = profile.CapabilityConfigurations
            .Select(configuration => new ExternalPluginCapabilityConfiguration
            {
                Capability = configuration.Capability,
                Settings = new Dictionary<string, string?>(configuration.Settings, StringComparer.OrdinalIgnoreCase)
            })
            .ToList();

        foreach (var contribution in plan.Contributions)
        {
            var configuration = configurations
                .FirstOrDefault(item => item.Capability.Equals(contribution.Capability, StringComparison.OrdinalIgnoreCase));

            if (configuration is null)
            {
                configuration = new ExternalPluginCapabilityConfiguration
                {
                    Capability = contribution.Capability,
                    Settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                };
                configurations.Add(configuration);
            }

            foreach (var parameter in contribution.Parameters)
            {
                if (!TryConvertDefaultValue(parameter.DefaultValue, out var defaultValue)
                    || string.IsNullOrWhiteSpace(defaultValue))
                {
                    continue;
                }

                if (configuration.Settings.TryGetValue(parameter.Name, out var existingValue)
                    && !string.IsNullOrWhiteSpace(existingValue))
                {
                    continue;
                }

                configuration.Settings[parameter.Name] = defaultValue;
                messages.Add(new ScenarioValidationMessage(
                    "external-plugin-default-applied",
                    ScenarioValidationSeverity.Info,
                    $"$.externalPlugins.capabilityConfigurations[{contribution.Capability}].settings.{parameter.Name}",
                    $"Applied plugin default for '{contribution.Capability}.{parameter.Name}' = '{defaultValue}'."));
            }
        }

        return new ScenarioPluginHydrationResult
        {
            Scenario = scenario with
            {
                ExternalPlugins = new ExternalPluginScenarioProfile
                {
                    PluginRootPaths = profile.PluginRootPaths.ToList(),
                    EnabledCapabilities = profile.EnabledCapabilities.ToList(),
                    CapabilityConfigurations = configurations,
                    AllowAssemblyPlugins = profile.AllowAssemblyPlugins,
                    ExecutionTimeoutSeconds = profile.ExecutionTimeoutSeconds,
                    MaxGeneratedRecords = profile.MaxGeneratedRecords,
                    MaxWarningCount = profile.MaxWarningCount,
                    MaxDiagnosticEntries = profile.MaxDiagnosticEntries,
                    MaxDiagnosticCharacters = profile.MaxDiagnosticCharacters,
                    MaxInputPayloadBytes = profile.MaxInputPayloadBytes,
                    MaxOutputPayloadBytes = profile.MaxOutputPayloadBytes,
                    RequireContentHashAllowList = profile.RequireContentHashAllowList,
                    RequireAssemblyHashApproval = profile.RequireAssemblyHashApproval,
                    AllowedContentHashes = profile.AllowedContentHashes.ToList()
                }
            },
            Messages = messages
        };
    }

    internal static bool TryConvertDefaultValue(object? value, out string? converted)
    {
        switch (value)
        {
            case null:
                converted = null;
                return false;
            case string text:
                converted = text;
                return true;
            case JsonElement jsonElement:
                converted = jsonElement.ValueKind switch
                {
                    JsonValueKind.String => jsonElement.GetString(),
                    JsonValueKind.Number => jsonElement.ToString(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Null => null,
                    _ => jsonElement.ToString()
                };
                return !string.IsNullOrWhiteSpace(converted);
            default:
                converted = value.ToString();
                return !string.IsNullOrWhiteSpace(converted);
        }
    }
}

public sealed class ScenarioPluginContributionResolver : IScenarioPluginContributionResolver
{
    private readonly IScenarioPackProfileResolver _packProfileResolver;
    private readonly IExternalPluginScenarioBindingService _bindingService;
    private readonly IExternalPluginCapabilityResolver _capabilityResolver;

    public ScenarioPluginContributionResolver(
        IScenarioPackProfileResolver packProfileResolver,
        IExternalPluginScenarioBindingService bindingService,
        IExternalPluginCapabilityResolver capabilityResolver)
    {
        _packProfileResolver = packProfileResolver;
        _bindingService = bindingService;
        _capabilityResolver = capabilityResolver;
    }

    public ScenarioPluginContributionResolution Resolve(ScenarioDefinition scenario)
    {
        var profile = _packProfileResolver.Resolve(scenario);
        if (profile.PluginRootPaths.Count == 0 || profile.EnabledCapabilities.Count == 0)
        {
            return new ScenarioPluginContributionResolution();
        }

        var executionSettings = _bindingService.Bind(profile);
        if (!executionSettings.Enabled)
        {
            return new ScenarioPluginContributionResolution();
        }

        var plan = _capabilityResolver.Resolve(new GenerationContext
        {
            Scenario = scenario,
            ExternalPlugins = executionSettings
        });

        var messages = plan.Warnings
            .Select(warning => new ScenarioValidationMessage(
                "external-plugin-plan",
                ScenarioValidationSeverity.Warning,
                "$.externalPlugins",
                warning))
            .ToList();

        var contributionsByCapability = plan.Contributions
            .ToDictionary(contribution => contribution.Capability, StringComparer.OrdinalIgnoreCase);

        foreach (var duplicateCapability in profile.CapabilityConfigurations
                     .Where(configuration => !string.IsNullOrWhiteSpace(configuration.Capability))
                     .GroupBy(configuration => configuration.Capability, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1)
                     .Select(group => group.Key))
        {
            messages.Add(new ScenarioValidationMessage(
                "external-plugin-duplicate-configuration",
                ScenarioValidationSeverity.Error,
                "$.externalPlugins.capabilityConfigurations",
                $"Capability '{duplicateCapability}' is configured more than once."));
        }

        foreach (var configuration in profile.CapabilityConfigurations.Where(configuration => !string.IsNullOrWhiteSpace(configuration.Capability)))
        {
            if (!contributionsByCapability.TryGetValue(configuration.Capability, out var contribution))
            {
                messages.Add(new ScenarioValidationMessage(
                    "external-plugin-unknown-capability-configuration",
                    ScenarioValidationSeverity.Warning,
                    "$.externalPlugins.capabilityConfigurations",
                    $"Capability configuration for '{configuration.Capability}' does not match a discovered enabled plugin capability."));
                continue;
            }

            var parametersByName = contribution.Parameters
                .ToDictionary(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var setting in configuration.Settings)
            {
                if (parametersByName.ContainsKey(setting.Key))
                {
                    continue;
                }

                messages.Add(new ScenarioValidationMessage(
                    "external-plugin-unknown-setting",
                    ScenarioValidationSeverity.Warning,
                    $"$.externalPlugins.capabilityConfigurations[{configuration.Capability}].settings.{setting.Key}",
                    $"Setting '{setting.Key}' is not declared by plugin capability '{configuration.Capability}'."));
            }

            foreach (var parameter in contribution.Parameters.Where(parameter => parameter.Required && parameter.DefaultValue is null))
            {
                if (configuration.Settings.TryGetValue(parameter.Name, out var value)
                    && !string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                messages.Add(new ScenarioValidationMessage(
                    "external-plugin-missing-required-setting",
                    ScenarioValidationSeverity.Error,
                    $"$.externalPlugins.capabilityConfigurations[{configuration.Capability}].settings.{parameter.Name}",
                    $"Plugin capability '{configuration.Capability}' requires setting '{parameter.Name}'."));
            }
        }

        foreach (var contribution in plan.Contributions)
        {
            var configuration = profile.CapabilityConfigurations
                .FirstOrDefault(item => item.Capability.Equals(contribution.Capability, StringComparison.OrdinalIgnoreCase));

            foreach (var parameter in contribution.Parameters.Where(parameter => parameter.Required && parameter.DefaultValue is null))
            {
                if (configuration is not null
                    && configuration.Settings.TryGetValue(parameter.Name, out var value)
                    && !string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                messages.Add(new ScenarioValidationMessage(
                    "external-plugin-missing-required-setting",
                    ScenarioValidationSeverity.Error,
                    $"$.externalPlugins.capabilityConfigurations[{contribution.Capability}].settings.{parameter.Name}",
                    $"Plugin capability '{contribution.Capability}' requires setting '{parameter.Name}'."));
            }
        }

        return new ScenarioPluginContributionResolution
        {
            Contributions = plan.Contributions,
            AuthoringHints = BuildAuthoringHints(plan.Contributions, profile),
            Messages = messages
        };
    }

    private static List<ScenarioPluginAuthoringHint> BuildAuthoringHints(
        IReadOnlyCollection<GenerationPluginCapabilityContribution> contributions,
        ExternalPluginScenarioProfile profile)
    {
        return contributions
            .Select(contribution =>
            {
                var existingConfiguration = profile.CapabilityConfigurations
                    .FirstOrDefault(item => item.Capability.Equals(contribution.Capability, StringComparison.OrdinalIgnoreCase));

                var suggestedSettings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                if (existingConfiguration is not null)
                {
                    foreach (var setting in existingConfiguration.Settings)
                    {
                        suggestedSettings[setting.Key] = setting.Value;
                    }
                }

                foreach (var parameter in contribution.Parameters)
                {
                    if (suggestedSettings.ContainsKey(parameter.Name))
                    {
                        continue;
                    }

                    if (ScenarioPluginProfileHydrator.TryConvertDefaultValue(parameter.DefaultValue, out var defaultValue))
                    {
                        suggestedSettings[parameter.Name] = defaultValue;
                    }
                }

                return new ScenarioPluginAuthoringHint
                {
                    Capability = contribution.Capability,
                    DisplayName = contribution.DisplayName,
                    SuggestedSettings = suggestedSettings,
                    Parameters = contribution.Parameters.ToList(),
                    Metadata = new Dictionary<string, string?>(contribution.Metadata, StringComparer.OrdinalIgnoreCase)
                };
            })
            .ToList();
    }
}

internal static class ScenarioSerializationHelper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static ScenarioEnvelope ToEnvelope(object input)
    {
        if (input is ScenarioMergeResult mergeResult)
        {
            return mergeResult.Scenario;
        }

        if (input is ScenarioEnvelope envelope)
        {
            return envelope;
        }

        if (input is ScenarioDefinition definition)
        {
            return new ScenarioEnvelope
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
                Packs = definition.Packs,
                ExternalPlugins = definition.ExternalPlugins,
                Anomalies = definition.Anomalies.ToList(),
                Companies = definition.Companies.ToList()
            };
        }

        if (input is string json)
        {
            return JsonSerializer.Deserialize<ScenarioEnvelope>(json, JsonOptions)
                ?? throw new InvalidOperationException("Scenario input could not be parsed.");
        }

        var serialized = JsonSerializer.Serialize(input, JsonOptions);
        return JsonSerializer.Deserialize<ScenarioEnvelope>(serialized, JsonOptions)
            ?? throw new InvalidOperationException("Scenario input could not be converted into a scenario envelope.");
    }
}

file static class ScenarioEnvelopeExtensions
{
    public static ScenarioEnvelope WithOverlay(
        this ScenarioEnvelope envelope,
        IdentityProfile? identity = null,
        InfrastructureProfile? infrastructure = null,
        string? geographyProfile = null,
        int? officeCount = null,
        List<AnomalyProfile>? anomalies = null)
    {
        return new ScenarioEnvelope
        {
            Name = envelope.Name,
            Description = envelope.Description,
            Template = envelope.Template,
            Overlays = envelope.Overlays.ToList(),
            CompanyCount = envelope.CompanyCount,
            IndustryProfile = envelope.IndustryProfile,
            GeographyProfile = geographyProfile ?? envelope.GeographyProfile,
            DeviationProfile = envelope.DeviationProfile,
            EmployeeSize = envelope.EmployeeSize,
            Identity = identity ?? envelope.Identity,
            Applications = envelope.Applications,
            Infrastructure = infrastructure ?? envelope.Infrastructure,
            Repositories = envelope.Repositories,
            Cmdb = envelope.Cmdb,
            ObservedData = envelope.ObservedData,
            Packs = envelope.Packs,
            ExternalPlugins = envelope.ExternalPlugins,
            Anomalies = anomalies ?? envelope.Anomalies.ToList(),
            Companies = envelope.Companies.ToList(),
            OfficeCount = officeCount ?? envelope.OfficeCount
        };
    }
}
