namespace SyntheticEnterprise.Contracts.Models;

[System.Text.Json.Serialization.JsonObjectCreationHandling(System.Text.Json.Serialization.JsonObjectCreationHandling.Populate)]
public class SyntheticEnterpriseWorld
{
    public List<Company> Companies { get; } = new();
    public List<BusinessUnit> BusinessUnits { get; } = new();
    public List<Department> Departments { get; } = new();
    public List<Team> Teams { get; } = new();
    public List<BusinessProcess> BusinessProcesses { get; } = new();
    public List<Person> People { get; } = new();
    public List<Office> Offices { get; } = new();
    public List<ExternalOrganization> ExternalOrganizations { get; } = new();
    public List<CrossTenantAccessPolicyRecord> CrossTenantAccessPolicies { get; } = new();
    public List<CrossTenantAccessEvent> CrossTenantAccessEvents { get; } = new();
    public List<ObservedEntitySnapshot> ObservedEntitySnapshots { get; } = new();

    public List<EnvironmentContainer> Containers { get; } = new();
    public List<PolicyRecord> Policies { get; } = new();
    public List<PolicySettingRecord> PolicySettings { get; } = new();
    public List<PolicyTargetLink> PolicyTargetLinks { get; } = new();
    public List<AccessControlEvidenceRecord> AccessControlEvidence { get; } = new();
    public List<ConfigurationItem> ConfigurationItems { get; } = new();
    public List<ConfigurationItemRelationship> ConfigurationItemRelationships { get; } = new();
    public List<CmdbSourceRecord> CmdbSourceRecords { get; } = new();
    public List<CmdbSourceLink> CmdbSourceLinks { get; } = new();
    public List<CmdbSourceRelationship> CmdbSourceRelationships { get; } = new();
    public List<DirectoryOrganizationalUnit> OrganizationalUnits { get; } = new();
    public List<IdentityStore> IdentityStores { get; } = new();
    public List<DirectoryAccount> Accounts { get; } = new();
    public List<DirectoryGroup> Groups { get; } = new();
    public List<DirectoryGroupMembership> GroupMemberships { get; } = new();
    public List<IdentityAnomaly> IdentityAnomalies { get; } = new();

    public List<ApplicationRecord> Applications { get; } = new();
    public List<ApplicationDependency> ApplicationDependencies { get; } = new();
    public List<ApplicationService> ApplicationServices { get; } = new();
    public List<ApplicationServiceDependency> ApplicationServiceDependencies { get; } = new();
    public List<ApplicationServiceHosting> ApplicationServiceHostings { get; } = new();
    public List<CloudTenant> CloudTenants { get; } = new();
    public List<ApplicationTenantLink> ApplicationTenantLinks { get; } = new();
    public List<ApplicationBusinessProcessLink> ApplicationBusinessProcessLinks { get; } = new();
    public List<ApplicationCounterpartyLink> ApplicationCounterpartyLinks { get; } = new();
    public List<BusinessProcessCounterpartyLink> BusinessProcessCounterpartyLinks { get; } = new();
    public List<ManagedDevice> Devices { get; } = new();
    public List<ServerAsset> Servers { get; } = new();
    public List<NetworkAsset> NetworkAssets { get; } = new();
    public List<TelephonyAsset> TelephonyAssets { get; } = new();
    public List<SoftwarePackage> SoftwarePackages { get; } = new();
    public List<DeviceSoftwareInstallation> DeviceSoftwareInstallations { get; } = new();
    public List<ServerSoftwareInstallation> ServerSoftwareInstallations { get; } = new();
    public List<EndpointAdministrativeAssignment> EndpointAdministrativeAssignments { get; } = new();
    public List<EndpointPolicyBaseline> EndpointPolicyBaselines { get; } = new();
    public List<EndpointLocalGroupMember> EndpointLocalGroupMembers { get; } = new();
    public List<InfrastructureAnomaly> InfrastructureAnomalies { get; } = new();

