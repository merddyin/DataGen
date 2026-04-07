namespace SyntheticEnterprise.Contracts.Abstractions;

using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Models;

public record GenerationContext
{
    public required ScenarioDefinition Scenario { get; init; }
    public int? Seed { get; init; }
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
    public Dictionary<string, string?> Metadata { get; init; } = new();
}

public record CatalogSet
{
    public Dictionary<string, IReadOnlyList<Dictionary<string, string?>>> CsvCatalogs { get; init; } = new();
    public Dictionary<string, object> JsonCatalogs { get; init; } = new();
}

public record GenerationStatistics
{
    public int CompanyCount { get; init; }
    public int OfficeCount { get; init; }
    public int PersonCount { get; init; }
    public int AccountCount { get; init; }
    public int GroupCount { get; init; }
    public int ApplicationCount { get; init; }
    public int DeviceCount { get; init; }
    public int RepositoryCount { get; init; }
}

public record WorldMetadata
{
    public required ScenarioDefinition Scenario { get; init; }
    public int? Seed { get; init; }
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
    public string? CatalogRootPath { get; init; }
    public HashSet<string> CatalogKeys { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> AppliedLayers { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> AppliedAnomalyProfiles { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public record GenerationResult
{
    public required SyntheticEnterpriseWorld World { get; init; }
    public required GenerationStatistics Statistics { get; init; }
    public CatalogSet Catalogs { get; init; } = new();
    public WorldMetadata? WorldMetadata { get; init; }
    public List<string> Warnings { get; init; } = new();
}
