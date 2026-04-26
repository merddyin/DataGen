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
    public int ContainerCount { get; init; }
    public int OrganizationalUnitCount { get; init; }
    public int PolicyCount { get; init; }
    public int PolicySettingCount { get; init; }
    public int PolicyTargetLinkCount { get; init; }
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
    public WorldQualityReport Quality { get; init; } = new();
    public TemporalSimulationResult Temporal { get; init; } = new();
    public CatalogSet Catalogs { get; init; } = new();
    public WorldMetadata? WorldMetadata { get; init; }
    public List<string> Warnings { get; init; } = new();
}

public record WorldQualityReport
{
    public decimal OverallScore { get; init; } = 100m;
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Heuristics { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, int> Metrics { get; init; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Samples { get; init; } = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
    public QualityDimensionReport Realism { get; init; } = new() { Name = "Realism" };
    public QualityDimensionReport Completeness { get; init; } = new() { Name = "Completeness" };
    public QualityDimensionReport Consistency { get; init; } = new() { Name = "Consistency" };
    public QualityDimensionReport Exportability { get; init; } = new() { Name = "Exportability" };
    public QualityDimensionReport Operational { get; init; } = new() { Name = "Operational" };
}

public record WorldQualityValidationScenarioResult
{
    public string ScenarioPath { get; init; } = string.Empty;
    public int Seed { get; init; }
    public string Status { get; init; } = "pass";
    public decimal OverallScore { get; init; }
    public IReadOnlyDictionary<string, decimal> DimensionScores { get; init; } = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, int> BlockingMetrics { get; init; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, int> AdvisoryMetrics { get; init; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<string> Messages { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public record WorldQualityValidationSummary
{
    public string Status { get; init; } = "pass";
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
    public int ScenarioCount { get; init; }
    public int PassCount { get; init; }
    public int WarnCount { get; init; }
    public int FailCount { get; init; }
    public IReadOnlyList<WorldQualityValidationScenarioResult> Scenarios { get; init; } = Array.Empty<WorldQualityValidationScenarioResult>();
}

public record QualityDimensionReport
{
    public string Name { get; init; } = string.Empty;
    public decimal Score { get; init; } = 100m;
    public decimal MaxScore { get; init; } = 100m;
    public int FindingCount { get; init; }
    public bool Passed => Score >= 80m;
    public IReadOnlyList<string> MetricKeys { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<QualityScoreInput> Inputs { get; init; } = Array.Empty<QualityScoreInput>();
}

public record QualityScoreInput
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public decimal ObservedValue { get; init; }
    public decimal TargetValue { get; init; }
    public decimal Weight { get; init; }
    public decimal Penalty { get; init; }
    public bool Passing { get; init; } = true;
    public string Unit { get; init; } = "count";
    public string Heuristic { get; init; } = string.Empty;
}

public record TemporalSimulationResult
{
    public TimelineProfile Timeline { get; init; } = new();
    public List<TemporalEventRecord> Events { get; init; } = new();
    public List<TemporalSnapshotDescriptor> Snapshots { get; init; } = new();
}

public record TemporalEventRecord
{
    public string Id { get; init; } = string.Empty;
    public DateTimeOffset OccurredAt { get; init; }
    public string EventType { get; init; } = string.Empty;
    public string EntityType { get; init; } = string.Empty;
    public string EntityId { get; init; } = string.Empty;
    public string? RelatedEntityType { get; init; }
    public string? RelatedEntityId { get; init; }
    public Dictionary<string, string?> Properties { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public record TemporalSnapshotDescriptor
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public DateTimeOffset SnapshotAt { get; init; }
    public string SnapshotMode { get; init; } = "AsOfDate";
    public int EventCountThroughSnapshot { get; init; }
}
