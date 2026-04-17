namespace SyntheticEnterprise.Core.Services;

using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Core.Abstractions;

public sealed class DefaultLayerOwnershipRegistry : ILayerOwnershipRegistry
{
    private static readonly IReadOnlyList<OwnedArtifactDescriptor> Artifacts =
    [
        new()
        {
            LayerName = "Identity",
            EntityType = "EnvironmentContainer",
            CollectionPath = "World.Containers",
            OwnershipMode = "Shared",
            SupportsStableRemap = false,
            SupportsMergeReconciliation = false
        },
        new()
        {
            LayerName = "Identity",
            EntityType = "PolicyRecord",
            CollectionPath = "World.Policies",
            OwnershipMode = "Shared",
            SupportsStableRemap = false,
            SupportsMergeReconciliation = false
        },
        new()
        {
            LayerName = "Identity",
            EntityType = "PolicySettingRecord",
            CollectionPath = "World.PolicySettings",
            OwnershipMode = "Shared",
            SupportsStableRemap = false,
            SupportsMergeReconciliation = false
        },
        new()
        {
            LayerName = "Identity",
            EntityType = "PolicyTargetLink",
            CollectionPath = "World.PolicyTargetLinks",
            OwnershipMode = "Shared",
            SupportsStableRemap = false,
            SupportsMergeReconciliation = false
        },
        new()
        {
            LayerName = "Identity",
            EntityType = "AccessControlEvidenceRecord",
            CollectionPath = "World.AccessControlEvidence",
            OwnershipMode = "Shared",
            SupportsStableRemap = false,
            SupportsMergeReconciliation = false
        },
        new()
        {
            LayerName = "Identity",
            EntityType = "IdentityStore",
            CollectionPath = "World.IdentityStores",
            OwnershipMode = "Shared",
            SupportsStableRemap = false,
            SupportsMergeReconciliation = false
        },
        new()
        {
            LayerName = "ConfigurationManagement",
            EntityType = "ConfigurationItem",
            CollectionPath = "World.ConfigurationItems",
            OwnershipMode = "Shared",
            SupportsStableRemap = false,
            SupportsMergeReconciliation = false
        },
        new()
        {
            LayerName = "ConfigurationManagement",
            EntityType = "ConfigurationItemRelationship",
            CollectionPath = "World.ConfigurationItemRelationships",
            OwnershipMode = "Shared",
            SupportsStableRemap = false,
            SupportsMergeReconciliation = false
        },
        new()
        {
            LayerName = "ConfigurationManagement",
            EntityType = "CmdbSourceRecord",
            CollectionPath = "World.CmdbSourceRecords",
            OwnershipMode = "Shared",
            SupportsStableRemap = false,
            SupportsMergeReconciliation = false
        },
        new()
        {
            LayerName = "ConfigurationManagement",
            EntityType = "CmdbSourceLink",
            CollectionPath = "World.CmdbSourceLinks",
            OwnershipMode = "Shared",
            SupportsStableRemap = false,
            SupportsMergeReconciliation = false
        },
        new()
        {
            LayerName = "ConfigurationManagement",
            EntityType = "CmdbSourceRelationship",
            CollectionPath = "World.CmdbSourceRelationships",
            OwnershipMode = "Shared",
            SupportsStableRemap = false,
            SupportsMergeReconciliation = false
        },
        new()
        {
            LayerName = "Identity",
            EntityType = "DirectoryAccount",
            CollectionPath = "World.Accounts",
            SupportsStableRemap = true,
            SupportsMergeReconciliation = true
        },
        new()
        {
            LayerName = "Identity",
            EntityType = "DirectoryGroup",
            CollectionPath = "World.Groups",
            SupportsStableRemap = true,
            SupportsMergeReconciliation = true
        },
        new()
        {
            LayerName = "Identity",
            EntityType = "DirectoryOrganizationalUnit",
            CollectionPath = "World.OrganizationalUnits",
            SupportsStableRemap = true,
            SupportsMergeReconciliation = true
        },
        new()
        {
            LayerName = "Identity",
            EntityType = "DirectoryGroupMembership",
            CollectionPath = "World.GroupMemberships",
            SelectionPredicate = "MemberObjectType in {Account,Group}",
            SupportsStableRemap = true,
            SupportsMergeReconciliation = true
        },
        new()
        {
            LayerName = "Identity",
            EntityType = "CrossTenantAccessPolicyRecord",
            CollectionPath = "World.CrossTenantAccessPolicies",
            SupportsStableRemap = false,
            SupportsMergeReconciliation = true
        },
        new()
        {
            LayerName = "Identity",
            EntityType = "CrossTenantAccessEvent",
            CollectionPath = "World.CrossTenantAccessEvents",
            SupportsStableRemap = true,
            SupportsMergeReconciliation = true
        },
        new()
        {
            LayerName = "Identity",
            EntityType = "ExternalOrganization",
            CollectionPath = "World.ExternalOrganizations",
            OwnershipMode = "Shared",
            SelectionPredicate = "RelationshipType in {StaffingPartner,ManagedServiceProvider,Partner}",
            SupportsStableRemap = false,
            SupportsMergeReconciliation = true
        },
        new()
        {
            LayerName = "Infrastructure",
            EntityType = "ManagedDevice",
            CollectionPath = "World.Devices",
            SupportsStableRemap = true,
            SupportsMergeReconciliation = true
        },
        new()
        {
            LayerName = "Infrastructure",
            EntityType = "ServerAsset",
            CollectionPath = "World.Servers",
            SupportsStableRemap = true,
            SupportsMergeReconciliation = true
        },
        new()
        {
            LayerName = "Infrastructure",
            EntityType = "SoftwarePackage",
            CollectionPath = "World.SoftwarePackages",
            SupportsStableRemap = false,
            SupportsMergeReconciliation = true
        },
        new()
        {
            LayerName = "Infrastructure",
            EntityType = "EndpointAdministrativeAssignment",
            CollectionPath = "World.EndpointAdministrativeAssignments",
            SupportsStableRemap = true,
            SupportsMergeReconciliation = true
        },
        new()
        {
            LayerName = "Infrastructure",
            EntityType = "EndpointPolicyBaseline",
            CollectionPath = "World.EndpointPolicyBaselines",
            SupportsStableRemap = true,
            SupportsMergeReconciliation = true
        },
        new()
        {
            LayerName = "Repository",
            EntityType = "DatabaseRepository",
            CollectionPath = "World.Databases",
            SupportsStableRemap = true,
            SupportsMergeReconciliation = true
        },
        new()
        {
            LayerName = "Repository",
            EntityType = "FileShareRepository",
            CollectionPath = "World.FileShares",
            SupportsStableRemap = true,
            SupportsMergeReconciliation = true
        },
        new()
        {
            LayerName = "Repository",
            EntityType = "CollaborationSite",
            CollectionPath = "World.CollaborationSites",
            SupportsStableRemap = true,
            SupportsMergeReconciliation = true
        },
        new()
        {
            LayerName = "Applications",
            EntityType = "ApplicationRecord",
            CollectionPath = "World.Applications",
            SupportsStableRemap = false,
            SupportsMergeReconciliation = true
        },
        new()
        {
            LayerName = "Applications",
            EntityType = "ApplicationService",
            CollectionPath = "World.ApplicationServices",
            SupportsStableRemap = false,
            SupportsMergeReconciliation = true
        },
        new()
        {
            LayerName = "Applications",
            EntityType = "CloudTenant",
            CollectionPath = "World.CloudTenants",
            OwnershipMode = "Shared",
            SupportsStableRemap = false,
            SupportsMergeReconciliation = true
        },
        new()
        {
            LayerName = "BusinessProcesses",
            EntityType = "BusinessProcess",
            CollectionPath = "World.BusinessProcesses",
            SupportsStableRemap = false,
            SupportsMergeReconciliation = true
        },
        new()
        {
            LayerName = "ExternalEcosystem",
            EntityType = "ExternalOrganization",
            CollectionPath = "World.ExternalOrganizations",
            OwnershipMode = "Shared",
            SelectionPredicate = "RelationshipType in {Vendor,Customer,Partner}",
            SupportsStableRemap = false,
            SupportsMergeReconciliation = true
        }
    ];

    public IReadOnlyList<OwnedArtifactDescriptor> GetOwnedArtifacts()
        => Artifacts;

    public IReadOnlyList<OwnedArtifactDescriptor> GetOwnedArtifacts(string layerName)
        => Artifacts
            .Where(artifact => string.Equals(artifact.LayerName, layerName, StringComparison.OrdinalIgnoreCase))
            .ToList();
}
