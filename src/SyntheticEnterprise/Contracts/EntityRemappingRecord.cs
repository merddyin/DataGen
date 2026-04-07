namespace SyntheticEnterprise.Contracts;

public sealed class EntityRemappingRecord
{
    public required string EntityType { get; init; }
    public required string OldId { get; init; }
    public string? NewId { get; init; }
    public required string RemapDisposition { get; init; }
    public string? Reason { get; init; }
}
