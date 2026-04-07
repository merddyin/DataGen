namespace SyntheticEnterprise.Contracts;

public sealed class OwnedArtifactDescriptor
{
    public required string LayerName { get; init; }
    public required string EntityType { get; init; }
    public string? CollectionPath { get; init; }
    public bool SupportsStableRemap { get; init; }
}
