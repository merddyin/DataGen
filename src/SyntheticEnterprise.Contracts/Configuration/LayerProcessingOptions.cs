namespace SyntheticEnterprise.Contracts.Configuration;

public enum LayerRegenerationMode
{
    SkipIfPresent = 0,
    ReplaceLayer = 1,
    Merge = 2
}

public record LayerProcessingOptions
{
    public LayerRegenerationMode IdentityMode { get; init; } = LayerRegenerationMode.SkipIfPresent;
    public LayerRegenerationMode InfrastructureMode { get; init; } = LayerRegenerationMode.SkipIfPresent;
    public LayerRegenerationMode RepositoryMode { get; init; } = LayerRegenerationMode.SkipIfPresent;
    public bool ApplyAnomaliesIdempotently { get; init; } = true;
}
