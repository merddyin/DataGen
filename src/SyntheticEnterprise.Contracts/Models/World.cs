namespace SyntheticEnterprise.Contracts.Models;

public record SyntheticEnterpriseWorld
{
    public List<Company> Companies { get; init; } = new();
    public List<Office> Offices { get; init; } = new();
    public List<BusinessUnit> BusinessUnits { get; init; } = new();
    public List<Department> Departments { get; init; } = new();
    public List<Team> Teams { get; init; } = new();
    public List<Person> People { get; init; } = new();
    public List<DirectoryOu> Ous { get; init; } = new();
    public List<DirectoryAccount> Accounts { get; init; } = new();
    public List<DirectoryGroup> Groups { get; init; } = new();
    public List<GroupMembership> GroupMemberships { get; init; } = new();
    public List<Application> Applications { get; init; } = new();
    public List<Device> Devices { get; init; } = new();
    public List<InstalledSoftware> InstalledSoftware { get; init; } = new();
    public List<Repository> Repositories { get; init; } = new();
    public List<AccessAssignment> AccessAssignments { get; init; } = new();
}
