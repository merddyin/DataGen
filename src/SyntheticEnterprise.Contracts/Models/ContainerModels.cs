namespace SyntheticEnterprise.Contracts.Models;

public record EnvironmentContainer
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string Name { get; init; } = "";
    public string ContainerType { get; init; } = "";
    public string Platform { get; init; } = "";
    public string? ParentContainerId { get; init; }
    public string ContainerPath { get; init; } = "";
    public string Purpose { get; init; } = "";
    public string Environment { get; init; } = "Production";
    public bool BlocksPolicyInheritance { get; init; }
    public string? IdentityStoreId { get; init; }
    public string? CloudTenantId { get; init; }
    public string? SourceEntityType { get; init; }
    public string? SourceEntityId { get; init; }
}
