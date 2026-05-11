namespace SyntheticEnterprise.Contracts.Models;

public record SoftwarePackage
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Category { get; init; } = "";
    public string Vendor { get; init; } = "";
    public string Version { get; init; } = "";
}

public record ManagedDevice
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string DeviceType { get; init; } = "Workstation";
    public string Hostname { get; init; } = "";
    public string AssetTag { get; init; } = "";
    public string SerialNumber { get; init; } = "";
    public string Manufacturer { get; init; } = "";
    public string Model { get; init; } = "";
    public string OperatingSystem { get; init; } = "";
    public string OperatingSystemVersion { get; init; } = "";
    public string? AssignedPersonId { get; init; }
    public string? AssignedOfficeId { get; init; }
    public string? DirectoryAccountId { get; init; }
    public string? OnPremDirectoryAccountId { get; init; }
    public string? CloudDirectoryAccountId { get; init; }
    public string? OuId { get; init; }
    public string? DistinguishedName { get; init; }
    public string? ActiveDirectorySiteId { get; init; }
    public string? NetworkSubnetId { get; init; }
    public string? IpAddress { get; init; }
    public bool DomainJoined { get; init; } = true;
    public string ComplianceState { get; init; } = "Compliant";
    public DateTimeOffset LastSeen { get; init; } = DateTimeOffset.UtcNow;
}

public record ServerAsset
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string Hostname { get; init; } = "";
    public string ServerRole { get; init; } = "";
    public string Environment { get; init; } = "Production";
    public string OperatingSystem { get; init; } = "";
    public string OperatingSystemVersion { get; init; } = "";
    public string OfficeId { get; init; } = "";
    public string? DirectoryAccountId { get; init; }
    public string? OnPremDirectoryAccountId { get; init; }
    public string? CloudDirectoryAccountId { get; init; }
    public string? OuId { get; init; }
    public string? DistinguishedName { get; init; }
    public string HostingLocationType { get; init; } = "OnPremises";
    public string? CloudProvider { get; init; }
    public string? CloudRegion { get; init; }
    public string? ActiveDirectorySiteId { get; init; }
    public string? NetworkSubnetId { get; init; }
    public string? IpAddress { get; init; }
    public bool DomainJoined { get; init; } = true;
    public string OwnerTeamId { get; init; } = "";
    public string Criticality { get; init; } = "Medium";
}

public record NetworkAsset
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string AssetType { get; init; } = "Switch";
    public string Hostname { get; init; } = "";
    public string OfficeId { get; init; } = "";
    public string Vendor { get; init; } = "";
    public string Model { get; init; } = "";
    public string? ActiveDirectorySiteId { get; init; }
    public string? NetworkSubnetId { get; init; }
    public string? IpAddress { get; init; }
}

public record TelephonyAsset
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string AssetType { get; init; } = "DeskPhone";
    public string Identifier { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string? PhoneNumber { get; init; }
    public string? Extension { get; init; }
    public string Vendor { get; init; } = "";
    public string Model { get; init; } = "";
    public string? AssignedPersonId { get; init; }
    public string? AssignedOfficeId { get; init; }
    public string? ActiveDirectorySiteId { get; init; }
    public string? NetworkSubnetId { get; init; }
    public string? IpAddress { get; init; }
}

public record ActiveDirectorySite
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string IdentityStoreId { get; init; } = "";
    public string Name { get; init; } = "";
    public string SiteType { get; init; } = "PhysicalOffice";
    public string SiteRole { get; init; } = "Spoke";
    public string? OfficeId { get; init; }
    public string Region { get; init; } = "";
    public string Country { get; init; } = "";
    public string City { get; init; } = "";
    public string? CloudProvider { get; init; }
    public string? CloudRegion { get; init; }
    public bool IsPrimaryHub { get; init; }
}

public record ActiveDirectorySiteLink
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string IdentityStoreId { get; init; } = "";
    public string Name { get; init; } = "";
    public string TopologyStyle { get; init; } = "FullMesh";
    public string Transport { get; init; } = "IP";
    public int Cost { get; init; } = 100;
    public int ReplicationIntervalMinutes { get; init; } = 180;
}

public record ActiveDirectorySiteLinkMembership
{
    public string Id { get; init; } = "";
    public string SiteLinkId { get; init; } = "";
    public string SiteId { get; init; } = "";
    public int MemberOrder { get; init; }
}

public record NetworkSubnet
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string IdentityStoreId { get; init; } = "";
    public string ActiveDirectorySiteId { get; init; } = "";
    public string Name { get; init; } = "";
    public string AddressCidr { get; init; } = "";
    public string GatewayAddress { get; init; } = "";
    public string UsableStartAddress { get; init; } = "";
    public string UsableEndAddress { get; init; } = "";
    public string SubnetType { get; init; } = "Workstation";
    public string LocationType { get; init; } = "Office";
    public string? OfficeId { get; init; }
    public string Region { get; init; } = "";
    public string Country { get; init; } = "";
    public string City { get; init; } = "";
    public string? CloudProvider { get; init; }
    public string? CloudRegion { get; init; }
    public string? BuildingLabel { get; init; }
    public string? FloorLabel { get; init; }
    public string? SegmentLabel { get; init; }
    public string? VlanId { get; init; }
    public bool IsDhcpScope { get; init; } = true;
}

public record DeviceSoftwareInstallation
{
    public string Id { get; init; } = "";
    public string DeviceId { get; init; } = "";
    public string SoftwareId { get; init; } = "";
}

public record ServerSoftwareInstallation
{
    public string Id { get; init; } = "";
    public string ServerId { get; init; } = "";
    public string SoftwareId { get; init; } = "";
}

public record EndpointAdministrativeAssignment
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string EndpointType { get; init; } = "";
    public string EndpointId { get; init; } = "";
    public string PrincipalObjectId { get; init; } = "";
    public string PrincipalType { get; init; } = "Group";
    public string AccessRole { get; init; } = "LocalAdministrator";
    public string AdministrativeTier { get; init; } = "";
    public string AssignmentScope { get; init; } = "Persistent";
    public string ManagementPlane { get; init; } = "DirectoryPolicy";
}

public record EndpointPolicyBaseline
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string EndpointType { get; init; } = "";
    public string EndpointId { get; init; } = "";
    public string PolicyName { get; init; } = "";
    public string PolicyCategory { get; init; } = "";
    public string AssignedFrom { get; init; } = "";
    public string EnforcementMode { get; init; } = "Enforced";
    public string DesiredState { get; init; } = "";
    public string CurrentState { get; init; } = "";
    public string AdministrativeTier { get; init; } = "";
}

public record EndpointLocalGroupMember
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string EndpointType { get; init; } = "";
    public string EndpointId { get; init; } = "";
    public string LocalGroupName { get; init; } = "";
    public string? PrincipalObjectId { get; init; }
    public string PrincipalType { get; init; } = "Group";
    public string PrincipalName { get; init; } = "";
    public string MembershipSource { get; init; } = "Policy";
    public string AdministrativeTier { get; init; } = "";
}

public record InfrastructureAnomaly
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string Category { get; init; } = "Infrastructure";
    public string Severity { get; init; } = "Medium";
    public string AffectedObjectId { get; init; } = "";
    public string Description { get; init; } = "";
}
