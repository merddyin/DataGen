namespace SyntheticEnterprise.Contracts;

public sealed class MergeResolutionRecord
{
    public required string EntityType { get; init; }
    public required string ConflictKey { get; init; }
    public required string Resolution { get; init; }
    public string? Reason { get; init; }
}
