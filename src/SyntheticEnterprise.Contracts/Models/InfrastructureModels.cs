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
}

public record TelephonyAsset
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string AssetType { get; init; } = "DeskPhone";
    public string Identifier { get; init; } = "";
    public string? AssignedPersonId { get; init; }
    public string? AssignedOfficeId { get; init; }
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

public record InfrastructureAnomaly
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string Category { get; init; } = "Infrastructure";
    public string Severity { get; init; } = "Medium";
    public string AffectedObjectId { get; init; } = "";
    public string Description { get; init; } = "";
}
