namespace SyntheticEnterprise.Contracts.Configuration;

public record ScenarioDefinition
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required int CompanyCount { get; init; }
    public required string IndustryProfile { get; init; }
    public required string GeographyProfile { get; init; }
    public required SizeBand EmployeeSize { get; init; }
    public IdentityProfile Identity { get; init; } = new();
    public InfrastructureProfile Infrastructure { get; init; } = new();
    public RepositoryProfile Repositories { get; init; } = new();
    public List<AnomalyProfile> Anomalies { get; init; } = new();
}

public record SizeBand
{
    public required int Minimum { get; init; }
    public required int Maximum { get; init; }
}

public record IdentityProfile
{
    public bool IncludeHybridDirectory { get; init; } = true;
    public bool IncludeM365StyleGroups { get; init; } = true;
    public double StaleAccountRate { get; init; } = 0.03;
}

public record InfrastructureProfile
{
    public bool IncludeServers { get; init; } = true;
    public bool IncludeWorkstations { get; init; } = true;
    public bool IncludeNetworkAssets { get; init; } = false;
    public bool IncludeTelephony { get; init; } = false;
}

public record RepositoryProfile
{
    public bool IncludeDatabases { get; init; } = true;
    public bool IncludeFileShares { get; init; } = true;
    public bool IncludeCollaborationSites { get; init; } = true;
}

public record AnomalyProfile
{
    public required string Name { get; init; }
    public required double Weight { get; init; }
}
