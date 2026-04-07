namespace SyntheticEnterprise.Module.Contracts;

public enum ValidationIssueSeverity
{
    Info,
    Warning,
    Error
}

public sealed record ValidationIssue(
    ValidationIssueSeverity Severity,
    string Code,
    string Message,
    string? Target = null);
