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

public record IdentityStore
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string Name { get; init; } = "";
    public string StoreType { get; init; } = "";
    public string Provider { get; init; } = "";
    public string PrimaryDomain { get; init; } = "";
    public string? NamingContext { get; init; }
    public string DirectoryMode { get; init; } = "";
    public string AuthenticationModel { get; init; } = "";
    public string Environment { get; init; } = "Production";
    public bool IsPrimary { get; init; } = true;
    public string? CloudTenantId { get; init; }
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
    public string? GeneratedPassword { get; init; }
    public string PasswordProfile { get; init; } = "Standard";
    public string? AdministrativeTier { get; init; }
    public DateTimeOffset? PasswordLastSet { get; init; }
    public DateTimeOffset? PasswordExpires { get; init; }
    public bool PasswordNeverExpires { get; init; }
    public bool MustChangePasswordAtNextLogon { get; init; }
    public string UserType { get; init; } = "Member";
    public string IdentityProvider { get; init; } = "HybridDirectory";
    public string? InvitedOrganizationId { get; init; }
    public string? InvitedByAccountId { get; init; }
    public string? HomeTenantDomain { get; init; }
    public string? ResourceTenantDomain { get; init; }
    public string? InvitationStatus { get; init; }
    public DateTimeOffset? InvitationSentAt { get; init; }
    public DateTimeOffset? InvitationRedeemedAt { get; init; }
    public DateTimeOffset? AccessExpiresAt { get; init; }
    public string? GuestLifecycleState { get; init; }
    public string? CrossTenantAccessPolicy { get; init; }
    public string? ExternalAccessCategory { get; init; }
    public string? EntitlementPackageName { get; init; }
    public string? EntitlementAssignmentState { get; init; }
    public DateTimeOffset? LastAccessReviewAt { get; init; }
    public string? AccessReviewStatus { get; init; }
    public string? PreviousInvitedByAccountId { get; init; }
    public DateTimeOffset? SponsorLastChangedAt { get; init; }
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
    public string? AdministrativeTier { get; init; }
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
