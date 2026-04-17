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
    public string? OwnerPersonId { get; init; }
    public string? HostServerId { get; init; }
    public string SharePurpose { get; init; } = "Department";
    public string FileCount { get; init; } = "";
    public string FolderCount { get; init; } = "";
    public string TotalSizeGb { get; init; } = "";
    public string AccessModel { get; init; } = "GroupBased";
    public string Sensitivity { get; init; } = "Internal";
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
    public string WorkspaceType { get; init; } = "Department";
}

public record CollaborationChannel
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string CollaborationSiteId { get; init; } = "";
    public string Name { get; init; } = "";
    public string ChannelType { get; init; } = "Standard";
    public string MemberCount { get; init; } = "";
    public string MessageCount { get; init; } = "";
    public string FileCount { get; init; } = "";
}

public record CollaborationChannelTab
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string CollaborationChannelId { get; init; } = "";
    public string Name { get; init; } = "";
    public string TabType { get; init; } = "Website";
    public string TargetType { get; init; } = "ExternalUrl";
    public string? TargetId { get; init; }
    public string? TargetReference { get; init; }
    public string Vendor { get; init; } = "";
    public bool IsPinned { get; init; } = true;
}

public record DocumentLibrary
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string CollaborationSiteId { get; init; } = "";
    public string Name { get; init; } = "";
    public string TemplateType { get; init; } = "Documents";
    public string ItemCount { get; init; } = "";
    public string TotalSizeGb { get; init; } = "";
    public string Sensitivity { get; init; } = "Internal";
}

public record SitePage
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string CollaborationSiteId { get; init; } = "";
    public string Title { get; init; } = "";
    public string PageType { get; init; } = "Home";
    public string AuthorPersonId { get; init; } = "";
    public string? AssociatedLibraryId { get; init; }
    public string ViewCount { get; init; } = "";
    public DateTimeOffset LastModified { get; init; }
    public string PromotedState { get; init; } = "None";
}

public record DocumentFolder
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string DocumentLibraryId { get; init; } = "";
    public string? ParentFolderId { get; init; }
    public string Name { get; init; } = "";
    public string FolderType { get; init; } = "Working";
    public string Depth { get; init; } = "1";
    public string ItemCount { get; init; } = "";
    public string TotalSizeGb { get; init; } = "";
    public string Sensitivity { get; init; } = "Internal";
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
