namespace SyntheticEnterprise.Contracts.Models;

public record Company : EntityBase
{
    public required string LegalName { get; init; }
    public required string DisplayName { get; init; }
    public required string Industry { get; init; }
    public required string Domain { get; init; }
    public required string RegionProfile { get; init; }
}

public record Office : EntityBase
{
    public required string CompanyId { get; init; }
    public required string Country { get; init; }
    public required string Region { get; init; }
    public required string City { get; init; }
    public string? StateOrProvince { get; init; }
    public string? PostalCode { get; init; }
    public string? AddressLine1 { get; init; }
    public string? TimeZone { get; init; }
}

public record BusinessUnit : EntityBase
{
    public required string CompanyId { get; init; }
    public required string Name { get; init; }
}

public record Department : EntityBase
{
    public required string CompanyId { get; init; }
    public required string BusinessUnitId { get; init; }
    public required string Name { get; init; }
}

public record Team : EntityBase
{
    public required string CompanyId { get; init; }
    public required string DepartmentId { get; init; }
    public required string Name { get; init; }
}

public record Person : EntityBase
{
    public required string CompanyId { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string DisplayName { get; init; }
    public string? PreferredName { get; init; }
    public required string Title { get; init; }
    public required string EmployeeId { get; init; }
    public required string DepartmentId { get; init; }
    public required string TeamId { get; init; }
    public required string OfficeId { get; init; }
    public string? ManagerPersonId { get; init; }
    public required EmploymentType EmploymentType { get; init; }
    public required string Email { get; init; }
    public required string UserName { get; init; }
}
