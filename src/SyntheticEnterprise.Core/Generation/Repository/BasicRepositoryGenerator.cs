namespace SyntheticEnterprise.Core.Generation.Repository;

using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Core.Abstractions;

public sealed class BasicRepositoryGenerator : IRepositoryGenerator
{
    private readonly IIdFactory _idFactory;
    private readonly IRandomSource _randomSource;

    public BasicRepositoryGenerator(IIdFactory idFactory, IRandomSource randomSource)
    {
        _idFactory = idFactory;
        _randomSource = randomSource;
    }

    public void GenerateRepositories(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs)
    {
        foreach (var company in world.Companies)
        {
            var definition = context.Scenario.Companies.FirstOrDefault(c =>
                string.Equals(c.Name, company.Name, StringComparison.OrdinalIgnoreCase));

            if (definition is null)
            {
                continue;
            }

            var departments = world.Departments.Where(d => d.CompanyId == company.Id).ToList();
            var groups = world.Groups.Where(g => g.CompanyId == company.Id).ToList();
            var people = world.People.Where(p => p.CompanyId == company.Id).ToList();
            var accounts = world.Accounts.Where(a => a.CompanyId == company.Id).ToList();
            var servers = world.Servers.Where(s => s.CompanyId == company.Id).ToList();
            var applications = world.Applications.Where(a => a.CompanyId == company.Id).ToList();
            var collaborationChannelPatterns = ReadCollaborationChannelPatterns(catalogs, company.Industry ?? string.Empty, definition.EmployeeCount);
            var documentLibraryPatterns = ReadDocumentLibraryPatterns(catalogs, company.Industry ?? string.Empty, definition.EmployeeCount);
            var sitePagePatterns = ReadSitePagePatterns(catalogs, company.Industry ?? string.Empty, definition.EmployeeCount);
            var documentFolderPatterns = ReadDocumentFolderPatterns(catalogs, company.Industry ?? string.Empty, definition.EmployeeCount);
            var applicationRepositoryPatterns = ReadApplicationRepositoryPatterns(
                catalogs,
                company.Industry ?? string.Empty,
                definition.EmployeeCount);

            CreateDatabases(world, company, definition, departments, servers, applications, catalogs, applicationRepositoryPatterns);
            CreateDepartmentFileShares(world, company, definition, departments, servers, applications, catalogs, applicationRepositoryPatterns);
            CreateUserFileShares(world, company, people, accounts, servers);
            CreateCollaborationSites(
                world,
                company,
                definition,
                departments,
                people,
                applications,
                catalogs,
                applicationRepositoryPatterns,
                collaborationChannelPatterns,
                documentLibraryPatterns,
                sitePagePatterns,
                documentFolderPatterns);
            CreateAccessGrants(world, company, departments, groups, accounts);
        }
    }

    private void CreateDatabases(
        SyntheticEnterpriseWorld world,
        Company company,
        ScenarioCompanyDefinition definition,
        IReadOnlyList<Department> departments,
        IReadOnlyList<ServerAsset> servers,
        IReadOnlyList<ApplicationRecord> applications,
        CatalogSet catalogs,
        IReadOnlyList<ApplicationRepositoryPatternRule> applicationRepositoryPatterns)
    {
        var engines = new[] { "SQL Server", "PostgreSQL", "MySQL", "Oracle" };
        var sensitivities = new[] { "Internal", "Confidential", "Restricted" };
        var prefixes = new[] { "ERP", "HRIS", "CRM", "MES", "DWH", "OPS", "FIN", "PAY", "SUP" };
        var patterns = ReadRepositoryPatterns(catalogs, "Database");

        for (var i = 0; i < Math.Max(1, definition.DatabaseCount); i++)
        {
            var dept = departments[i % departments.Count];
            var server = servers.Count > 0 ? servers[i % servers.Count] : null;
            var selection = SelectApplicationForRepository(
                applications,
                applicationRepositoryPatterns,
                "Database",
                dept.Name,
                null,
                null,
                prefixes[i % prefixes.Length],
                dept.Id);
            var pattern = patterns.Count == 0 ? null : patterns[i % patterns.Count];
            var database = new DatabaseRepository
            {
                Id = _idFactory.Next("DB"),
                CompanyId = company.Id,
                Name = pattern is null ? $"{prefixes[i % prefixes.Length]}_{Slug(dept.Name)}_{i + 1:00}" : ApplySlugPattern(pattern.Pattern, dept.Name, i + 1),
                Engine = engines[i % engines.Length],
                Environment = i % 6 == 0 ? "Staging" : "Production",
                SizeGb = ((i + 1) * 25 + _randomSource.Next(5, 90)).ToString(),
                OwnerDepartmentId = dept.Id,
                AssociatedApplicationId = selection?.Application.Id,
                HostServerId = server?.Id,
                Sensitivity = sensitivities[i % sensitivities.Length]
            };
            world.Databases.Add(database);

            AddApplicationRepositoryLink(world, company, selection, database.Id, "Database", "PrimaryDataStore");
        }
    }

