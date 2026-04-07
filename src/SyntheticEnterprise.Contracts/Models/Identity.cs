namespace SyntheticEnterprise.Contracts.Models;

public record DirectoryOu : EntityBase
{
    public required string CompanyId { get; init; }
    public required string DistinguishedName { get; init; }
    public string? ParentOuId { get; init; }
}

public record DirectoryAccount : EntityBase
{
    public required string CompanyId { get; init; }
    public string? PersonId { get; init; }
    public required AccountType AccountType { get; init; }
    public required string SamAccountName { get; init; }
    public required string UserPrincipalName { get; init; }
    public string? Mail { get; init; }
    public required bool Enabled { get; init; }
    public required string OuId { get; init; }
    public bool MfaEnabled { get; init; }
}

public record DirectoryGroup : EntityBase
{
    public required string CompanyId { get; init; }
    public required GroupType GroupType { get; init; }
    public required string Name { get; init; }
    public bool MailEnabled { get; init; }
    public string? OwnerAccountId { get; init; }
}

public record GroupMembership : EntityBase
{
    public required string GroupId { get; init; }
    public required string MemberObjectId { get; init; }
    public required string MemberObjectType { get; init; }
}
