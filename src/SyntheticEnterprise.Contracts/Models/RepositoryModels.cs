namespace SyntheticEnterprise.Contracts.Models;

public record DatabaseRepository
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string Name { get; init; } = "";
    public string Engine { get; init; } = "";
    public string Environment { get; init; } = "Production";
    public string SizeGb { get; init; } = "";
    public string OwnerDepartmentId { get; init; } = "";
    public string? AssociatedApplicationId { get; init; }
    public string? HostServerId { get; init; }
    public string Sensitivity { get; init; } = "Internal";
}

public record FileShareRepository
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string ShareName { get; init; } = "";
    public string UncPath { get; init; } = "";
    public string OwnerDepartmentId { get; init; } = "";
    public string FileCount { get; init; } = "";
    public string FolderCount { get; init; } = "";
    public string TotalSizeGb { get; init; } = "";
    public string AccessModel { get; init; } = "GroupBased";
}

public record CollaborationSite
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string Platform { get; init; } = "SharePoint";
    public string Name { get; init; } = "";
    public string Url { get; init; } = "";
    public string OwnerPersonId { get; init; } = "";
    public string OwnerDepartmentId { get; init; } = "";
    public string MemberCount { get; init; } = "";
    public string FileCount { get; init; } = "";
    public string TotalSizeGb { get; init; } = "";
    public string PrivacyType { get; init; } = "Private";
}

public record RepositoryAccessGrant
{
    public string Id { get; init; } = "";
    public string RepositoryId { get; init; } = "";
    public string RepositoryType { get; init; } = "";
    public string PrincipalObjectId { get; init; } = "";
    public string PrincipalType { get; init; } = "Group";
    public string AccessLevel { get; init; } = "Read";
}

public record RepositoryAnomaly
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string Category { get; init; } = "Repository";
    public string Severity { get; init; } = "Medium";
    public string AffectedObjectId { get; init; } = "";
    public string Description { get; init; } = "";
}
