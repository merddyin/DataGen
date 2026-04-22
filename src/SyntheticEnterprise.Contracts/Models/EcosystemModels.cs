namespace SyntheticEnterprise.Contracts.Models;

public record ExternalOrganization
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string Name { get; init; } = "";
    public string LegalName { get; init; } = "";
    public string Description { get; init; } = "";
    public string Tagline { get; init; } = "";
    public string RelationshipType { get; init; } = "";
    public string RelationshipBasis { get; init; } = "";
    public string RelationshipScope { get; init; } = "";
    public string RelationshipDefinition { get; init; } = "";
    public string Industry { get; init; } = "";
    public string Country { get; init; } = "";
    public string PrimaryDomain { get; init; } = "";
    public string Website { get; init; } = "";
    public string ContactEmail { get; init; } = "";
    public string TaxIdentifier { get; init; } = "";
    public string Segment { get; init; } = "";
    public string RevenueBand { get; init; } = "";
    public string OwnerDepartmentId { get; init; } = "";
    public string Criticality { get; init; } = "Medium";
}

public record ApplicationCounterpartyLink
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string ApplicationId { get; init; } = "";
    public string ExternalOrganizationId { get; init; } = "";
    public string RelationshipType { get; init; } = "";
    public string IntegrationType { get; init; } = "";
    public string Criticality { get; init; } = "Medium";
}

public record BusinessProcessCounterpartyLink
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string BusinessProcessId { get; init; } = "";
    public string ExternalOrganizationId { get; init; } = "";
    public string RelationshipType { get; init; } = "";
    public bool IsPrimary { get; init; }
}

public record CrossTenantAccessPolicyRecord
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string ExternalOrganizationId { get; init; } = "";
    public string ResourceTenantDomain { get; init; } = "";
    public string HomeTenantDomain { get; init; } = "";
    public string RelationshipType { get; init; } = "";
    public string PolicyName { get; init; } = "";
    public string AccessDirection { get; init; } = "Inbound";
    public string TrustLevel { get; init; } = "";
    public string DefaultAccess { get; init; } = "";
    public string ConditionalAccessProfile { get; init; } = "";
    public string AllowedResourceScope { get; init; } = "";
    public bool B2BCollaborationEnabled { get; init; } = true;
    public bool InboundTrustMfa { get; init; }
    public bool InboundTrustCompliantDevice { get; init; }
    public bool AllowInvitations { get; init; } = true;
    public bool EntitlementManagementEnabled { get; init; }
}

public record CrossTenantAccessEvent
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string AccountId { get; init; } = "";
    public string ExternalOrganizationId { get; init; } = "";
    public string EventType { get; init; } = "";
    public string EventStatus { get; init; } = "";
    public string EventCategory { get; init; } = "";
    public string? ActorAccountId { get; init; }
    public string? PolicyId { get; init; }
    public string? ResourceReference { get; init; }
    public string? EntitlementPackageName { get; init; }
    public string? ReviewDecision { get; init; }
    public string SourceSystem { get; init; } = "";
    public DateTimeOffset EventAt { get; init; } = DateTimeOffset.UtcNow;
}
