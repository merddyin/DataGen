namespace SyntheticEnterprise.Contracts;

public sealed class ReferenceRepairRecord
{
    public required string SourceEntityType { get; init; }
    public required string SourceEntityId { get; init; }
    public required string ReferencePath { get; init; }
    public string? OldTargetId { get; init; }
    public string? NewTargetId { get; init; }
    public required string RepairDisposition { get; init; }
    public string? Reason { get; init; }
}
