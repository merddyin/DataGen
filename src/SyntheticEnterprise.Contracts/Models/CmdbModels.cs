namespace SyntheticEnterprise.Contracts.Models;

public record MaintenanceWindowDefinition
{
    public string DayOfWeek { get; init; } = "";
    public string StartTimeLocal { get; init; } = "";
    public int DurationMinutes { get; init; } = 60;
    public string TimeZone { get; init; } = "UTC";
    public string Frequency { get; init; } = "Weekly";
}

public record ConfigurationItem
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string CiKey { get; init; } = "";
    public string Name { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string CiType { get; init; } = "";
    public string CiClass { get; init; } = "";
    public string? SourceEntityType { get; init; }
    public string? SourceEntityId { get; init; }
    public string? Manufacturer { get; init; }
    public string? Vendor { get; init; }
    public string? Model { get; init; }
    public string? Version { get; init; }
    public string? SerialNumber { get; init; }
    public string? AssetTag { get; init; }
    public string? Fqdn { get; init; }
    public string? UncPath { get; init; }
    public string Environment { get; init; } = "Production";
    public string OperationalStatus { get; init; } = "Active";
    public string LifecycleStatus { get; init; } = "InService";
    public string? LocationType { get; init; }
    public string? LocationId { get; init; }
    public string? BusinessOwnerPersonId { get; init; }
    public string? TechnicalOwnerPersonId { get; init; }
    public string? SupportTeamId { get; init; }
    public string? OwningDepartmentId { get; init; }
    public string? OwningLobId { get; init; }
    public string ServiceTier { get; init; } = "Tier2";
    public string ServiceClassification { get; init; } = "Standard";
    public string? BusinessCriticality { get; init; }
    public string? DataSensitivity { get; init; }
    public MaintenanceWindowDefinition? MaintenanceWindow { get; init; }
    public DateTimeOffset? InstallDate { get; init; }
    public DateTimeOffset? RetirementDate { get; init; }
    public DateTimeOffset? LastReviewedAt { get; init; }
    public int? RtoHours { get; init; }
    public int? RpoHours { get; init; }
    public string? Notes { get; init; }
}

public record ConfigurationItemRelationship
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string SourceConfigurationItemId { get; init; } = "";
    public string TargetConfigurationItemId { get; init; } = "";
    public string RelationshipType { get; init; } = "";
    public bool IsPrimary { get; init; }
    public string Confidence { get; init; } = "High";
    public string? SourceEvidence { get; init; }
    public string? Notes { get; init; }
}

public record CmdbSourceRecord
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string SourceSystem { get; init; } = "";
    public string SourceRecordId { get; init; } = "";
    public string CiType { get; init; } = "";
    public string CiClass { get; init; } = "";
    public string Name { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string? ObservedManufacturer { get; init; }
    public string? ObservedVendor { get; init; }
    public string? ObservedModel { get; init; }
    public string? ObservedVersion { get; init; }
    public string? ObservedSerialNumber { get; init; }
    public string? ObservedAssetTag { get; init; }
    public string? ObservedLocation { get; init; }
    public string? ObservedEnvironment { get; init; }
    public string? ObservedOperationalStatus { get; init; }
    public string? ObservedLifecycleStatus { get; init; }
    public string? ObservedBusinessOwner { get; init; }
    public string? ObservedTechnicalOwner { get; init; }
    public string? ObservedSupportGroup { get; init; }
    public string? ObservedOwningLob { get; init; }
    public string? ObservedServiceTier { get; init; }
    public string? ObservedServiceClassification { get; init; }
    public string? ObservedBusinessCriticality { get; init; }
    public string? ObservedMaintenanceWindow { get; init; }
    public string MatchStatus { get; init; } = "Matched";
    public string Confidence { get; init; } = "Medium";
    public DateTimeOffset LastSeen { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastImported { get; init; } = DateTimeOffset.UtcNow;
}

public record CmdbSourceLink
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string SourceRecordId { get; init; } = "";
    public string ConfigurationItemId { get; init; } = "";
    public string LinkType { get; init; } = "Matched";
    public string MatchMethod { get; init; } = "SyntheticProjection";
    public string Confidence { get; init; } = "Medium";
}

public record CmdbSourceRelationship
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string SourceSystem { get; init; } = "";
    public string SourceRelationshipId { get; init; } = "";
    public string SourceRecordId { get; init; } = "";
    public string TargetRecordId { get; init; } = "";
    public string RelationshipType { get; init; } = "";
    public bool IsPrimary { get; init; }
    public string Confidence { get; init; } = "Medium";
    public string Status { get; init; } = "Active";
}
