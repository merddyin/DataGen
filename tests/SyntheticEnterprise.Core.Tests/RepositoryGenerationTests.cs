using Microsoft.Extensions.DependencyInjection;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Core.Abstractions;
using SyntheticEnterprise.Core.DependencyInjection;

namespace SyntheticEnterprise.Core.Tests;

public sealed class RepositoryGenerationTests
{
    [Fact]
    public void WorldGenerator_Populates_Richer_FileShare_And_Collaboration_Structures()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var result = generator.Generate(
            new GenerationContext
            {
                Scenario = new ScenarioDefinition
                {
                    Name = "Repository Realism Test",
                    Applications = new ApplicationProfile
                    {
                        IncludeApplications = true,
                        BaseApplicationCount = 5,
                        IncludeLineOfBusinessApplications = true,
                        IncludeSaaSApplications = true
                    },
                    Companies = new()
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Repository Realism Co",
                            Industry = "Manufacturing",
                            EmployeeCount = 220,
                            BusinessUnitCount = 2,
                            DepartmentCountPerBusinessUnit = 4,
                            TeamCountPerDepartment = 2,
                            OfficeCount = 2,
                            FileShareCount = 10,
                            CollaborationSiteCount = 12,
                            Countries = new() { "United States" }
                        }
                    }
                }
            },
            new CatalogSet());

        Assert.NotEmpty(result.World.FileShares);
        Assert.NotEmpty(result.World.CollaborationSites);
        Assert.NotEmpty(result.World.DocumentLibraries);
        Assert.NotEmpty(result.World.CollaborationChannels);
        Assert.NotEmpty(result.World.CollaborationChannelTabs);
        Assert.NotEmpty(result.World.SitePages);
        Assert.NotEmpty(result.World.DocumentFolders);
        Assert.NotEmpty(result.World.RepositoryAccessGrants);

        Assert.Contains(result.World.FileShares, share => share.SharePurpose == "Department");
        Assert.Contains(result.World.FileShares, share => share.SharePurpose == "UserHome");
        Assert.Contains(result.World.FileShares, share => share.SharePurpose == "UserProfile");
        Assert.Contains(result.World.FileShares, share => !string.IsNullOrWhiteSpace(share.OwnerPersonId));
        Assert.Contains(result.World.FileShares, share => !string.IsNullOrWhiteSpace(share.HostServerId));

        Assert.Contains(result.World.CollaborationSites, site => site.Platform == "Teams");
        Assert.Contains(result.World.CollaborationSites, site => site.Platform == "SharePoint");
        Assert.Contains(result.World.CollaborationChannels, channel => channel.ChannelType == "Private" || channel.ChannelType == "Shared");
        Assert.Contains(result.World.CollaborationChannelTabs, tab => tab.TargetType == "DocumentLibrary" || tab.TargetType == "Application");
        Assert.Contains(result.World.DocumentLibraries, library => library.TemplateType == "Documents");
        Assert.Contains(result.World.SitePages, page => page.PageType == "Home" || page.PageType == "News");
        Assert.Contains(result.World.DocumentFolders, folder => !string.IsNullOrWhiteSpace(folder.ParentFolderId));
        Assert.Contains(result.World.RepositoryAccessGrants, grant => grant.RepositoryType == "FileShare" && grant.PrincipalType == "Account");
        Assert.Contains(result.World.RepositoryAccessGrants, grant => grant.RepositoryType == "DocumentLibrary");
        Assert.Contains(result.World.RepositoryAccessGrants, grant => grant.RepositoryType == "CollaborationChannel");
        Assert.Contains(result.World.RepositoryAccessGrants, grant => grant.RepositoryType == "SitePage");
        Assert.Contains(result.World.RepositoryAccessGrants, grant => grant.RepositoryType == "DocumentFolder");
        Assert.Contains(result.World.AccessControlEvidence, evidence =>
            evidence.TargetType == "FileShare"
            && evidence.RightName == "NtfsModify"
            && evidence.SourceSystem == "NTFS");
        Assert.Contains(result.World.AccessControlEvidence, evidence =>
            evidence.TargetType == "FileShare"
            && evidence.RightName == "ShareFullControl"
            && evidence.SourceSystem == "SMB");
        Assert.Contains(result.World.AccessControlEvidence, evidence =>
            evidence.TargetType == "FileShare"
            && evidence.RightName == "ShareRead"
            && evidence.AccessType == "Deny"
            && evidence.SourceSystem == "SMB");
        Assert.Contains(result.World.AccessControlEvidence, evidence =>
            evidence.TargetType == "FileShare"
            && evidence.RightName == "NtfsRead"
            && evidence.AccessType == "Deny"
            && evidence.SourceSystem == "NTFS");
        Assert.Contains(result.World.AccessControlEvidence, evidence =>
            evidence.TargetType == "DocumentLibrary"
            && evidence.RightName == "LibraryContribute"
            && evidence.IsInherited
            && evidence.SourceSystem == "SharePoint");
        Assert.Contains(result.World.AccessControlEvidence, evidence =>
            evidence.TargetType == "DocumentLibrary"
            && evidence.RightName == "LibraryFullControl"
            && !evidence.IsInherited
            && evidence.PrincipalType == "Account"
            && evidence.SourceSystem == "SharePoint");
        Assert.Contains(result.World.AccessControlEvidence, evidence =>
            evidence.TargetType == "CollaborationChannel"
            && evidence.RightName == "ChannelMember"
            && evidence.IsInherited
            && evidence.SourceSystem == "Teams");
        Assert.Contains(result.World.AccessControlEvidence, evidence =>
            evidence.TargetType == "CollaborationChannel"
            && evidence.RightName == "ChannelMember"
            && evidence.AccessType == "Deny"
            && evidence.SourceSystem == "Teams");
        Assert.Contains(result.World.AccessControlEvidence, evidence =>
            evidence.TargetType == "Database"
            && evidence.RightName == "DbDataWriter"
            && evidence.SourceSystem == "DatabaseAcl");
        Assert.Contains(result.World.AccessControlEvidence, evidence =>
            evidence.TargetType == "DocumentFolder"
            && evidence.RightName == "FolderModify"
            && !evidence.IsInherited
            && evidence.SourceSystem == "SharePoint");
        Assert.Contains(result.World.AccessControlEvidence, evidence =>
            evidence.TargetType == "DocumentFolder"
            && evidence.RightName == "FolderRead"
            && evidence.AccessType == "Deny"
            && evidence.SourceSystem == "SharePoint");
        Assert.Contains(result.World.AccessControlEvidence, evidence =>
            evidence.TargetType == "CollaborationSite"
            && evidence.RightName == "SiteCollectionAdmin"
            && evidence.PrincipalType == "Account"
            && !evidence.IsInherited
            && evidence.SourceSystem == "SharePoint");
        Assert.Contains(result.World.AccessControlEvidence, evidence =>
            evidence.TargetType == "CollaborationSite"
            && evidence.RightName == "SiteRead"
            && evidence.AccessType == "Deny"
            && evidence.SourceSystem == "SharePoint");
        Assert.Contains(result.World.AccessControlEvidence, evidence =>
            evidence.TargetType == "SitePage"
            && evidence.RightName == "PageEdit"
            && evidence.PrincipalType == "Account"
            && !evidence.IsInherited
            && evidence.SourceSystem == "SharePoint");
    }

    [Fact]
    public void WorldGenerator_Uses_Repository_Pattern_Catalog_For_Naming_And_Access_Model()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var result = generator.Generate(
            new GenerationContext
            {
                Scenario = new ScenarioDefinition
                {
                    Name = "Repository Pattern Test",
                    Applications = new ApplicationProfile
                    {
                        IncludeApplications = true,
                        BaseApplicationCount = 4,
                        IncludeLineOfBusinessApplications = true,
                        IncludeSaaSApplications = true
                    },
                    Companies = new()
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Pattern Works",
                            Industry = "Manufacturing",
                            EmployeeCount = 160,
                            BusinessUnitCount = 1,
                            DepartmentCountPerBusinessUnit = 3,
                            TeamCountPerDepartment = 2,
                            OfficeCount = 1,
                            DatabaseCount = 2,
                            FileShareCount = 2,
                            CollaborationSiteCount = 2,
                            Countries = new() { "United States" }
                        }
                    }
                }
            },
            new CatalogSet
            {
                CsvCatalogs = new Dictionary<string, IReadOnlyList<Dictionary<string, string?>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["repository_patterns"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Type", "Database"), ("Pattern", "ERP_{dept}_{n}"), ("OwnerHint", "Finance"), ("AccessModel", "GroupBased")),
                        NewRow(("Type", "FileShare"), ("Pattern", "{dept}-archive-{n}"), ("OwnerHint", "Department"), ("AccessModel", "GroupBased")),
                        NewRow(("Type", "CollaborationSite"), ("Pattern", "{dept} Projects"), ("OwnerHint", "Department"), ("AccessModel", "Private"))
                    }
                }
            });

        Assert.Contains(result.World.Databases, database => database.Name.StartsWith("ERP_", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.World.FileShares, share => share.ShareName.Contains("-archive-", StringComparison.OrdinalIgnoreCase) && share.SharePurpose == "DepartmentArchive" && share.AccessModel == "GroupBased");
        Assert.Contains(result.World.CollaborationSites, site => site.Name.EndsWith("Projects", StringComparison.OrdinalIgnoreCase) && site.PrivacyType == "Private");
    }

    [Fact]
    public void WorldGenerator_Uses_Application_Repository_Pattern_Catalog_For_Link_Shaping()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var result = generator.Generate(
            new GenerationContext
            {
                Scenario = new ScenarioDefinition
                {
                    Name = "Repository Link Pattern Test",
                    Applications = new ApplicationProfile
                    {
                        IncludeApplications = true,
                        BaseApplicationCount = 4,
                        IncludeLineOfBusinessApplications = true,
                        IncludeSaaSApplications = true
                    },
                    Companies = new()
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Pattern Works",
                            Industry = "Manufacturing",
                            EmployeeCount = 160,
                            BusinessUnitCount = 1,
                            DepartmentCountPerBusinessUnit = 3,
                            TeamCountPerDepartment = 2,
                            OfficeCount = 1,
                            DatabaseCount = 2,
                            FileShareCount = 2,
                            CollaborationSiteCount = 2,
                            Countries = new() { "United States" }
                        }
                    }
                }
            },
            new CatalogSet
            {
                CsvCatalogs = new Dictionary<string, IReadOnlyList<Dictionary<string, string?>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["repository_patterns"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Type", "Database"), ("Pattern", "ERP_{dept}_{n}"), ("OwnerHint", "Finance"), ("AccessModel", "GroupBased")),
                        NewRow(("Type", "FileShare"), ("Pattern", "{dept}-archive-{n}"), ("OwnerHint", "Department"), ("AccessModel", "GroupBased")),
                        NewRow(("Type", "CollaborationSite"), ("Pattern", "{dept} Projects"), ("OwnerHint", "Department"), ("AccessModel", "Private"))
                    },
                    ["application_repository_patterns"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("RepositoryType", "Database"), ("MatchApplicationNameContains", "HRIS"), ("MatchCategory", "HR"), ("IndustryTags", "All"), ("MinimumEmployees", "1"), ("RelationshipType", "PrimaryDataStore"), ("Criticality", "High")),
                        NewRow(("RepositoryType", "FileShare"), ("MatchApplicationNameContains", "IT Service Management"), ("MatchCategory", "Operations"), ("MatchSharePurpose", "DepartmentArchive"), ("IndustryTags", "All"), ("MinimumEmployees", "1"), ("RelationshipType", "OperationalArchive"), ("Criticality", "Medium")),
                        NewRow(("RepositoryType", "CollaborationSite"), ("MatchApplicationNameContains", "IT Service Management"), ("MatchCategory", "Operations"), ("IndustryTags", "All"), ("MinimumEmployees", "1"), ("RelationshipType", "OperationalWorkspace"), ("Criticality", "Medium"))
                    }
                }
            });

        Assert.Contains(result.World.ApplicationRepositoryLinks, link =>
            link.RepositoryType == "Database"
            && link.RelationshipType == "PrimaryDataStore"
            && link.Criticality == "High"
            && result.World.Applications.Any(application =>
                application.Id == link.ApplicationId
                && application.Name.Contains("HRIS", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(result.World.ApplicationRepositoryLinks, link =>
            link.RepositoryType == "FileShare"
            && link.RelationshipType == "OperationalArchive"
            && link.Criticality == "Medium"
            && result.World.Applications.Any(application =>
                application.Id == link.ApplicationId
                && application.Name.Contains("IT Service Management", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(result.World.ApplicationRepositoryLinks, link =>
            link.RepositoryType == "CollaborationSite"
            && link.RelationshipType == "OperationalWorkspace"
            && link.Criticality == "Medium"
            && result.World.Applications.Any(application =>
                application.Id == link.ApplicationId
                && application.Name.Contains("IT Service Management", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void WorldGenerator_Uses_Collaboration_Tab_Pattern_Catalog_For_Application_Tabs()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var result = generator.Generate(
            new GenerationContext
            {
                Scenario = new ScenarioDefinition
                {
                    Name = "Repository Tab Pattern Test",
                    Applications = new ApplicationProfile
                    {
                        IncludeApplications = true,
                        BaseApplicationCount = 4,
                        IncludeLineOfBusinessApplications = true,
                        IncludeSaaSApplications = true
                    },
                    Companies = new()
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Pattern Works",
                            Industry = "Manufacturing",
                            EmployeeCount = 220,
                            BusinessUnitCount = 2,
                            DepartmentCountPerBusinessUnit = 4,
                            TeamCountPerDepartment = 2,
                            OfficeCount = 2,
                            CollaborationSiteCount = 8,
                            Countries = new() { "United States" }
                        }
                    }
                }
            },
            new CatalogSet
            {
                CsvCatalogs = new Dictionary<string, IReadOnlyList<Dictionary<string, string?>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["application_collaboration_tab_patterns"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("ChannelType", "Standard"), ("WorkspaceType", "Department"), ("MatchApplicationNameContains", "IT Service Management"), ("MatchCategory", "Operations"), ("IndustryTags", "All"), ("MinimumEmployees", "1"), ("PreferredTabName", "Ops Board"), ("TabType", "Application"), ("TargetType", "Application"), ("IsPinned", "true"))
                    }
                }
            });

        Assert.Contains(result.World.CollaborationChannelTabs, tab =>
            tab.Name == "Ops Board"
            && tab.TabType == "Application"
            && tab.TargetType == "Application"
            && tab.IsPinned
            && result.World.Applications.Any(application =>
                application.Id == tab.TargetId
                && application.Name.Contains("IT Service Management", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void WorldGenerator_Uses_Collaboration_Content_Pattern_Catalogs()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var result = generator.Generate(
            new GenerationContext
            {
                Seed = 1,
                Scenario = new ScenarioDefinition
                {
                    Name = "Repository Content Pattern Test",
                    Applications = new ApplicationProfile
                    {
                        IncludeApplications = true,
                        BaseApplicationCount = 4,
                        IncludeLineOfBusinessApplications = true,
                        IncludeSaaSApplications = true
                    },
                    Companies = new()
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Pattern Works",
                            Industry = "Manufacturing",
                            EmployeeCount = 220,
                            BusinessUnitCount = 2,
                            DepartmentCountPerBusinessUnit = 4,
                            TeamCountPerDepartment = 2,
                            OfficeCount = 2,
                            CollaborationSiteCount = 8,
                            Countries = new() { "United States" }
                        }
                    }
                }
            },
            new CatalogSet
            {
                CsvCatalogs = new Dictionary<string, IReadOnlyList<Dictionary<string, string?>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["collaboration_channel_patterns"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Platform", "Teams"), ("WorkspaceType", "Department"), ("PrivacyType", "Private"), ("ChannelName", "Risk Review"), ("ChannelType", "Standard"), ("IndustryTags", "All"), ("MinimumEmployees", "1"))
                    },
                    ["document_library_patterns"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Platform", "Teams"), ("WorkspaceType", "Department"), ("LibraryName", "Runbooks"), ("TemplateType", "Knowledge"), ("Sensitivity", "Confidential"), ("IndustryTags", "All"), ("MinimumEmployees", "1"))
                    },
                    ["site_page_patterns"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Platform", "Teams"), ("WorkspaceType", "Department"), ("PageTitle", "Operations Playbook"), ("PageType", "Knowledge"), ("PromotedState", "None"), ("AssociatedLibraryName", "Runbooks"), ("IndustryTags", "All"), ("MinimumEmployees", "1"))
                    },
                    ["document_folder_patterns"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("LibraryName", "Runbooks"), ("WorkspaceType", "Department"), ("FolderName", "Escalations"), ("FolderType", "Department"), ("Depth", "1"), ("IndustryTags", "All"), ("MinimumEmployees", "1")),
                        NewRow(("LibraryName", "Runbooks"), ("WorkspaceType", "Department"), ("ParentFolderName", "Escalations"), ("FolderName", "Sev1"), ("FolderType", "Working"), ("Depth", "2"), ("IndustryTags", "All"), ("MinimumEmployees", "1"))
                    }
                }
            });

        Assert.Contains(result.World.CollaborationChannels, channel =>
            channel.Name == "Risk Review"
            && channel.ChannelType == "Standard");
        Assert.Contains(result.World.DocumentLibraries, library =>
            library.Name == "Runbooks"
            && library.TemplateType == "Knowledge"
            && library.Sensitivity == "Confidential");
        Assert.Contains(result.World.SitePages, page =>
            page.Title.Contains("Operations Playbook", StringComparison.OrdinalIgnoreCase)
            && page.PageType == "Knowledge"
            && result.World.DocumentLibraries.Any(library =>
                library.Id == page.AssociatedLibraryId
                && library.Name == "Runbooks"));
        Assert.Contains(result.World.DocumentFolders, folder =>
            folder.Name == "Escalations"
            && folder.Depth == "1");
        Assert.Contains(result.World.DocumentFolders, folder =>
            folder.Name == "Sev1"
            && folder.Depth == "2"
            && result.World.DocumentFolders.Any(parent =>
                parent.Id == folder.ParentFolderId
                && parent.Name == "Escalations"));
    }

    private static Dictionary<string, string?> NewRow(params (string Key, string? Value)[] entries)
    {
        var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in entries)
        {
            row[key] = value;
        }

        return row;
    }
}
