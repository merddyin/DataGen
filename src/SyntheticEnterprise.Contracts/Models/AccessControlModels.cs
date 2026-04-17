namespace SyntheticEnterprise.Contracts.Models;

public record AccessControlEvidenceRecord
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string PrincipalObjectId { get; init; } = "";
    public string PrincipalType { get; init; } = "";
    public string TargetType { get; init; } = "";
    public string TargetId { get; init; } = "";
    public string RightName { get; init; } = "";
    public string AccessType { get; init; } = "Allow";
    public bool IsInherited { get; init; }
    public bool IsDefaultEntry { get; init; }
    public string SourceSystem { get; init; } = "";
    public string? InheritanceSourceId { get; init; }
    public string? Notes { get; init; }
}
