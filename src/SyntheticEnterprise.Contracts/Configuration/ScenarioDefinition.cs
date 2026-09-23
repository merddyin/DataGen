namespace SyntheticEnterprise.Contracts.Configuration;

using SyntheticEnterprise.Contracts.Plugins;
using SyntheticEnterprise.Contracts.Scenarios;

public record ScenarioDefinition
{
    public string Name { get; init; } = "Default";
    public string Description { get; init; } = "Synthetic enterprise scenario";
    public ScenarioArchetypeKind? Archetype { get; init; }
    public List<ScenarioPersonaKind> Personas { get; init; } = new();
    public int CompanyCount { get; init; } = 1;
    public string IndustryProfile { get; init; } = "General";
    public string GeographyProfile { get; init; } = "Regional";
    public string DeviationProfile { get; init; } = ScenarioDeviationProfiles.Realistic;
    public SizeBand EmployeeSize { get; init; } = new();
    public IdentityProfile Identity { get; init; } = new();
    public ApplicationProfile Applications { get; init; } = new();
    public InfrastructureProfile Infrastructure { get; init; } = new();
    public RepositoryProfile Repositories { get; init; } = new();
    public CmdbProfile Cmdb { get; init; } = new();
    public ObservedDataProfile ObservedData { get; init; } = new();
    public TimelineProfile Timeline { get; init; } = new();
    public ScenarioPackProfile Packs { get; init; } = new();
    public ExternalPluginScenarioProfile ExternalPlugins { get; init; } = new();
    public List<ScenarioCompanyDefinition> Companies { get; init; } = new();
    public List<AnomalyProfile> Anomalies { get; init; } = new();
}

public static class ScenarioDeviationProfiles
{
    public const string Clean = "Clean";
    public const string Realistic = "Realistic";
    public const string Aggressive = "Aggressive";

    public static IReadOnlyList<string> All { get; } = new[]
    {
        Clean,
        Realistic,
        Aggressive
    };
}

public record SizeBand
{
    public int Minimum { get; init; } = 100;
    public int Maximum { get; init; } = 500;
}

public record ScenarioCompanyDefinition
{
    public string Name { get; init; } = "Contoso Dynamics";
    public string Industry { get; init; } = "Technology";
    public int EmployeeCount { get; init; } = 250;
    public int BusinessUnitCount { get; init; } = 3;
    public int DepartmentCountPerBusinessUnit { get; init; } = 3;
    public int TeamCountPerDepartment { get; init; } = 2;
    public int OfficeCount { get; init; } = 3;
    public string AddressMode { get; init; } = "Hybrid";
    public bool IncludeGeocodes { get; init; } = false;
    public int SharedMailboxCount { get; init; } = 5;
    public int ServiceAccountCount { get; init; } = 8;
    public bool IncludePrivilegedAccounts { get; init; } = true;
    public double WorkstationCoverageRatio { get; init; } = 0.92;
    public int ServerCount { get; init; } = 24;
    public int NetworkAssetCountPerOffice { get; init; } = 6;
    public int TelephonyAssetCountPerOffice { get; init; } = 20;
    public int DatabaseCount { get; init; } = 18;
    public int FileShareCount { get; init; } = 12;
    public int CollaborationSiteCount { get; init; } = 20;
    public int AcquiredCompanyCount { get; init; }
    public List<string> Countries { get; init; } = new();
}

public record IdentityProfile
{
    public bool IncludeHybridDirectory { get; init; } = true;
    public bool IncludeM365StyleGroups { get; init; } = true;
    public bool IncludeAdministrativeTiers { get; init; } = true;
    public bool IncludeEnvironmentDefaults { get; init; } = true;
    public bool IncludeExternalWorkforce { get; init; } = true;
    public bool IncludeB2BGuests { get; init; } = true;
    public double ContractorRatio { get; init; } = 0.06;
    public double ManagedServiceProviderRatio { get; init; } = 0.01;
    public double GuestUserRatio { get; init; } = 0.025;
    public double StaleAccountRate { get; init; } = 0.03;

    /// <summary>
    /// Upper bound on <see cref="AccountOwnershipConditionCount"/>. Each unit adds a fixed
    /// group of directory objects to every company, so the option is bounded rather than
    /// open-ended.
    /// </summary>
    public const int MaximumAccountOwnershipConditionCount = 25;

