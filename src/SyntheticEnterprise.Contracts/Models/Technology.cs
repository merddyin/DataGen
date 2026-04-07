namespace SyntheticEnterprise.Contracts.Models;

public record Application : EntityBase
{
    public required string CompanyId { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required ApplicationHostingModel HostingModel { get; init; }
    public required string OwnerDepartmentId { get; init; }
    public string? Url { get; init; }
    public bool SsoEnabled { get; init; }
    public bool MfaRequired { get; init; }
}

public record Device : EntityBase
{
    public required string CompanyId { get; init; }
    public required DeviceType DeviceType { get; init; }
    public required string Hostname { get; init; }
    public string? AssignedPersonId { get; init; }
    public string? OfficeId { get; init; }
    public string? OperatingSystem { get; init; }
    public DateTimeOffset? LastSeen { get; init; }
}

public record InstalledSoftware : EntityBase
{
    public required string DeviceOrServerId { get; init; }
    public required string Name { get; init; }
    public string? Version { get; init; }
    public string? Vendor { get; init; }
}

public record Repository : EntityBase
{
    public required string CompanyId { get; init; }
    public required string RepositoryKind { get; init; }
    public required string Name { get; init; }
    public string? AssociatedApplicationId { get; init; }
    public decimal? SizeGb { get; init; }
}

public record AccessAssignment : EntityBase
{
    public required string ResourceId { get; init; }
    public required string PrincipalId { get; init; }
    public required string PrincipalType { get; init; }
    public required string AccessLevel { get; init; }
}
