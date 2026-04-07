namespace SyntheticEnterprise.Module.Services;

public sealed class CmdletServiceRegistry
{
    public object? WorldGenerator { get; init; }
    public object? LayerProcessor { get; init; }
    public object? ExportCoordinator { get; init; }
    public object? SnapshotPersistenceService { get; init; }
    public object? ScenarioValidator { get; init; }
    public object? ScenarioResolver { get; init; }
    public object? AnomalyNormalizationService { get; init; }
}
