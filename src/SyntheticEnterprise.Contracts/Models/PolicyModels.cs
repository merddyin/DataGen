namespace SyntheticEnterprise.Contracts.Models;

public record PolicyRecord
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string PolicyGuid { get; init; } = "";
    public string Name { get; init; } = "";
    public string PolicyType { get; init; } = "";
    public string Platform { get; init; } = "";
    public string Category { get; init; } = "";
    public string Environment { get; init; } = "Production";
    public string Status { get; init; } = "Enabled";
    public string Description { get; init; } = "";
    public string? IdentityStoreId { get; init; }
    public string? CloudTenantId { get; init; }
    public string? SourceEntityType { get; init; }
    public string? SourceEntityId { get; init; }
}

public record PolicySettingRecord
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string PolicyId { get; init; } = "";
    public string SettingName { get; init; } = "";
    public string SettingCategory { get; init; } = "";
    public string PolicyPath { get; init; } = "";
    public string? RegistryPath { get; init; }
    public string ValueType { get; init; } = "";
    public string ConfiguredValue { get; init; } = "";
    public bool IsLegacy { get; init; }
    public bool IsConflicting { get; init; }
    public string? SourceReference { get; init; }
}

public record PolicyTargetLink
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string PolicyId { get; init; } = "";
    public string TargetType { get; init; } = "";
    public string TargetId { get; init; } = "";
    public string AssignmentMode { get; init; } = "";
    public bool LinkEnabled { get; init; } = true;
    public bool IsEnforced { get; init; }
    public int LinkOrder { get; init; }
    public string? FilterType { get; init; }
    public string? FilterValue { get; init; }
}
