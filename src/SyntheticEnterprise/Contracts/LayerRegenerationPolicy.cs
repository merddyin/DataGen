namespace SyntheticEnterprise.Contracts;

public sealed class LayerRegenerationPolicy
{
    public required string LayerName { get; init; }
    public required string RegenerationMode { get; init; }
    public bool PreserveStableIdentifiersWhenPossible { get; init; } = true;
    public bool AttemptDownstreamReferenceRepair { get; init; } = true;
    public bool WarnOnUnrepairedReferences { get; init; } = true;
    public string DefaultMergeConflictResolution { get; init; } = "PreserveExisting";
}