    private void CreateDepartmentFileShares(
        SyntheticEnterpriseWorld world,
        Company company,
        ScenarioCompanyDefinition definition,
        IReadOnlyList<Department> departments,
        IReadOnlyList<ServerAsset> servers,
        IReadOnlyList<ApplicationRecord> applications,
        CatalogSet catalogs,
        IReadOnlyList<ApplicationRepositoryPatternRule> applicationRepositoryPatterns)
    {
        var fileServers = servers
            .Where(server => string.Equals(server.ServerRole, "File Server", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var patterns = ReadRepositoryPatterns(catalogs, "FileShare");

        for (var i = 0; i < Math.Max(1, definition.FileShareCount); i++)
        {
            var dept = departments[i % departments.Count];
            var pattern = patterns.Count == 0 ? null : patterns[i % patterns.Count];
            var shareName = pattern is null ? $"{Slug(dept.Name)}-share-{i + 1:00}" : ApplySlugPattern(pattern.Pattern, dept.Name, i + 1);
            var hostServer = fileServers.Count > 0 ? fileServers[i % fileServers.Count] : servers.FirstOrDefault();
            var share = new FileShareRepository
            {
                Id = _idFactory.Next("FS"),
                CompanyId = company.Id,
                ShareName = shareName,
                UncPath = $"\\\\files.{Slug(company.Name)}.test\\{shareName}",
                OwnerDepartmentId = dept.Id,
                OwnerPersonId = null,
                HostServerId = hostServer?.Id,
                SharePurpose = DetermineSharePurpose(shareName, i),
                FileCount = (500 + _randomSource.Next(0, 20000)).ToString(),
                FolderCount = (20 + _randomSource.Next(0, 700)).ToString(),
                TotalSizeGb = (10 + _randomSource.Next(0, 2500)).ToString(),
                AccessModel = !string.IsNullOrWhiteSpace(pattern?.AccessModel) ? pattern.AccessModel : i % 4 == 0 ? "Mixed" : "GroupBased",
                Sensitivity = i % 5 == 0 ? "Confidential" : "Internal"
            };
            world.FileShares.Add(share);

            var selection = SelectApplicationForRepository(
                applications,
                applicationRepositoryPatterns,
                "FileShare",
                dept.Name,
                share.SharePurpose,
                null,
                "Portal",
                "Workspace",
                "Operations",
                "Document",
                "Knowledge",
                dept.Id);
            AddApplicationRepositoryLink(world, company, selection, share.Id, "FileShare", "DocumentStore");
        }
    }

    private void CreateUserFileShares(
        SyntheticEnterpriseWorld world,
        Company company,
        IReadOnlyList<Person> people,
        IReadOnlyList<DirectoryAccount> accounts,
        IReadOnlyList<ServerAsset> servers)
    {
        var userAccountsByPersonId = accounts
            .Where(account => string.Equals(account.AccountType, "User", StringComparison.OrdinalIgnoreCase)
                              && !string.IsNullOrWhiteSpace(account.PersonId))
            .GroupBy(account => account.PersonId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var fileServers = servers
            .Where(server => string.Equals(server.ServerRole, "File Server", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var targetPeople = people
            .Where(person => userAccountsByPersonId.ContainsKey(person.Id))
            .ToList();

        for (var i = 0; i < targetPeople.Count; i++)
        {
            var person = targetPeople[i];
            var account = userAccountsByPersonId[person.Id];
            var shareToken = account.SamAccountName;
            var hostServer = fileServers.Count > 0 ? fileServers[i % fileServers.Count] : servers.FirstOrDefault();

            world.FileShares.Add(new FileShareRepository
            {
                Id = _idFactory.Next("FS"),
                CompanyId = company.Id,
                ShareName = $"home-{shareToken}",
                UncPath = $"\\\\files.{Slug(company.Name)}.test\\home\\{shareToken}",
                OwnerDepartmentId = person.DepartmentId,
                OwnerPersonId = person.Id,
                HostServerId = hostServer?.Id,
                SharePurpose = "UserHome",
                FileCount = (20 + _randomSource.Next(0, 2400)).ToString(),
                FolderCount = (5 + _randomSource.Next(0, 120)).ToString(),
                TotalSizeGb = (1 + _randomSource.Next(0, 120)).ToString(),
                AccessModel = "DirectOwner",
                Sensitivity = i % 6 == 0 ? "Confidential" : "Internal"
            });

            if (i % 2 == 0)
            {
                world.FileShares.Add(new FileShareRepository
                {
                    Id = _idFactory.Next("FS"),
                    CompanyId = company.Id,
                    ShareName = $"profile-{shareToken}",
                    UncPath = $"\\\\profiles.{Slug(company.Name)}.test\\profiles$\\{shareToken}",
                    OwnerDepartmentId = person.DepartmentId,
                    OwnerPersonId = person.Id,
                    HostServerId = hostServer?.Id,
                    SharePurpose = "UserProfile",
                    FileCount = (10 + _randomSource.Next(0, 800)).ToString(),
                    FolderCount = (4 + _randomSource.Next(0, 80)).ToString(),
                    TotalSizeGb = (1 + _randomSource.Next(0, 80)).ToString(),
                    AccessModel = "DirectOwner",
                    Sensitivity = "Confidential"
                });
            }
        }
    }

    private void CreateCollaborationSites(
        SyntheticEnterpriseWorld world,
        Company company,
        ScenarioCompanyDefinition definition,
        IReadOnlyList<Department> departments,
        IReadOnlyList<Person> people,
        IReadOnlyList<ApplicationRecord> applications,
        CatalogSet catalogs,
        IReadOnlyList<ApplicationRepositoryPatternRule> applicationRepositoryPatterns,
        IReadOnlyList<CollaborationChannelPatternRule> collaborationChannelPatterns,
        IReadOnlyList<DocumentLibraryPatternRule> documentLibraryPatterns,
        IReadOnlyList<SitePagePatternRule> sitePagePatterns,
        IReadOnlyList<DocumentFolderPatternRule> documentFolderPatterns)
    {
        var patterns = ReadRepositoryPatterns(catalogs, "CollaborationSite");

        for (var i = 0; i < Math.Max(1, definition.CollaborationSiteCount); i++)
        {
            var dept = departments[i % departments.Count];
            var owner = people[i % people.Count];
            var platform = i % 5 == 0 ? "Teams" : "SharePoint";
            var pattern = patterns.Count == 0 ? null : patterns[i % patterns.Count];
            var siteName = pattern is null ? $"{dept.Name} {(i % 3 == 0 ? "Operations" : i % 3 == 1 ? "Workspace" : "Projects")}" : ApplyDisplayPattern(pattern.Pattern, dept.Name, i + 1);
            var workspaceType = platform == "Teams"
                ? (i % 3 == 0 ? "Team" : i % 3 == 1 ? "Project" : "Department")
                : (i % 3 == 0 ? "Department" : i % 3 == 1 ? "Knowledge" : "Project");

            var site = new CollaborationSite
            {
                Id = _idFactory.Next("SITE"),
                CompanyId = company.Id,
                Platform = platform,
                Name = siteName,
                Url = $"https://collab.{Slug(company.Name)}.test/sites/{Slug(siteName)}",
                OwnerPersonId = owner.Id,
                OwnerDepartmentId = dept.Id,
                MemberCount = (8 + _randomSource.Next(0, 220)).ToString(),
                FileCount = (100 + _randomSource.Next(0, 25000)).ToString(),
                TotalSizeGb = (1 + _randomSource.Next(0, 800)).ToString(),
                PrivacyType = DeterminePrivacyType(pattern?.AccessModel, i),
                WorkspaceType = workspaceType
            };
            world.CollaborationSites.Add(site);

            var libraries = CreateLibrariesAndFoldersForSite(world, company, site, documentLibraryPatterns, documentFolderPatterns);
            CreatePagesForSite(world, company, site, owner, libraries, sitePagePatterns);
            if (string.Equals(site.Platform, "Teams", StringComparison.OrdinalIgnoreCase))
            {
                var channels = CreateChannelsForSite(world, company, site, collaborationChannelPatterns);
                CreateTabsForChannels(world, company, site, channels, libraries, applications, catalogs);
            }

            var selection = SelectApplicationForRepository(
                applications,
                applicationRepositoryPatterns,
                "CollaborationSite",
                dept.Name,
                null,
                site.WorkspaceType,
                "Portal",
                "Workspace",
                "Teams",
                "Campaign",
                "Supplier",
                "Service",
                dept.Id);
            AddApplicationRepositoryLink(world, company, selection, site.Id, "CollaborationSite", "CollaborationSpace");
        }
    }

    private List<DocumentLibrary> CreateLibrariesAndFoldersForSite(
        SyntheticEnterpriseWorld world,
        Company company,
        CollaborationSite site,
        IReadOnlyList<DocumentLibraryPatternRule> libraryPatterns,
        IReadOnlyList<DocumentFolderPatternRule> folderPatterns)
        => CreateLibrariesForSite(world, company, site, createFolders: true, libraryPatterns, folderPatterns);

    private List<DocumentLibrary> CreateLibrariesForSite(
        SyntheticEnterpriseWorld world,
        Company company,
        CollaborationSite site,
        bool createFolders,
        IReadOnlyList<DocumentLibraryPatternRule> libraryPatterns,
        IReadOnlyList<DocumentFolderPatternRule> folderPatterns)
    {
        var curatedPatterns = libraryPatterns
            .Where(pattern =>
                string.Equals(pattern.Platform, site.Platform, StringComparison.OrdinalIgnoreCase)
                && string.Equals(pattern.WorkspaceType, site.WorkspaceType, StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrWhiteSpace(pattern.PrivacyType)
                    || string.Equals(pattern.PrivacyType, site.PrivacyType, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var libraryTemplates = curatedPatterns.Count > 0
            ? curatedPatterns.Select(pattern => (Name: pattern.LibraryName, TemplateType: pattern.TemplateType, Sensitivity: pattern.Sensitivity)).ToList()
            : (string.Equals(site.Platform, "Teams", StringComparison.OrdinalIgnoreCase)
                ? new List<(string Name, string TemplateType, string Sensitivity)>
                {
                    ("Documents", "Documents", "Internal"),
                    ("Shared", "Shared", "Internal"),
                    ("Meeting Notes", "Meeting Notes", "Internal")
                }
                : new List<(string Name, string TemplateType, string Sensitivity)>
                {
                    ("Documents", "Documents", "Internal"),
                    ("Policies", "Policies", site.PrivacyType == "Private" ? "Confidential" : "Internal"),
                    ("Templates", "Templates", "Internal"),
                    ("Projects", "Projects", "Internal")
                });
        var takeCount = curatedPatterns.Count > 0
            ? libraryTemplates.Count
            : string.Equals(site.Platform, "Teams", StringComparison.OrdinalIgnoreCase) ? 2 + _randomSource.Next(0, 2) : 2 + _randomSource.Next(1, 3);
        var libraries = new List<DocumentLibrary>();

        for (var i = 0; i < Math.Min(libraryTemplates.Count, takeCount); i++)
        {
            var template = libraryTemplates[i];
            var library = new DocumentLibrary
            {
                Id = _idFactory.Next("LIB"),
                CompanyId = company.Id,
                CollaborationSiteId = site.Id,
                Name = template.Name,
                TemplateType = template.TemplateType,
                ItemCount = (60 + _randomSource.Next(0, 4000)).ToString(),
                TotalSizeGb = (1 + _randomSource.Next(0, 240)).ToString(),
                Sensitivity = string.IsNullOrWhiteSpace(template.Sensitivity)
                    ? site.PrivacyType == "Private" ? "Confidential" : "Internal"
                    : template.Sensitivity
            };
            libraries.Add(library);
            world.DocumentLibraries.Add(library);

            if (createFolders)
            {
                CreateFoldersForLibrary(world, company, site, library, folderPatterns);
            }
        }

        return libraries;
    }

    private List<CollaborationChannel> CreateChannelsForSite(
        SyntheticEnterpriseWorld world,
        Company company,
        CollaborationSite site,
        IReadOnlyList<CollaborationChannelPatternRule> channelPatterns)
    {
        var channels = channelPatterns
            .Where(pattern =>
                string.Equals(pattern.Platform, site.Platform, StringComparison.OrdinalIgnoreCase)
                && string.Equals(pattern.WorkspaceType, site.WorkspaceType, StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrWhiteSpace(pattern.PrivacyType)
                    || string.Equals(pattern.PrivacyType, site.PrivacyType, StringComparison.OrdinalIgnoreCase)))
            .Select(pattern => (pattern.ChannelName, pattern.ChannelType))
            .ToList();

        if (channels.Count == 0)
        {
            channels = new List<(string Name, string ChannelType)>
            {
                ("General", "Standard"),
                ("Operations", "Standard")
            };

            if (site.Name.Contains("Project", StringComparison.OrdinalIgnoreCase)
                || site.WorkspaceType == "Project")
            {
                channels.Add(("Project-Delivery", "Standard"));
            }

            if (site.PrivacyType == "Private")
            {
                channels.Add(("Leadership", "Private"));
            }
            else
            {
                channels.Add(("Announcements", "Standard"));
            }

            if (_randomSource.NextDouble() < 0.35)
            {
                channels.Add(("Partners", "Shared"));
            }
        }

        var createdChannels = new List<CollaborationChannel>();
        foreach (var channel in channels.Distinct())
        {
            var created = new CollaborationChannel
            {
                Id = _idFactory.Next("CHAN"),
                CompanyId = company.Id,
                CollaborationSiteId = site.Id,
                Name = channel.ChannelName,
                ChannelType = channel.ChannelType,
                MemberCount = (6 + _randomSource.Next(0, 180)).ToString(),
                MessageCount = (100 + _randomSource.Next(0, 12000)).ToString(),
                FileCount = (5 + _randomSource.Next(0, 2200)).ToString()
            };
            createdChannels.Add(created);
            world.CollaborationChannels.Add(created);
        }

        return createdChannels;
    }

    private void CreatePagesForSite(
        SyntheticEnterpriseWorld world,
        Company company,
        CollaborationSite site,
        Person owner,
        IReadOnlyList<DocumentLibrary> libraries,
        IReadOnlyList<SitePagePatternRule> pagePatterns)
    {
        var curatedPages = pagePatterns
            .Where(pattern =>
                string.Equals(pattern.Platform, site.Platform, StringComparison.OrdinalIgnoreCase)
                && string.Equals(pattern.WorkspaceType, site.WorkspaceType, StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrWhiteSpace(pattern.PrivacyType)
                    || string.Equals(pattern.PrivacyType, site.PrivacyType, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var pageTemplates = curatedPages.Count > 0
            ? curatedPages.Select(pattern => (pattern.PageTitle, pattern.PageType, pattern.PromotedState, pattern.AssociatedLibraryName)).ToArray()
            : site.WorkspaceType switch
            {
                "Project" => new[] { ("Home", "Home", "None", string.Empty), ("Project Charter", "Knowledge", "None", string.Empty), ("Status Dashboard", "Landing", "None", string.Empty), ("Weekly Update", "News", "News", string.Empty) },
                "Knowledge" => new[] { ("Home", "Home", "None", string.Empty), ("Knowledge Base", "Knowledge", "None", string.Empty), ("How We Work", "Landing", "None", string.Empty) },
                "Department" => new[] { ("Home", "Home", "None", string.Empty), ("Operations Handbook", "Knowledge", "None", string.Empty), ("Department News", "News", "News", string.Empty) },
                _ => new[] { ("Home", "Home", "None", string.Empty), ("Workspace Guide", "Landing", "None", string.Empty), ("Announcements", "News", "News", string.Empty) }
            };

        var pageCount = curatedPages.Count > 0 ? pageTemplates.Length : Math.Min(pageTemplates.Length, 2 + _randomSource.Next(1, pageTemplates.Length));
        for (var i = 0; i < pageCount; i++)
        {
            var template = pageTemplates[i];
            var associatedLibrary = !string.IsNullOrWhiteSpace(template.Item4)
                ? libraries.FirstOrDefault(library => string.Equals(library.Name, template.Item4, StringComparison.OrdinalIgnoreCase))
                : libraries.Count == 0 ? null : libraries[Math.Min(i, libraries.Count - 1)];
            world.SitePages.Add(new SitePage
            {
                Id = _idFactory.Next("PAGE"),
                CompanyId = company.Id,
                CollaborationSiteId = site.Id,
                Title = $"{site.Name} {template.Item1}",
                PageType = template.Item2,
                AuthorPersonId = owner.Id,
                AssociatedLibraryId = associatedLibrary?.Id,
                ViewCount = (40 + _randomSource.Next(0, 4000)).ToString(),
                LastModified = DateTimeOffset.UtcNow.AddDays(-_randomSource.Next(0, 45)).AddHours(-_randomSource.Next(0, 24)),
                PromotedState = string.IsNullOrWhiteSpace(template.Item3) ? template.Item2 == "News" ? "News" : "None" : template.Item3
            });
        }
    }

    private void CreateFoldersForLibrary(
        SyntheticEnterpriseWorld world,
        Company company,
        CollaborationSite site,
        DocumentLibrary library,
        IReadOnlyList<DocumentFolderPatternRule> folderPatterns)
    {
        var curatedFolders = folderPatterns
            .Where(pattern =>
                string.Equals(pattern.LibraryName, library.Name, StringComparison.OrdinalIgnoreCase)
                && string.Equals(pattern.WorkspaceType, site.WorkspaceType, StringComparison.OrdinalIgnoreCase))
            .OrderBy(pattern => pattern.Depth)
            .ThenBy(pattern => pattern.ParentFolderName)
            .ThenBy(pattern => pattern.FolderName)
            .ToList();
        if (curatedFolders.Count > 0)
        {
            var folderIdsByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pattern in curatedFolders)
            {
                string? parentFolderId = null;
                if (!string.IsNullOrWhiteSpace(pattern.ParentFolderName)
                    && folderIdsByKey.TryGetValue(pattern.ParentFolderName, out var matchedParentId))
                {
                    parentFolderId = matchedParentId;
                }

                var folder = new DocumentFolder
                {
                    Id = _idFactory.Next("FOLDER"),
                    CompanyId = company.Id,
                    DocumentLibraryId = library.Id,
                    ParentFolderId = parentFolderId,
                    Name = pattern.FolderName,
                    FolderType = pattern.FolderType,
                    Depth = pattern.Depth.ToString(),
                    ItemCount = (parentFolderId is null ? 20 : 8 + _randomSource.Next(0, 400)).ToString(),
                    TotalSizeGb = (parentFolderId is null ? 1 + _randomSource.Next(0, 40) : 1 + _randomSource.Next(0, 16)).ToString(),
                    Sensitivity = library.Sensitivity
                };
                world.DocumentFolders.Add(folder);
                folderIdsByKey[pattern.FolderName] = folder.Id;
            }

            return;
        }

        var rootTemplates = library.Name switch
        {
            "Policies" => new[] { "Security", "Human Resources", "Finance" },
            "Templates" => new[] { "Presentations", "Spreadsheets", "Contracts" },
            "Projects" => new[] { "Active", "Archive", "PMO" },
            "Meeting Notes" => new[] { "Steering", "Daily Standup", "Retro" },
            _ => new[] { "Shared", "Working", "Archive" }
        };

        for (var i = 0; i < rootTemplates.Length; i++)
        {
            var rootFolder = new DocumentFolder
            {
                Id = _idFactory.Next("FOLDER"),
                CompanyId = company.Id,
                DocumentLibraryId = library.Id,
                ParentFolderId = null,
                Name = rootTemplates[i],
                FolderType = i == 2 ? "Archive" : site.WorkspaceType,
                Depth = "1",
                ItemCount = (20 + _randomSource.Next(0, 1200)).ToString(),
                TotalSizeGb = (1 + _randomSource.Next(0, 40)).ToString(),
                Sensitivity = library.Sensitivity
            };
            world.DocumentFolders.Add(rootFolder);

            var childCount = 1 + _randomSource.Next(1, 3);
            for (var childIndex = 0; childIndex < childCount; childIndex++)
            {
                world.DocumentFolders.Add(new DocumentFolder
                {
                    Id = _idFactory.Next("FOLDER"),
                    CompanyId = company.Id,
                    DocumentLibraryId = library.Id,
                    ParentFolderId = rootFolder.Id,
                    Name = $"{rootTemplates[i]}-{childIndex + 1:00}",
                    FolderType = childIndex == childCount - 1 ? "ArchiveLeaf" : "Working",
                    Depth = "2",
                    ItemCount = (8 + _randomSource.Next(0, 400)).ToString(),
                    TotalSizeGb = (1 + _randomSource.Next(0, 16)).ToString(),
                    Sensitivity = library.Sensitivity
                });
            }
        }
    }

    private void CreateTabsForChannels(
        SyntheticEnterpriseWorld world,
        Company company,
        CollaborationSite site,
        IReadOnlyList<CollaborationChannel> channels,
        IReadOnlyList<DocumentLibrary> libraries,
        IReadOnlyList<ApplicationRecord> applications,
        CatalogSet catalogs)
    {
        var siteUrl = site.Url.TrimEnd('/');
        var siteApplication = SelectDepartmentApplication(applications, site.OwnerDepartmentId, "Teams", "Portal", "Service", "Dashboard");
        var collaborationTabPatterns = ReadCollaborationTabPatterns(catalogs, company.Industry ?? string.Empty, applications.Count);

        foreach (var channel in channels)
        {
            var defaultTabs = new List<CollaborationChannelTab>
            {
                new()
                {
                    Id = _idFactory.Next("TAB"),
                    CompanyId = company.Id,
                    CollaborationChannelId = channel.Id,
                    Name = "Files",
                    TabType = "DocumentLibrary",
                    TargetType = "DocumentLibrary",
                    TargetId = libraries.FirstOrDefault()?.Id,
                    TargetReference = libraries.FirstOrDefault()?.Name,
                    Vendor = "Microsoft",
                    IsPinned = true
                },
                new()
                {
                    Id = _idFactory.Next("TAB"),
                    CompanyId = company.Id,
                    CollaborationChannelId = channel.Id,
                    Name = "Wiki",
                    TabType = "SharePointPage",
                    TargetType = "CollaborationSite",
                    TargetId = site.Id,
                    TargetReference = $"{siteUrl}/SitePages/Home.aspx",
                    Vendor = "Microsoft",
                    IsPinned = channel.Name == "General"
                }
            };

            var curatedTab = SelectCuratedCollaborationTab(
                collaborationTabPatterns,
                channel.ChannelType,
                site.WorkspaceType,
                applications,
                site.OwnerDepartmentId);

            if (curatedTab?.Application is not null)
            {
                defaultTabs.Add(new CollaborationChannelTab
                {
                    Id = _idFactory.Next("TAB"),
                    CompanyId = company.Id,
                    CollaborationChannelId = channel.Id,
                    Name = string.IsNullOrWhiteSpace(curatedTab.Pattern.PreferredTabName)
                        ? curatedTab.Application.Name
                        : curatedTab.Pattern.PreferredTabName,
                    TabType = string.IsNullOrWhiteSpace(curatedTab.Pattern.TabType) ? "Application" : curatedTab.Pattern.TabType,
                    TargetType = string.IsNullOrWhiteSpace(curatedTab.Pattern.TargetType) ? "Application" : curatedTab.Pattern.TargetType,
                    TargetId = curatedTab.Application.Id,
                    TargetReference = curatedTab.Application.Url ?? $"app://{curatedTab.Application.Id}",
                    Vendor = string.IsNullOrWhiteSpace(curatedTab.Application.Vendor) ? "Internal" : curatedTab.Application.Vendor,
                    IsPinned = curatedTab.Pattern.IsPinned ?? true
                });
            }
            else if (siteApplication is not null)
            {
                defaultTabs.Add(new CollaborationChannelTab
                {
                    Id = _idFactory.Next("TAB"),
                    CompanyId = company.Id,
                    CollaborationChannelId = channel.Id,
                    Name = channel.ChannelType == "Private" ? "Action Tracker" : "Operations Dashboard",
                    TabType = "Application",
                    TargetType = "Application",
                    TargetId = siteApplication.Id,
                    TargetReference = siteApplication.Url ?? $"app://{siteApplication.Id}",
                    Vendor = string.IsNullOrWhiteSpace(siteApplication.Vendor) ? "Internal" : siteApplication.Vendor,
                    IsPinned = true
                });
            }

            if (channel.ChannelType == "Shared")
            {
                defaultTabs.Add(new CollaborationChannelTab
                {
                    Id = _idFactory.Next("TAB"),
                    CompanyId = company.Id,
                    CollaborationChannelId = channel.Id,
                    Name = "Partner Workspace",
                    TabType = "Website",
                    TargetType = "ExternalUrl",
                    TargetReference = $"{siteUrl}/partners/{Slug(channel.Name)}",
                    Vendor = "Partner Portal",
                    IsPinned = true
                });
            }

            foreach (var tab in defaultTabs.Where(tab => !string.IsNullOrWhiteSpace(tab.TargetReference)))
            {
                world.CollaborationChannelTabs.Add(tab);
            }
        }
    }

    private void CreateAccessGrants(
        SyntheticEnterpriseWorld world,
        Company company,
        IReadOnlyList<Department> departments,
        IReadOnlyList<DirectoryGroup> groups,
        IReadOnlyList<DirectoryAccount> accounts)
    {
        foreach (var db in world.Databases.Where(d => d.CompanyId == company.Id))
        {
            var group = FindDepartmentGroup(groups, departments, db.OwnerDepartmentId)
                ?? groups.FirstOrDefault(g => g.CompanyId == company.Id && g.Name == "SG-AllEmployees");

            if (group is null)
            {
                continue;
            }

            var grant = new RepositoryAccessGrant
            {
                Id = _idFactory.Next("RAG"),
                RepositoryId = db.Id,
                RepositoryType = "Database",
                PrincipalObjectId = group.Id,
                PrincipalType = "Group",
                AccessLevel = "Modify"
            };
            world.RepositoryAccessGrants.Add(grant);
            AddRepositoryAccessEvidence(world, company.Id, grant, "DbDataReader", "DatabaseAcl");
            AddRepositoryAccessEvidence(world, company.Id, grant, "DbDataWriter", "DatabaseAcl");
        }

        foreach (var share in world.FileShares.Where(s => s.CompanyId == company.Id))
        {
            var broadEmployeeGroup = groups.FirstOrDefault(g => g.CompanyId == company.Id && g.Name == "SG-AllEmployees");
            var serverAdminGroup = groups.FirstOrDefault(g => g.CompanyId == company.Id && g.Name == "SG-Tier1-ServerAdmins");

            if (!string.IsNullOrWhiteSpace(share.OwnerPersonId))
            {
                var account = accounts.FirstOrDefault(a => string.Equals(a.PersonId, share.OwnerPersonId, StringComparison.OrdinalIgnoreCase)
                                                           && string.Equals(a.AccountType, "User", StringComparison.OrdinalIgnoreCase));
                if (account is not null)
                {
                    var ownerGrant = new RepositoryAccessGrant
                    {
                        Id = _idFactory.Next("RAG"),
                        RepositoryId = share.Id,
                        RepositoryType = "FileShare",
                        PrincipalObjectId = account.Id,
                        PrincipalType = "Account",
                        AccessLevel = "FullControl"
                    };
                    world.RepositoryAccessGrants.Add(ownerGrant);
                    AddRepositoryAccessEvidence(world, company.Id, ownerGrant, "ShareFullControl", "SMB");
                    AddRepositoryAccessEvidence(world, company.Id, ownerGrant, "NtfsFullControl", "NTFS");
                }

                if (broadEmployeeGroup is not null)
                {
                    AddAccessControlEvidence(
                        world,
                        company.Id,
                        broadEmployeeGroup.Id,
                        "Group",
                        "FileShare",
                        share.Id,
                        "ShareRead",
                        "Deny",
                        false,
                        "SMB",
                        notes: "Personal share with unique permissions excluding broad employee access");
                    AddAccessControlEvidence(
                        world,
                        company.Id,
                        broadEmployeeGroup.Id,
                        "Group",
                        "FileShare",
                        share.Id,
                        "NtfsRead",
                        "Deny",
                        false,
                        "NTFS",
                        notes: "Personal share with explicit NTFS deny for broad employee group");
                }

                if (serverAdminGroup is not null)
                {
                    AddAccessControlEvidence(
                        world,
                        company.Id,
                        serverAdminGroup.Id,
                        "Group",
                        "FileShare",
                        share.Id,
                        "ShareFullControl",
                        "Allow",
                        false,
                        "SMB",
                        notes: "Operational file-server admin override");
                    AddAccessControlEvidence(
                        world,
                        company.Id,
                        serverAdminGroup.Id,
                        "Group",
                        "FileShare",
                        share.Id,
                        "NtfsFullControl",
                        "Allow",
                        false,
                        "NTFS",
                        notes: "Operational file-server admin override");
                }

                continue;
            }

            var group = FindDepartmentGroup(groups, departments, share.OwnerDepartmentId)
                ?? groups.FirstOrDefault(g => g.CompanyId == company.Id && g.Name == "SG-AllEmployees");

            if (group is null)
            {
                continue;
            }

            var grant = new RepositoryAccessGrant
            {
                Id = _idFactory.Next("RAG"),
                RepositoryId = share.Id,
                RepositoryType = "FileShare",
                PrincipalObjectId = group.Id,
                PrincipalType = "Group",
                AccessLevel = share.AccessModel == "Mixed" ? "FullControl" : "Modify"
            };
            world.RepositoryAccessGrants.Add(grant);
            if (string.Equals(grant.AccessLevel, "FullControl", StringComparison.OrdinalIgnoreCase))
            {
                AddRepositoryAccessEvidence(world, company.Id, grant, "ShareFullControl", "SMB");
                AddRepositoryAccessEvidence(world, company.Id, grant, "NtfsFullControl", "NTFS");
            }
            else
            {
                AddRepositoryAccessEvidence(world, company.Id, grant, "ShareChange", "SMB");
                AddRepositoryAccessEvidence(world, company.Id, grant, "NtfsModify", "NTFS");
            }

            if (serverAdminGroup is not null)
            {
                AddAccessControlEvidence(
                    world,
                    company.Id,
                    serverAdminGroup.Id,
                    "Group",
                    "FileShare",
                    share.Id,
                    "ShareFullControl",
                    "Allow",
                    false,
                    "SMB",
                    notes: "Operational file-server admin override on departmental share");
                AddAccessControlEvidence(
                    world,
                    company.Id,
                    serverAdminGroup.Id,
                    "Group",
                    "FileShare",
                    share.Id,
                    "NtfsFullControl",
                    "Allow",
                    false,
                    "NTFS",
                    notes: "Operational file-server admin override on departmental share");
            }

            if (string.Equals(share.Sensitivity, "Confidential", StringComparison.OrdinalIgnoreCase)
                && broadEmployeeGroup is not null
                && !string.Equals(broadEmployeeGroup.Id, group.Id, StringComparison.OrdinalIgnoreCase))
            {
                AddAccessControlEvidence(
                    world,
                    company.Id,
                    broadEmployeeGroup.Id,
                    "Group",
                    "FileShare",
                    share.Id,
                    "ShareRead",
                    "Deny",
                    false,
                    "SMB",
                    notes: "Confidential departmental share with deny for broad employee group");
                AddAccessControlEvidence(
                    world,
                    company.Id,
                    broadEmployeeGroup.Id,
                    "Group",
                    "FileShare",
                    share.Id,
                    "NtfsRead",
                    "Deny",
                    false,
                    "NTFS",
                    notes: "Confidential departmental share with explicit NTFS deny");
            }
        }

        foreach (var site in world.CollaborationSites.Where(s => s.CompanyId == company.Id))
        {
            var group = FindDepartmentGroup(groups, departments, site.OwnerDepartmentId)
                ?? groups.FirstOrDefault(g => g.CompanyId == company.Id && g.Name == "M365-AllEmployees");

            if (group is null)
            {
                continue;
            }

            var grant = new RepositoryAccessGrant
            {
                Id = _idFactory.Next("RAG"),
                RepositoryId = site.Id,
                RepositoryType = "CollaborationSite",
                PrincipalObjectId = group.Id,
                PrincipalType = "Group",
                AccessLevel = site.PrivacyType == "Public" ? "Read" : "Member"
            };
            world.RepositoryAccessGrants.Add(grant);
            AddRepositoryAccessEvidence(
                world,
                company.Id,
                grant,
                site.PrivacyType == "Public" ? "SiteRead" : "SiteMember",
                "SharePoint");

            var ownerAccount = accounts.FirstOrDefault(account =>
                string.Equals(account.PersonId, site.OwnerPersonId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(account.AccountType, "User", StringComparison.OrdinalIgnoreCase));
            if (ownerAccount is not null)
            {
                AddAccessControlEvidence(
                    world,
                    company.Id,
                    ownerAccount.Id,
                    "Account",
                    "CollaborationSite",
                    site.Id,
                    "SiteCollectionAdmin",
                    "Allow",
                    false,
                    "SharePoint",
                    notes: "Direct site collection administration for site owner");
            }

            if (!string.Equals(site.PrivacyType, "Public", StringComparison.OrdinalIgnoreCase))
            {
                var broadCollaborationGroup = groups.FirstOrDefault(g => g.CompanyId == company.Id && g.Name == "M365-AllEmployees");
                if (broadCollaborationGroup is not null && !string.Equals(broadCollaborationGroup.Id, group.Id, StringComparison.OrdinalIgnoreCase))
                {
                    AddAccessControlEvidence(
                        world,
                        company.Id,
                        broadCollaborationGroup.Id,
                        "Group",
                        "CollaborationSite",
                        site.Id,
                        "SiteRead",
                        "Deny",
                        false,
                        "SharePoint",
                        notes: "Private site with unique permissions blocking broad tenant-wide readers");
                }
            }
        }

        foreach (var channel in world.CollaborationChannels.Where(c => c.CompanyId == company.Id))
        {
            var site = world.CollaborationSites.FirstOrDefault(candidate => candidate.Id == channel.CollaborationSiteId);
            if (site is null)
            {
                continue;
            }

            var group = FindDepartmentGroup(groups, departments, site.OwnerDepartmentId)
                ?? groups.FirstOrDefault(g => g.CompanyId == company.Id && g.Name == "M365-AllEmployees");

            if (group is null)
            {
                continue;
            }

            var grant = new RepositoryAccessGrant
            {
                Id = _idFactory.Next("RAG"),
                RepositoryId = channel.Id,
                RepositoryType = "CollaborationChannel",
                PrincipalObjectId = group.Id,
                PrincipalType = "Group",
                AccessLevel = channel.ChannelType == "Private" ? "Owner" : "Member"
            };
            world.RepositoryAccessGrants.Add(grant);
            AddRepositoryAccessEvidence(
                world,
                company.Id,
                grant,
                channel.ChannelType == "Private" ? "ChannelOwner" : "ChannelMember",
                "Teams",
                isInherited: string.Equals(channel.ChannelType, "Standard", StringComparison.OrdinalIgnoreCase),
                inheritanceSourceId: string.Equals(channel.ChannelType, "Standard", StringComparison.OrdinalIgnoreCase) ? site.Id : null);

            if (!string.Equals(channel.ChannelType, "Standard", StringComparison.OrdinalIgnoreCase))
            {
                var broadCollaborationGroup = groups.FirstOrDefault(g => g.CompanyId == company.Id && g.Name == "M365-AllEmployees");
                if (broadCollaborationGroup is not null && !string.Equals(broadCollaborationGroup.Id, group.Id, StringComparison.OrdinalIgnoreCase))
                {
                    AddAccessControlEvidence(
                        world,
                        company.Id,
                        broadCollaborationGroup.Id,
                        "Group",
                        "CollaborationChannel",
                        channel.Id,
                        "ChannelMember",
                        "Deny",
                        false,
                        "Teams",
                        notes: $"Explicit exception for {channel.ChannelType.ToLowerInvariant()} channel membership");
                }
            }
        }

        foreach (var library in world.DocumentLibraries.Where(l => l.CompanyId == company.Id))
        {
            var site = world.CollaborationSites.FirstOrDefault(candidate => candidate.Id == library.CollaborationSiteId);
            if (site is null)
            {
                continue;
            }

            var group = FindDepartmentGroup(groups, departments, site.OwnerDepartmentId)
                ?? groups.FirstOrDefault(g => g.CompanyId == company.Id && g.Name == "M365-AllEmployees");

            if (group is null)
            {
                continue;
            }

            var grant = new RepositoryAccessGrant
            {
                Id = _idFactory.Next("RAG"),
                RepositoryId = library.Id,
                RepositoryType = "DocumentLibrary",
                PrincipalObjectId = group.Id,
                PrincipalType = "Group",
                AccessLevel = site.PrivacyType == "Public" ? "Read" : "Contribute"
            };
            world.RepositoryAccessGrants.Add(grant);
            AddRepositoryAccessEvidence(
                world,
                company.Id,
                grant,
                site.PrivacyType == "Public" ? "LibraryRead" : "LibraryContribute",
                "SharePoint",
                isInherited: true,
                inheritanceSourceId: site.Id);

            if (string.Equals(library.Sensitivity, "Confidential", StringComparison.OrdinalIgnoreCase))
            {
                var ownerAccount = accounts.FirstOrDefault(account =>
                    string.Equals(account.PersonId, site.OwnerPersonId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(account.AccountType, "User", StringComparison.OrdinalIgnoreCase));
                if (ownerAccount is not null)
                {
                    AddAccessControlEvidence(
                        world,
                        company.Id,
                        ownerAccount.Id,
                        "Account",
                        "DocumentLibrary",
                        library.Id,
                        "LibraryFullControl",
                        "Allow",
                        false,
                        "SharePoint",
                        notes: "Confidential library with direct owner full-control exception");
                }
            }
        }

        foreach (var folder in world.DocumentFolders.Where(f => f.CompanyId == company.Id))
        {
            var library = world.DocumentLibraries.FirstOrDefault(candidate => candidate.Id == folder.DocumentLibraryId);
            if (library is null)
            {
                continue;
            }

            var site = world.CollaborationSites.FirstOrDefault(candidate => candidate.Id == library.CollaborationSiteId);
            if (site is null)
            {
                continue;
            }

            var group = FindDepartmentGroup(groups, departments, site.OwnerDepartmentId)
                ?? groups.FirstOrDefault(g => g.CompanyId == company.Id && g.Name == "M365-AllEmployees");
            if (group is null)
            {
                continue;
            }

            var grant = new RepositoryAccessGrant
            {
                Id = _idFactory.Next("RAG"),
                RepositoryId = folder.Id,
                RepositoryType = "DocumentFolder",
                PrincipalObjectId = group.Id,
                PrincipalType = "Group",
                AccessLevel = folder.Sensitivity == "Confidential" ? "Modify" : "Read"
            };
            world.RepositoryAccessGrants.Add(grant);
            if (string.Equals(folder.Sensitivity, "Confidential", StringComparison.OrdinalIgnoreCase))
            {
                AddRepositoryAccessEvidence(
                    world,
                    company.Id,
                    grant,
                    "FolderRead",
                    "SharePoint",
                    isInherited: true,
                    inheritanceSourceId: folder.ParentFolderId ?? library.Id,
                    notes: "Inherited baseline access from parent library");
                AddRepositoryAccessEvidence(
                    world,
                    company.Id,
                    grant,
                    "FolderModify",
                    "SharePoint",
                    isInherited: false,
                    notes: "Direct exception granting elevated access to confidential folder");

                var broadCollaborationGroup = groups.FirstOrDefault(g => g.CompanyId == company.Id && g.Name == "M365-AllEmployees");
                if (broadCollaborationGroup is not null && !string.Equals(broadCollaborationGroup.Id, group.Id, StringComparison.OrdinalIgnoreCase))
                {
                    AddAccessControlEvidence(
                        world,
                        company.Id,
                        broadCollaborationGroup.Id,
                        "Group",
                        "DocumentFolder",
                        folder.Id,
                        "FolderRead",
                        "Deny",
                        false,
                        "SharePoint",
                        notes: "Confidential folder with explicit deny for broad collaboration group");
                }
            }
            else
            {
                AddRepositoryAccessEvidence(
                    world,
                    company.Id,
                    grant,
                    "FolderRead",
                    "SharePoint",
                    isInherited: true,
                    inheritanceSourceId: folder.ParentFolderId ?? library.Id);
            }
        }

        foreach (var page in world.SitePages.Where(p => p.CompanyId == company.Id))
        {
            var site = world.CollaborationSites.FirstOrDefault(candidate => candidate.Id == page.CollaborationSiteId);
            if (site is null)
            {
                continue;
            }

            var group = FindDepartmentGroup(groups, departments, site.OwnerDepartmentId)
                ?? groups.FirstOrDefault(g => g.CompanyId == company.Id && g.Name == "M365-AllEmployees");
            if (group is null)
            {
                continue;
            }

            var grant = new RepositoryAccessGrant
            {
                Id = _idFactory.Next("RAG"),
                RepositoryId = page.Id,
                RepositoryType = "SitePage",
                PrincipalObjectId = group.Id,
                PrincipalType = "Group",
                AccessLevel = page.PageType == "News" ? "Read" : "Contribute"
            };
            world.RepositoryAccessGrants.Add(grant);
            AddRepositoryAccessEvidence(
                world,
                company.Id,
                grant,
                page.PageType == "News" ? "PageRead" : "PageEdit",
                "SharePoint",
                isInherited: true,
                inheritanceSourceId: site.Id);

            var ownerAccount = accounts.FirstOrDefault(account =>
                string.Equals(account.PersonId, site.OwnerPersonId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(account.AccountType, "User", StringComparison.OrdinalIgnoreCase));
            if (ownerAccount is not null && string.Equals(page.PageType, "Knowledge", StringComparison.OrdinalIgnoreCase))
            {
                AddAccessControlEvidence(
                    world,
                    company.Id,
                    ownerAccount.Id,
                    "Account",
                    "SitePage",
                    page.Id,
                    "PageEdit",
                    "Allow",
                    false,
                    "SharePoint",
                    notes: "Knowledge page with direct owner edit rights beyond inherited members");
            }
        }
    }

    private void AddRepositoryAccessEvidence(
        SyntheticEnterpriseWorld world,
        string companyId,
        RepositoryAccessGrant grant,
        string rightName,
        string sourceSystem,
        string accessType = "Allow",
        bool isInherited = false,
        string? inheritanceSourceId = null,
        string? notes = null)
    {
        if (world.AccessControlEvidence.Any(evidence =>
                evidence.CompanyId == companyId
                && string.Equals(evidence.PrincipalObjectId, grant.PrincipalObjectId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(evidence.TargetType, grant.RepositoryType, StringComparison.OrdinalIgnoreCase)
                && string.Equals(evidence.TargetId, grant.RepositoryId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(evidence.RightName, rightName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(evidence.AccessType, accessType, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        world.AccessControlEvidence.Add(new AccessControlEvidenceRecord
        {
            Id = _idFactory.Next("ACE"),
            CompanyId = companyId,
            PrincipalObjectId = grant.PrincipalObjectId,
            PrincipalType = grant.PrincipalType,
            TargetType = grant.RepositoryType,
            TargetId = grant.RepositoryId,
            RightName = rightName,
            AccessType = accessType,
            IsInherited = isInherited,
            IsDefaultEntry = false,
            SourceSystem = sourceSystem,
            InheritanceSourceId = inheritanceSourceId,
            Notes = notes
        });
    }

    private void AddAccessControlEvidence(
        SyntheticEnterpriseWorld world,
        string companyId,
        string? principalObjectId,
        string principalType,
        string targetType,
        string? targetId,
        string rightName,
        string accessType,
        bool isInherited,
        string sourceSystem,
        string? inheritanceSourceId = null,
        string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(principalObjectId) || string.IsNullOrWhiteSpace(targetId))
        {
            return;
        }

        if (world.AccessControlEvidence.Any(evidence =>
                evidence.CompanyId == companyId
                && string.Equals(evidence.PrincipalObjectId, principalObjectId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(evidence.TargetType, targetType, StringComparison.OrdinalIgnoreCase)
                && string.Equals(evidence.TargetId, targetId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(evidence.RightName, rightName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(evidence.AccessType, accessType, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        world.AccessControlEvidence.Add(new AccessControlEvidenceRecord
        {
            Id = _idFactory.Next("ACE"),
            CompanyId = companyId,
            PrincipalObjectId = principalObjectId,
            PrincipalType = principalType,
            TargetType = targetType,
            TargetId = targetId,
            RightName = rightName,
            AccessType = accessType,
            IsInherited = isInherited,
            IsDefaultEntry = false,
            SourceSystem = sourceSystem,
            InheritanceSourceId = inheritanceSourceId,
            Notes = notes
        });
    }

    private static DirectoryGroup? FindDepartmentGroup(
        IReadOnlyList<DirectoryGroup> groups,
        IReadOnlyList<Department> departments,
        string departmentId)
    {
        var dept = departments.FirstOrDefault(d => d.Id == departmentId);
        if (dept is null)
        {
            return null;
        }

        var expected = $"SG-{Slug(dept.Name)}-Users";
        return groups.FirstOrDefault(g => string.Equals(g.Name, expected, StringComparison.OrdinalIgnoreCase));
    }

    private static string Slug(string value)
        => new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private static ApplicationRecord? SelectDepartmentApplication(
        IReadOnlyList<ApplicationRecord> applications,
        string departmentId,
        params string[] preferredNameFragments)
    {
        var departmentApps = applications
            .Where(application => application.OwnerDepartmentId == departmentId)
            .ToList();

        foreach (var fragment in preferredNameFragments)
        {
            var match = departmentApps.FirstOrDefault(application =>
                application.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return departmentApps.FirstOrDefault()
            ?? applications.FirstOrDefault(application => application.Criticality == "High")
            ?? applications.FirstOrDefault();
    }

    private void AddApplicationRepositoryLink(
        SyntheticEnterpriseWorld world,
        Company company,
        ApplicationRepositorySelection? selection,
        string repositoryId,
        string repositoryType,
        string defaultRelationshipType)
    {
        if (selection?.Application is null)
        {
            return;
        }

        world.ApplicationRepositoryLinks.Add(new ApplicationRepositoryLink
        {
            Id = _idFactory.Next("ARL"),
            CompanyId = company.Id,
            ApplicationId = selection.Application.Id,
            RepositoryId = repositoryId,
            RepositoryType = repositoryType,
            RelationshipType = string.IsNullOrWhiteSpace(selection.Pattern?.RelationshipType) ? defaultRelationshipType : selection.Pattern.RelationshipType,
            Criticality = string.IsNullOrWhiteSpace(selection.Pattern?.Criticality) ? selection.Application.Criticality : selection.Pattern.Criticality
        });
    }

    private static ApplicationRepositorySelection? SelectApplicationForRepository(
        IReadOnlyList<ApplicationRecord> applications,
        IReadOnlyList<ApplicationRepositoryPatternRule> patterns,
        string repositoryType,
        string departmentName,
        string? sharePurpose,
        string? workspaceType,
        params string[] fallbackPreferredNameFragments)
        => SelectApplicationForRepository(
            applications,
            patterns,
            repositoryType,
            departmentName,
            sharePurpose,
            workspaceType,
            fallbackPreferredNameFragments,
            null);

    private static ApplicationRepositorySelection? SelectApplicationForRepository(
        IReadOnlyList<ApplicationRecord> applications,
        IReadOnlyList<ApplicationRepositoryPatternRule> patterns,
        string repositoryType,
        string departmentName,
        string? sharePurpose,
        string? workspaceType,
        IEnumerable<string> fallbackPreferredNameFragments,
        string? departmentId)
    {
        foreach (var pattern in patterns.Where(pattern =>
                     string.Equals(pattern.RepositoryType, repositoryType, StringComparison.OrdinalIgnoreCase)
                     && (string.IsNullOrWhiteSpace(pattern.MatchDepartmentNameContains)
                         || departmentName.Contains(pattern.MatchDepartmentNameContains, StringComparison.OrdinalIgnoreCase))
                     && (string.IsNullOrWhiteSpace(pattern.MatchSharePurpose)
                         || string.Equals(sharePurpose, pattern.MatchSharePurpose, StringComparison.OrdinalIgnoreCase))
                     && (string.IsNullOrWhiteSpace(pattern.MatchWorkspaceType)
                         || string.Equals(workspaceType, pattern.MatchWorkspaceType, StringComparison.OrdinalIgnoreCase))))
        {
            var match = applications.FirstOrDefault(application =>
                (string.IsNullOrWhiteSpace(pattern.MatchApplicationNameContains)
                 || application.Name.Contains(pattern.MatchApplicationNameContains, StringComparison.OrdinalIgnoreCase))
                && (string.IsNullOrWhiteSpace(pattern.MatchVendor)
                    || string.Equals(application.Vendor, pattern.MatchVendor, StringComparison.OrdinalIgnoreCase))
                && (string.IsNullOrWhiteSpace(pattern.MatchCategory)
                    || string.Equals(application.Category, pattern.MatchCategory, StringComparison.OrdinalIgnoreCase))
                && (string.IsNullOrWhiteSpace(departmentId) || application.OwnerDepartmentId == departmentId || application.OwnerDepartmentId == string.Empty));

            if (match is not null)
            {
                return new ApplicationRepositorySelection(match, pattern);
            }
        }

        var fallback = departmentId is not null
            ? SelectDepartmentApplication(applications, departmentId, fallbackPreferredNameFragments.ToArray())
            : applications.FirstOrDefault(application =>
                    fallbackPreferredNameFragments.Any(fragment => application.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
              ?? applications.FirstOrDefault(application => application.Criticality == "High")
              ?? applications.FirstOrDefault();

        return fallback is null ? null : new ApplicationRepositorySelection(fallback, null);
    }

    private static string DetermineSharePurpose(string shareName, int index)
    {
        if (shareName.Contains("archive", StringComparison.OrdinalIgnoreCase))
        {
            return "DepartmentArchive";
        }

        if (shareName.Contains("drop", StringComparison.OrdinalIgnoreCase))
        {
            return "DepartmentDrop";
        }

        return index % 4 == 0 ? "DepartmentDrop" : "Department";
    }

    private static string DeterminePrivacyType(string? accessModel, int index)
    {
        if (string.Equals(accessModel, "Public", StringComparison.OrdinalIgnoreCase))
        {
            return "Public";
        }

        if (string.Equals(accessModel, "Private", StringComparison.OrdinalIgnoreCase))
        {
            return "Private";
        }

        return index % 4 == 0 ? "Public" : "Private";
    }

    private static CuratedCollaborationTabSelection? SelectCuratedCollaborationTab(
        IReadOnlyList<CollaborationTabPatternRule> patterns,
        string channelType,
        string workspaceType,
        IReadOnlyList<ApplicationRecord> applications,
        string ownerDepartmentId)
    {
        foreach (var pattern in patterns.Where(pattern =>
                     string.Equals(pattern.ChannelType, channelType, StringComparison.OrdinalIgnoreCase)
                     && string.Equals(pattern.WorkspaceType, workspaceType, StringComparison.OrdinalIgnoreCase)))
        {
            var match = applications.FirstOrDefault(application =>
                (string.IsNullOrWhiteSpace(pattern.MatchApplicationNameContains)
                 || application.Name.Contains(pattern.MatchApplicationNameContains, StringComparison.OrdinalIgnoreCase))
                && (string.IsNullOrWhiteSpace(pattern.MatchVendor)
                    || string.Equals(application.Vendor, pattern.MatchVendor, StringComparison.OrdinalIgnoreCase))
                && (string.IsNullOrWhiteSpace(pattern.MatchCategory)
                    || string.Equals(application.Category, pattern.MatchCategory, StringComparison.OrdinalIgnoreCase))
                && (application.OwnerDepartmentId == ownerDepartmentId || string.IsNullOrWhiteSpace(ownerDepartmentId)));

            match ??= applications.FirstOrDefault(application =>
                (string.IsNullOrWhiteSpace(pattern.MatchApplicationNameContains)
                 || application.Name.Contains(pattern.MatchApplicationNameContains, StringComparison.OrdinalIgnoreCase))
                && (string.IsNullOrWhiteSpace(pattern.MatchVendor)
                    || string.Equals(application.Vendor, pattern.MatchVendor, StringComparison.OrdinalIgnoreCase))
                && (string.IsNullOrWhiteSpace(pattern.MatchCategory)
                    || string.Equals(application.Category, pattern.MatchCategory, StringComparison.OrdinalIgnoreCase)));

            if (match is not null)
            {
                return new CuratedCollaborationTabSelection(match, pattern);
            }
        }

        return null;
    }

    private static List<RepositoryPatternRule> ReadRepositoryPatterns(CatalogSet catalogs, string type)
    {
        if (!catalogs.CsvCatalogs.TryGetValue("repository_patterns", out var rows))
        {
            return new List<RepositoryPatternRule>();
        }

        return rows
            .Where(row => string.Equals(Read(row, "Type"), type, StringComparison.OrdinalIgnoreCase))
            .Select(row => new RepositoryPatternRule(
                Read(row, "Type"),
                Read(row, "Pattern"),
                Read(row, "OwnerHint"),
                Read(row, "AccessModel")))
            .Where(rule => !string.IsNullOrWhiteSpace(rule.Pattern))
            .ToList();
    }

    private static List<ApplicationRepositoryPatternRule> ReadApplicationRepositoryPatterns(
        CatalogSet catalogs,
        string industry,
        int employeeCount)
    {
        if (!catalogs.CsvCatalogs.TryGetValue("application_repository_patterns", out var rows))
        {
            return new List<ApplicationRepositoryPatternRule>();
        }

        var industryTokens = BuildIndustryTokens(industry);
        return rows
            .Select(row => new ApplicationRepositoryPatternRule(
                Read(row, "RepositoryType"),
                Read(row, "MatchApplicationNameContains"),
                Read(row, "MatchVendor"),
                Read(row, "MatchCategory"),
                Read(row, "MatchDepartmentNameContains"),
                Read(row, "MatchSharePurpose"),
                Read(row, "MatchWorkspaceType"),
                SplitPipe(Read(row, "IndustryTags")),
                int.TryParse(Read(row, "MinimumEmployees"), out var minimumEmployees) ? minimumEmployees : 0,
                Read(row, "RelationshipType"),
                Read(row, "Criticality")))
            .Where(rule => !string.IsNullOrWhiteSpace(rule.RepositoryType))
            .Where(rule => rule.MinimumEmployees <= Math.Max(1, employeeCount))
            .Where(rule => rule.IndustryTags.Count == 0
                           || rule.IndustryTags.Contains("All", StringComparer.OrdinalIgnoreCase)
                           || rule.IndustryTags.Any(tag => industryTokens.Contains(tag)))
            .OrderByDescending(GetPatternSpecificity)
            .ToList();
    }

    private List<CollaborationTabPatternRule> ReadCollaborationTabPatterns(
        CatalogSet catalogs,
        string industry,
        int applicationCount)
    {
        if (!catalogs.CsvCatalogs.TryGetValue("application_collaboration_tab_patterns", out var rows))
        {
            return new List<CollaborationTabPatternRule>();
        }

        var industryTokens = BuildIndustryTokens(industry);
        return rows
            .Select(row => new CollaborationTabPatternRule(
                Read(row, "ChannelType"),
                Read(row, "WorkspaceType"),
                Read(row, "MatchApplicationNameContains"),
                Read(row, "MatchVendor"),
                Read(row, "MatchCategory"),
                Read(row, "PreferredTabName"),
                Read(row, "TabType"),
                Read(row, "TargetType"),
                SplitPipe(Read(row, "IndustryTags")),
                int.TryParse(Read(row, "MinimumEmployees"), out var minimumEmployees) ? minimumEmployees : 0,
                bool.TryParse(Read(row, "IsPinned"), out var isPinned) ? isPinned : null))
            .Where(rule => !string.IsNullOrWhiteSpace(rule.ChannelType) && !string.IsNullOrWhiteSpace(rule.WorkspaceType))
            .Where(rule => rule.MinimumEmployees <= Math.Max(1, applicationCount))
            .Where(rule => rule.IndustryTags.Count == 0
                           || rule.IndustryTags.Contains("All", StringComparer.OrdinalIgnoreCase)
                           || rule.IndustryTags.Any(tag => industryTokens.Contains(tag)))
            .OrderByDescending(GetPatternSpecificity)
            .ToList();
    }

    private List<CollaborationChannelPatternRule> ReadCollaborationChannelPatterns(
        CatalogSet catalogs,
        string industry,
        int employeeCount)
    {
        if (!catalogs.CsvCatalogs.TryGetValue("collaboration_channel_patterns", out var rows))
        {
            return new List<CollaborationChannelPatternRule>();
        }

        var industryTokens = BuildIndustryTokens(industry);
        return rows
            .Select(row => new CollaborationChannelPatternRule(
                Read(row, "Platform"),
                Read(row, "WorkspaceType"),
                Read(row, "PrivacyType"),
                Read(row, "ChannelName"),
                Read(row, "ChannelType"),
                SplitPipe(Read(row, "IndustryTags")),
                int.TryParse(Read(row, "MinimumEmployees"), out var minimumEmployees) ? minimumEmployees : 0))
            .Where(rule => !string.IsNullOrWhiteSpace(rule.Platform)
                           && !string.IsNullOrWhiteSpace(rule.WorkspaceType)
                           && !string.IsNullOrWhiteSpace(rule.ChannelName))
            .Where(rule => rule.MinimumEmployees <= Math.Max(1, employeeCount))
            .Where(rule => rule.IndustryTags.Count == 0
                           || rule.IndustryTags.Contains("All", StringComparer.OrdinalIgnoreCase)
                           || rule.IndustryTags.Any(tag => industryTokens.Contains(tag)))
            .ToList();
    }

    private List<DocumentLibraryPatternRule> ReadDocumentLibraryPatterns(
        CatalogSet catalogs,
        string industry,
        int employeeCount)
    {
        if (!catalogs.CsvCatalogs.TryGetValue("document_library_patterns", out var rows))
        {
            return new List<DocumentLibraryPatternRule>();
        }

        var industryTokens = BuildIndustryTokens(industry);
        return rows
            .Select(row => new DocumentLibraryPatternRule(
                Read(row, "Platform"),
                Read(row, "WorkspaceType"),
                Read(row, "PrivacyType"),
                Read(row, "LibraryName"),
                Read(row, "TemplateType"),
                Read(row, "Sensitivity"),
                SplitPipe(Read(row, "IndustryTags")),
                int.TryParse(Read(row, "MinimumEmployees"), out var minimumEmployees) ? minimumEmployees : 0))
            .Where(rule => !string.IsNullOrWhiteSpace(rule.Platform)
                           && !string.IsNullOrWhiteSpace(rule.WorkspaceType)
                           && !string.IsNullOrWhiteSpace(rule.LibraryName))
            .Where(rule => rule.MinimumEmployees <= Math.Max(1, employeeCount))
            .Where(rule => rule.IndustryTags.Count == 0
                           || rule.IndustryTags.Contains("All", StringComparer.OrdinalIgnoreCase)
                           || rule.IndustryTags.Any(tag => industryTokens.Contains(tag)))
            .ToList();
    }

    private List<SitePagePatternRule> ReadSitePagePatterns(
        CatalogSet catalogs,
        string industry,
        int employeeCount)
    {
        if (!catalogs.CsvCatalogs.TryGetValue("site_page_patterns", out var rows))
        {
            return new List<SitePagePatternRule>();
        }

        var industryTokens = BuildIndustryTokens(industry);
        return rows
            .Select(row => new SitePagePatternRule(
                Read(row, "Platform"),
                Read(row, "WorkspaceType"),
                Read(row, "PrivacyType"),
                Read(row, "PageTitle"),
                Read(row, "PageType"),
                Read(row, "PromotedState"),
                Read(row, "AssociatedLibraryName"),
                SplitPipe(Read(row, "IndustryTags")),
                int.TryParse(Read(row, "MinimumEmployees"), out var minimumEmployees) ? minimumEmployees : 0))
            .Where(rule => !string.IsNullOrWhiteSpace(rule.Platform)
                           && !string.IsNullOrWhiteSpace(rule.WorkspaceType)
                           && !string.IsNullOrWhiteSpace(rule.PageTitle))
            .Where(rule => rule.MinimumEmployees <= Math.Max(1, employeeCount))
            .Where(rule => rule.IndustryTags.Count == 0
                           || rule.IndustryTags.Contains("All", StringComparer.OrdinalIgnoreCase)
                           || rule.IndustryTags.Any(tag => industryTokens.Contains(tag)))
            .ToList();
    }

    private List<DocumentFolderPatternRule> ReadDocumentFolderPatterns(
        CatalogSet catalogs,
        string industry,
        int employeeCount)
    {
        if (!catalogs.CsvCatalogs.TryGetValue("document_folder_patterns", out var rows))
        {
            return new List<DocumentFolderPatternRule>();
        }

        var industryTokens = BuildIndustryTokens(industry);
        return rows
            .Select(row => new DocumentFolderPatternRule(
                Read(row, "LibraryName"),
                Read(row, "WorkspaceType"),
                Read(row, "ParentFolderName"),
                Read(row, "FolderName"),
                Read(row, "FolderType"),
                int.TryParse(Read(row, "Depth"), out var depth) ? depth : 1,
                SplitPipe(Read(row, "IndustryTags")),
                int.TryParse(Read(row, "MinimumEmployees"), out var minimumEmployees) ? minimumEmployees : 0))
            .Where(rule => !string.IsNullOrWhiteSpace(rule.LibraryName)
                           && !string.IsNullOrWhiteSpace(rule.WorkspaceType)
                           && !string.IsNullOrWhiteSpace(rule.FolderName))
            .Where(rule => rule.MinimumEmployees <= Math.Max(1, employeeCount))
            .Where(rule => rule.IndustryTags.Count == 0
                           || rule.IndustryTags.Contains("All", StringComparer.OrdinalIgnoreCase)
                           || rule.IndustryTags.Any(tag => industryTokens.Contains(tag)))
            .ToList();
    }

    private static int GetPatternSpecificity(ApplicationRepositoryPatternRule pattern)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(pattern.MatchApplicationNameContains))
        {
            score += 4;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchVendor))
        {
            score += 2;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchCategory))
        {
            score += 1;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchDepartmentNameContains))
        {
            score += 1;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchSharePurpose) || !string.IsNullOrWhiteSpace(pattern.MatchWorkspaceType))
        {
            score += 1;
        }

        return score;
    }

    private static int GetPatternSpecificity(CollaborationTabPatternRule pattern)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(pattern.MatchApplicationNameContains))
        {
            score += 4;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchVendor))
        {
            score += 2;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchCategory))
        {
            score += 1;
        }

        if (pattern.IndustryTags.Count > 0 && !pattern.IndustryTags.Contains("All", StringComparer.OrdinalIgnoreCase))
        {
            score += 1;
        }

        return score;
    }

    private static string ApplySlugPattern(string pattern, string departmentName, int index)
        => pattern
            .Replace("{dept}", Slug(departmentName), StringComparison.OrdinalIgnoreCase)
            .Replace("{n}", $"{index:00}", StringComparison.OrdinalIgnoreCase);

    private static string ApplyDisplayPattern(string pattern, string departmentName, int index)
        => pattern
            .Replace("{dept}", departmentName, StringComparison.OrdinalIgnoreCase)
            .Replace("{n}", $"{index:00}", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> SplitPipe(string value)
        => string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static HashSet<string> BuildIndustryTokens(string industry)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(industry))
        {
            return tokens;
        }

        foreach (var token in industry.Split(['|', ',', '/', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            tokens.Add(token);
        }

        return tokens;
    }

    private static string Read(IReadOnlyDictionary<string, string?> row, string key)
        => row.TryGetValue(key, out var value) ? value ?? string.Empty : string.Empty;

    private sealed record RepositoryPatternRule(string Type, string Pattern, string OwnerHint, string AccessModel);
    private sealed record ApplicationRepositoryPatternRule(
        string RepositoryType,
        string MatchApplicationNameContains,
        string MatchVendor,
        string MatchCategory,
        string MatchDepartmentNameContains,
        string MatchSharePurpose,
        string MatchWorkspaceType,
        IReadOnlyList<string> IndustryTags,
        int MinimumEmployees,
        string RelationshipType,
        string Criticality);
    private sealed record ApplicationRepositorySelection(ApplicationRecord Application, ApplicationRepositoryPatternRule? Pattern);
    private sealed record CollaborationTabPatternRule(
        string ChannelType,
        string WorkspaceType,
        string MatchApplicationNameContains,
        string MatchVendor,
        string MatchCategory,
        string PreferredTabName,
        string TabType,
        string TargetType,
        IReadOnlyList<string> IndustryTags,
        int MinimumEmployees,
        bool? IsPinned);
    private sealed record CuratedCollaborationTabSelection(ApplicationRecord Application, CollaborationTabPatternRule Pattern);
    private sealed record CollaborationChannelPatternRule(
        string Platform,
        string WorkspaceType,
        string PrivacyType,
        string ChannelName,
        string ChannelType,
        IReadOnlyList<string> IndustryTags,
        int MinimumEmployees);
    private sealed record DocumentLibraryPatternRule(
        string Platform,
        string WorkspaceType,
        string PrivacyType,
        string LibraryName,
        string TemplateType,
        string Sensitivity,
        IReadOnlyList<string> IndustryTags,
        int MinimumEmployees);
    private sealed record SitePagePatternRule(
        string Platform,
        string WorkspaceType,
        string PrivacyType,
        string PageTitle,
        string PageType,
        string PromotedState,
        string AssociatedLibraryName,
        IReadOnlyList<string> IndustryTags,
        int MinimumEmployees);
    private sealed record DocumentFolderPatternRule(
        string LibraryName,
        string WorkspaceType,
        string ParentFolderName,
        string FolderName,
        string FolderType,
        int Depth,
        IReadOnlyList<string> IndustryTags,
        int MinimumEmployees);
}