    public List<DatabaseRepository> Databases { get; } = new();
    public List<FileShareRepository> FileShares { get; } = new();
    public List<CollaborationSite> CollaborationSites { get; } = new();
    public List<CollaborationChannel> CollaborationChannels { get; } = new();
    public List<CollaborationChannelTab> CollaborationChannelTabs { get; } = new();
    public List<DocumentLibrary> DocumentLibraries { get; } = new();
    public List<SitePage> SitePages { get; } = new();
    public List<DocumentFolder> DocumentFolders { get; } = new();
    public List<ApplicationRepositoryLink> ApplicationRepositoryLinks { get; } = new();
    public List<RepositoryAccessGrant> RepositoryAccessGrants { get; } = new();
    public List<RepositoryAnomaly> RepositoryAnomalies { get; } = new();
    public List<PluginGeneratedRecord> PluginRecords { get; } = new();
}

public record ApplicationRecord
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string Name { get; init; } = "";
    public string Category { get; init; } = "";
    public string Vendor { get; init; } = "";
    public string BusinessCapability { get; init; } = "";
    public string HostingModel { get; init; } = "";
    public string ApplicationType { get; init; } = "Undefined";
    public string DeploymentType { get; init; } = "Undefined";
    public string Environment { get; init; } = "Production";
    public string Criticality { get; init; } = "Medium";
    public string DataSensitivity { get; init; } = "Internal";
    public string UserScope { get; init; } = "Enterprise";
    public string OwnerDepartmentId { get; init; } = "";
    public string? Url { get; init; }
    public bool SsoEnabled { get; init; }
    public bool MfaRequired { get; init; }
}

public record BusinessProcess
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string Name { get; init; } = "";
    public string Domain { get; init; } = "";
    public string BusinessCapability { get; init; } = "";
    public string OwnerDepartmentId { get; init; } = "";
    public string OperatingModel { get; init; } = "";
    public string ProcessScope { get; init; } = "";
    public string Criticality { get; init; } = "Medium";
    public bool CustomerFacing { get; init; }
}

public record ApplicationDependency
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string SourceApplicationId { get; init; } = "";
    public string TargetApplicationId { get; init; } = "";
    public string DependencyType { get; init; } = "";
    public string InterfaceType { get; init; } = "";
    public string Criticality { get; init; } = "Medium";
}

public record ApplicationService
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string ApplicationId { get; init; } = "";
    public string Name { get; init; } = "";
    public string ServiceType { get; init; } = "";
    public string Runtime { get; init; } = "";
    public string DeploymentModel { get; init; } = "";
    public string Environment { get; init; } = "Production";
    public string OwnerTeamId { get; init; } = "";
    public string Criticality { get; init; } = "Medium";
}

public record ApplicationServiceDependency
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string SourceServiceId { get; init; } = "";
    public string TargetServiceId { get; init; } = "";
    public string DependencyType { get; init; } = "";
    public string InterfaceType { get; init; } = "";
    public string Criticality { get; init; } = "Medium";
}

public record ApplicationServiceHosting
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string ApplicationServiceId { get; init; } = "";
    public string HostType { get; init; } = "";
    public string? HostId { get; init; }
    public string HostName { get; init; } = "";
    public string HostingRole { get; init; } = "";
    public string DeploymentModel { get; init; } = "";
}

public record CloudTenant
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string Provider { get; init; } = "";
    public string TenantType { get; init; } = "";
    public string Name { get; init; } = "";
    public string PrimaryDomain { get; init; } = "";
    public string Region { get; init; } = "";
    public string AuthenticationModel { get; init; } = "";
    public string Environment { get; init; } = "Production";
    public string AdminDepartmentId { get; init; } = "";
}

public record ApplicationTenantLink
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string ApplicationId { get; init; } = "";
    public string CloudTenantId { get; init; } = "";
    public string RelationshipType { get; init; } = "";
    public bool IsPrimary { get; init; } = true;
}

public record ApplicationBusinessProcessLink
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string ApplicationId { get; init; } = "";
    public string BusinessProcessId { get; init; } = "";
    public string RelationshipType { get; init; } = "";
    public bool IsPrimary { get; init; } = false;
}

public record ApplicationRepositoryLink
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string ApplicationId { get; init; } = "";
    public string RepositoryId { get; init; } = "";
    public string RepositoryType { get; init; } = "";
    public string RelationshipType { get; init; } = "";
    public string Criticality { get; init; } = "Medium";
}

public record PluginGeneratedRecord
{
    public string Id { get; init; } = "";
    public string PluginCapability { get; init; } = "";
    public string RecordType { get; init; } = "";
    public string? AssociatedEntityType { get; init; }
    public string? AssociatedEntityId { get; init; }
    public Dictionary<string, string?> Properties { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public string? JsonPayload { get; init; }
}
