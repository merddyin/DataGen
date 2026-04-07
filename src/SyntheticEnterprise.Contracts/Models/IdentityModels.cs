namespace SyntheticEnterprise.Contracts.Models;

public record DirectoryOrganizationalUnit
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string Name { get; init; } = "";
    public string DistinguishedName { get; init; } = "";
    public string? ParentOuId { get; init; }
    public string Purpose { get; init; } = "";
}

public record DirectoryAccount
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string? PersonId { get; init; }
    public string AccountType { get; init; } = "User";
    public string SamAccountName { get; init; } = "";
    public string UserPrincipalName { get; init; } = "";
    public string? Mail { get; init; }
    public string DistinguishedName { get; init; } = "";
    public string OuId { get; init; } = "";
    public bool Enabled { get; init; } = true;
    public bool Privileged { get; init; }
    public bool MfaEnabled { get; init; } = true;
    public string? EmployeeId { get; init; }
    public string? ManagerAccountId { get; init; }
}

public record DirectoryGroup
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string Name { get; init; } = "";
    public string GroupType { get; init; } = "Security";
    public string Scope { get; init; } = "Global";
    public bool MailEnabled { get; init; }
    public string DistinguishedName { get; init; } = "";
    public string OuId { get; init; } = "";
    public string Purpose { get; init; } = "";
}

public record DirectoryGroupMembership
{
    public string Id { get; init; } = "";
    public string GroupId { get; init; } = "";
    public string MemberObjectId { get; init; } = "";
    public string MemberObjectType { get; init; } = "Account";
}

public record IdentityAnomaly
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string Category { get; init; } = "";
    public string Severity { get; init; } = "Medium";
    public string AffectedObjectId { get; init; } = "";
    public string Description { get; init; } = "";
}
