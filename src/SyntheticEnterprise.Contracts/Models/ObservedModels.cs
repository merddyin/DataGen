namespace SyntheticEnterprise.Contracts.Models;

public record ObservedEntitySnapshot
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string SourceSystem { get; init; } = "";
    public string EntityType { get; init; } = "";
    public string EntityId { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string ObservedState { get; init; } = "";
    public string GroundTruthState { get; init; } = "";
    public string DriftType { get; init; } = "None";
    public string? Environment { get; init; }
    public string? OwnerReference { get; init; }
    public DateTimeOffset RecordedAt { get; init; } = DateTimeOffset.UtcNow;
}
