namespace SyntheticEnterprise.Contracts.Scenarios;

using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Plugins;

public enum ScenarioTemplateKind
{
    RegionalManufacturer,
    GlobalSaaS,
    HealthcareNetwork,
    FinancialServices,
    HigherEducation
}

public enum ScenarioArchetypeKind
{
    RegionalManufacturer,
    GlobalSaaS,
    HealthcareNetwork,
    PublicSectorAgency,
    RetailDistribution
}

public enum ScenarioOverlayKind
{
    FastGrowth,
    PostMerger,
    ComplianceHeavy,
    UnderGoverned,
    Modernization,
    IdentityHeavy,
    RemoteWorkforce,
    LegacyInfrastructure,
    HighAnomalyDensity,
    MultiRegionExpansion
}

public enum ScenarioValidationSeverity
{
    Info,
    Warning,
    Error
}

public sealed record ScenarioValidationMessage(
    string Code,
    ScenarioValidationSeverity Severity,
    string Path,
    string Message);

public sealed class ScenarioValidationResult
{
    public bool IsValid { get; init; }
    public List<ScenarioValidationMessage> Messages { get; init; } = new();
    public List<GenerationPluginCapabilityContribution> Contributions { get; init; } = new();
    public List<ScenarioPluginAuthoringHint> AuthoringHints { get; init; } = new();
    public ScenarioDefinition? ResolvedScenario { get; init; }
}

public sealed class ScenarioPluginAuthoringHint
{
    public required string Capability { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public Dictionary<string, string?> SuggestedSettings { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<PluginParameterDescriptor> Parameters { get; init; } = new();
    public Dictionary<string, string?> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ScenarioTemplateDescriptor
{
    public required ScenarioTemplateKind Kind { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public List<ScenarioOverlayKind> RecommendedOverlays { get; init; } = new();
    public List<GenerationPluginCapabilityContribution> PluginContributions { get; init; } = new();
    public List<ScenarioPluginAuthoringHint> PluginAuthoringHints { get; init; } = new();
}

public sealed class ScenarioArchetypeDescriptor
{
    public required ScenarioArchetypeKind Kind { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public string IndustryProfile { get; init; } = string.Empty;
    public string GeographyProfile { get; init; } = string.Empty;
    public List<ScenarioOverlayKind> RecommendedOverlays { get; init; } = new();
    public List<GenerationPluginCapabilityContribution> PluginContributions { get; init; } = new();
    public List<ScenarioPluginAuthoringHint> PluginAuthoringHints { get; init; } = new();
}

public sealed class ScenarioMergeResult
{
    public ScenarioEnvelope Scenario { get; init; } = new();
    public List<string> AppliedSources { get; init; } = new();
    public List<ScenarioValidationMessage> Messages { get; init; } = new();
}

public sealed class ScenarioEnvelope
{
    public string Name { get; init; } = "Default";
    public string Description { get; init; } = "Synthetic enterprise scenario";
    public ScenarioArchetypeKind? Archetype { get; init; }
    public ScenarioTemplateKind? Template { get; init; }
    public List<ScenarioOverlayKind> Overlays { get; init; } = new();
    public int? CompanyCount { get; init; }
    public string? IndustryProfile { get; init; }
    public string? GeographyProfile { get; init; }
    public string? DeviationProfile { get; init; }
    public SizeBand? EmployeeSize { get; init; }
    public IdentityProfile? Identity { get; init; }
    public ApplicationProfile? Applications { get; init; }
    public InfrastructureProfile? Infrastructure { get; init; }
    public RepositoryProfile? Repositories { get; init; }
    public CmdbProfile? Cmdb { get; init; }
    public ObservedDataProfile? ObservedData { get; init; }
    public TimelineProfile? Timeline { get; init; }
    public ScenarioPackProfile? Packs { get; init; }
    public ExternalPluginScenarioProfile? ExternalPlugins { get; init; }
    public List<AnomalyProfile> Anomalies { get; init; } = new();
    public List<ScenarioCompanyDefinition> Companies { get; init; } = new();
    public int? OfficeCount { get; init; }
}
