using System;
using System.Collections.Generic;
using SyntheticEnterprise.Contracts;

namespace SyntheticEnterprise.Services;

public sealed class DefaultLayerOwnershipRegistry : ILayerOwnershipRegistry
{
    public IReadOnlyList<OwnedArtifactDescriptor> GetOwnedArtifacts(string layerName)
    {
        return layerName switch
        {
            "Identity" => new List<OwnedArtifactDescriptor>
            {
                new() { LayerName = "Identity", EntityType = "DirectoryAccount", CollectionPath = "World.DirectoryAccounts", SupportsStableRemap = true },
                new() { LayerName = "Identity", EntityType = "DirectoryGroup", CollectionPath = "World.DirectoryGroups", SupportsStableRemap = true },
                new() { LayerName = "Identity", EntityType = "DirectoryGroupMembership", CollectionPath = "World.DirectoryGroupMemberships", SupportsStableRemap = false },
            },
            "Infrastructure" => new List<OwnedArtifactDescriptor>
            {
                new() { LayerName = "Infrastructure", EntityType = "ManagedDevice", CollectionPath = "World.ManagedDevices", SupportsStableRemap = true },
                new() { LayerName = "Infrastructure", EntityType = "ServerAsset", CollectionPath = "World.Servers", SupportsStableRemap = true },
            },
            "Repository" => new List<OwnedArtifactDescriptor>
            {
                new() { LayerName = "Repository", EntityType = "DatabaseRepository", CollectionPath = "World.DatabaseRepositories", SupportsStableRemap = true },
                new() { LayerName = "Repository", EntityType = "RepositoryAccessGrant", CollectionPath = "World.RepositoryAccessGrants", SupportsStableRemap = false },
            },
            _ => Array.Empty<OwnedArtifactDescriptor>(),
        };
    }
}
