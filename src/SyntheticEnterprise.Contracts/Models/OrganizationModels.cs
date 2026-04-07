namespace SyntheticEnterprise.Contracts.Models;

public record Company
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Industry { get; init; } = "";
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
}

public class SyntheticEnterpriseWorld
{
    public List<Company> Companies { get; } = new();
    public List<BusinessUnit> BusinessUnits { get; } = new();
    public List<Department> Departments { get; } = new();
    public List<Team> Teams { get; } = new();
    public List<Person> People { get; } = new();
    public List<Office> Offices { get; } = new();

    public List<DirectoryOrganizationalUnit> OrganizationalUnits { get; } = new();
    public List<DirectoryAccount> Accounts { get; } = new();
    public List<DirectoryGroup> Groups { get; } = new();
    public List<DirectoryGroupMembership> GroupMemberships { get; } = new();
    public List<IdentityAnomaly> IdentityAnomalies { get; } = new();

    public List<object> Applications { get; } = new();
    public List<ManagedDevice> Devices { get; } = new();
    public List<ServerAsset> Servers { get; } = new();
    public List<NetworkAsset> NetworkAssets { get; } = new();
    public List<TelephonyAsset> TelephonyAssets { get; } = new();
    public List<SoftwarePackage> SoftwarePackages { get; } = new();
    public List<DeviceSoftwareInstallation> DeviceSoftwareInstallations { get; } = new();
    public List<ServerSoftwareInstallation> ServerSoftwareInstallations { get; } = new();
    public List<InfrastructureAnomaly> InfrastructureAnomalies { get; } = new();

    public List<DatabaseRepository> Databases { get; } = new();
    public List<FileShareRepository> FileShares { get; } = new();
    public List<CollaborationSite> CollaborationSites { get; } = new();
    public List<RepositoryAccessGrant> RepositoryAccessGrants { get; } = new();
    public List<RepositoryAnomaly> RepositoryAnomalies { get; } = new();
}
