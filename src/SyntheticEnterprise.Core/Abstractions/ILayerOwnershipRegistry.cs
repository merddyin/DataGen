namespace SyntheticEnterprise.Core.Abstractions;

using SyntheticEnterprise.Contracts.Abstractions;

public interface ILayerOwnershipRegistry
{
    IReadOnlyList<OwnedArtifactDescriptor> GetOwnedArtifacts();
    IReadOnlyList<OwnedArtifactDescriptor> GetOwnedArtifacts(string layerName);
}
