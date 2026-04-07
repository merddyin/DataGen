namespace SyntheticEnterprise.Module.Contracts;

public sealed record GoldenComparisonResult(
    bool IsMatch,
    string BaselineName,
    string? DiffPath,
    IReadOnlyList<ValidationIssue> Issues);
