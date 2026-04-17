namespace SyntheticEnterprise.Contracts.Models;

public record Company
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Industry { get; init; } = "";
    public string LegalName { get; init; } = "";
    public string PrimaryCountry { get; init; } = "";
    public string PrimaryDomain { get; init; } = "";
    public string Website { get; init; } = "";
    public string Tagline { get; init; } = "";
    public string? HeadquartersOfficeId { get; init; }
    public string? PrimaryPhoneNumber { get; init; }
}

public record BusinessUnit
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string Name { get; init; } = "";
}

public record Department
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string BusinessUnitId { get; init; } = "";
    public string Name { get; init; } = "";
}

public record Team
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string DepartmentId { get; init; } = "";
    public string Name { get; init; } = "";
}

public record Person
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string TeamId { get; init; } = "";
    public string DepartmentId { get; init; } = "";
    public string FirstName { get; init; } = "";
    public string LastName { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Title { get; init; } = "";
    public string? ManagerPersonId { get; init; }
    public string EmployeeId { get; init; } = "";
    public string Country { get; init; } = "";
    public string? OfficeId { get; init; }
    public string UserPrincipalName { get; init; } = "";
    public string EmploymentType { get; init; } = "Employee";
    public string PersonType { get; init; } = "Internal";
    public string? EmployerOrganizationId { get; init; }
    public string? SponsorPersonId { get; init; }
}