    /// <summary>
    /// Number of times each documented account ownership condition is emitted per company: a
    /// second account a person genuinely holds, an account whose name attributes were never
    /// corrected when its holder changed, a duplicate object for one holder, a disabled object
    /// retained after it was superseded, a hand-made object that was never linked to an employee
    /// record, an account no owner is recorded for, a shared mailbox several named people hold
    /// access to, and an account whose only ownership record is prose. Zero, the default, emits
    /// none. A company that cannot supply the people or the shared mailbox a condition needs
    /// emits that condition fewer times; people and mailboxes are never invented to reach the
    /// count.
    /// </summary>
    public int AccountOwnershipConditionCount { get; init; }
}

public record ApplicationProfile
{
    public bool IncludeApplications { get; init; } = true;
    public int BaseApplicationCount { get; init; } = 6;
    public bool IncludeLineOfBusinessApplications { get; init; } = true;
    public bool IncludeSaaSApplications { get; init; } = true;
}

public record InfrastructureProfile
{
    public bool IncludeServers { get; init; } = true;
    public bool IncludeWorkstations { get; init; } = true;
    public bool IncludeNetworkAssets { get; init; } = true;
    public bool IncludeTelephony { get; init; } = true;
    public bool IncludeRepresentativeManagementObservations { get; init; }
    /// <summary>
    /// Enables management-observation generation when greater than zero and limits the
    /// default representative sample per company. Explicit population coverage replaces
    /// this limit while retaining it as the enablement switch.
    /// </summary>
    public int RepresentativeManagementObservationCount { get; init; } = 15;
    /// <summary>
    /// Percentage of each endpoint cohort that receives a current management observation.
    /// Set to a value from 1 through 100 to opt into deterministic population coverage.
    /// Zero preserves the bounded representative-sample behavior.
    /// </summary>
    public int ManagementObservationPopulationCoveragePercentage { get; init; }
    /// <summary>
    /// Maximum historical management observations emitted per company in addition to
    /// <see cref="RepresentativeManagementObservationCount"/>. Zero disables history.
    /// </summary>
    public int RepresentativeManagementHistoryObservationCount { get; init; } = 1;
    public int HostedComputeObservationPercentage { get; init; } = 20;

    /// <summary>
    /// Upper bound on <see cref="EffectiveSecurityConfigurationEndpointCount"/>. The
    /// families emitted per endpoint are large, so the option is bounded rather than
    /// open-ended.
    /// </summary>
    public const int MaximumEffectiveSecurityConfigurationEndpointCount = 250;

    /// <summary>
    /// Number of Windows endpoints per company that report their effective local security
    /// configuration — privilege rights, advanced audit subcategories and object security
    /// descriptors in the vocabulary <c>secedit</c> and <c>auditpol</c> produce. Zero, the
    /// default, emits none. A company holding fewer Windows endpoints than requested
    /// reports on every endpoint it has; endpoints are never invented to reach the count.
    /// </summary>
    public int EffectiveSecurityConfigurationEndpointCount { get; init; }
}

public record RepositoryProfile
{
    public bool IncludeDatabases { get; init; } = true;
    public bool IncludeFileShares { get; init; } = true;
    public bool IncludeCollaborationSites { get; init; } = true;
}

public record CmdbProfile
{
    public bool IncludeConfigurationManagement { get; init; } = false;
    public bool IncludeBusinessServices { get; init; } = true;
    public bool IncludeCloudServices { get; init; } = true;
    public bool IncludeAutoDiscoveryRecords { get; init; } = true;
    public bool IncludeServiceCatalogRecords { get; init; } = true;
    public bool IncludeSpreadsheetImportRecords { get; init; } = true;
    public string? DeviationProfile { get; init; }
}

public record ObservedDataProfile
{
    public bool IncludeObservedViews { get; init; } = true;
    public double CoverageRatio { get; init; } = 0.7;
}

public record TimelineProfile
{
    public bool Enabled { get; init; }
    public string StartAtUtc { get; init; } = "2026-01-01T00:00:00Z";
    public int DurationDays { get; init; } = 30;
    public List<int> SnapshotDays { get; init; } = new() { 0, 15, 30 };
    public string DefaultSnapshotMode { get; init; } = "AsOfDate";
}

public record AnomalyProfile
{
    public string Name { get; init; } = "None";
    public string Category { get; init; } = "General";
    public double Intensity { get; init; } = 1.0;
    public double Weight { get; init; } = 1.0;
}
