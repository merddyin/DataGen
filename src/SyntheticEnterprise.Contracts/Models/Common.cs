namespace SyntheticEnterprise.Contracts.Models;

public abstract record EntityBase
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public Dictionary<string, string?> Attributes { get; init; } = new();
}

public enum EmploymentType
{
    Employee,
    Contractor,
    Intern,
    External
}

public enum AccountType
{
    UserInternal,
    UserExternal,
    Service,
    Shared,
    Privileged,
    Emergency
}

public enum GroupType
{
    Security,
    Distribution,
    Dynamic,
    Microsoft365,
    Application,
    RoleBased
}

public enum DeviceType
{
    Laptop,
    Workstation,
    VirtualDesktop,
    Mobile,
    Server
}

public enum ApplicationHostingModel
{
    SaaS,
    IaaS,
    OnPrem
}
