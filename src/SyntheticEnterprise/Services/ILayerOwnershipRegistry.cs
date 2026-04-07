using System.Collections.Generic;
using SyntheticEnterprise.Contracts;

namespace SyntheticEnterprise.Services;

public interface ILayerOwnershipRegistry
{
    IReadOnlyList<OwnedArtifactDescriptor> GetOwnedArtifacts(string layerName);
}
