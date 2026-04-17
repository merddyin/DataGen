namespace SyntheticEnterprise.Contracts.Abstractions;

using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Contracts.Plugins;

public record GenerationContext
{
    public required ScenarioDefinition Scenario { get; init; }
    public int? Seed { get; init; }
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
    public Dictionary<string, string?> Metadata { get; init; } = new();
    public ExternalPluginExecutionSettings ExternalPlugins { get; init; } = new();
}

public record CatalogSet
{
    public Dictionary<string, IReadOnlyList<Dictionary<string, string?>>> CsvCatalogs { get; init; } = new();
    public Dictionary<string, object> JsonCatalogs { get; init; } = new();
    public CatalogBuildMetadata? BuildMetadata { get; init; }
    public List<CatalogSourceMetadata> Sources { get; init; } = new();
}

public record CatalogBuildMetadata
{
    public string? BuiltAtUtc { get; init; }
    public string? Version { get; init; }
    public string? ManifestVersion { get; init; }
}

public record CatalogSourceMetadata
{
    public required string CatalogName { get; init; }
    public required string SourceFile { get; init; }
    public required string SourceRoot { get; init; }
    public required string SourceKind { get; init; }
    public string? Strategy { get; init; }
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

public record OwnedArtifactDescriptor
{
    public required string LayerName { get; init; }
    public required string EntityType { get; init; }
    public required string CollectionPath { get; init; }
    public string OwnershipMode { get; init; } = "Exclusive";
    public string? SelectionPredicate { get; init; }
    public bool SupportsStableRemap { get; init; }
    public bool SupportsMergeReconciliation { get; init; }
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
    public List<OwnedArtifactDescriptor> OwnedArtifacts { get; init; } = new();
}

public record GenerationResult
{
    public required SyntheticEnterpriseWorld World { get; init; }
    public required GenerationStatistics Statistics { get; init; }
    public CatalogSet Catalogs { get; init; } = new();
    public WorldMetadata? WorldMetadata { get; init; }
    public List<string> Warnings { get; init; } = new();
}
