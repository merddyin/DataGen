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
            var servers = world.Servers.Where(s => s.CompanyId == company.Id).ToList();

            CreateDatabases(world, company, definition, departments, servers);
            CreateFileShares(world, company, definition, departments);
            CreateCollaborationSites(world, company, definition, departments, people);
            CreateAccessGrants(world, company, departments, groups);
        }
    }

    private void CreateDatabases(
        SyntheticEnterpriseWorld world,
        Company company,
        ScenarioCompanyDefinition definition,
        IReadOnlyList<Department> departments,
        IReadOnlyList<ServerAsset> servers)
    {
        var engines = new[] { "SQL Server", "PostgreSQL", "MySQL", "Oracle" };
        var sensitivities = new[] { "Internal", "Confidential", "Restricted" };
        var prefixes = new[] { "ERP", "HRIS", "CRM", "MES", "DWH", "OPS", "FIN", "PAY", "SUP" };

        for (var i = 0; i < Math.Max(1, definition.DatabaseCount); i++)
        {
            var dept = departments[i % departments.Count];
            var server = servers.Count > 0 ? servers[i % servers.Count] : null;
            world.Databases.Add(new DatabaseRepository
            {
                Id = _idFactory.Next("DB"),
                CompanyId = company.Id,
                Name = $"{prefixes[i % prefixes.Length]}_{Slug(dept.Name)}_{i + 1:00}",
                Engine = engines[i % engines.Length],
                Environment = i % 6 == 0 ? "Staging" : "Production",
                SizeGb = ((i + 1) * 25 + _randomSource.Next(5, 90)).ToString(),
                OwnerDepartmentId = dept.Id,
                HostServerId = server?.Id,
                Sensitivity = sensitivities[i % sensitivities.Length]
            });
        }
    }

    private void CreateFileShares(
        SyntheticEnterpriseWorld world,
        Company company,
        ScenarioCompanyDefinition definition,
        IReadOnlyList<Department> departments)
    {
        for (var i = 0; i < Math.Max(1, definition.FileShareCount); i++)
        {
            var dept = departments[i % departments.Count];
            var shareName = $"{Slug(dept.Name)}-share-{i + 1:00}";
            world.FileShares.Add(new FileShareRepository
            {
                Id = _idFactory.Next("FS"),
                CompanyId = company.Id,
                ShareName = shareName,
                UncPath = $"\\files.{Slug(company.Name)}.test\{shareName}",
                OwnerDepartmentId = dept.Id,
                FileCount = (500 + _randomSource.Next(0, 20000)).ToString(),
                FolderCount = (20 + _randomSource.Next(0, 700)).ToString(),
                TotalSizeGb = (10 + _randomSource.Next(0, 2500)).ToString(),
                AccessModel = i % 4 == 0 ? "Mixed" : "GroupBased"
            });
        }
    }

    private void CreateCollaborationSites(
        SyntheticEnterpriseWorld world,
        Company company,
        ScenarioCompanyDefinition definition,
        IReadOnlyList<Department> departments,
        IReadOnlyList<Person> people)
    {
        for (var i = 0; i < Math.Max(1, definition.CollaborationSiteCount); i++)
        {
            var dept = departments[i % departments.Count];
            var owner = people[i % people.Count];
            var siteName = $"{dept.Name} {(i % 3 == 0 ? "Operations" : i % 3 == 1 ? "Workspace" : "Projects")}";

            world.CollaborationSites.Add(new CollaborationSite
            {
                Id = _idFactory.Next("SITE"),
                CompanyId = company.Id,
                Platform = i % 5 == 0 ? "Teams" : "SharePoint",
                Name = siteName,
                Url = $"https://collab.{Slug(company.Name)}.test/sites/{Slug(siteName)}",
                OwnerPersonId = owner.Id,
                OwnerDepartmentId = dept.Id,
                MemberCount = (8 + _randomSource.Next(0, 220)).ToString(),
                FileCount = (100 + _randomSource.Next(0, 25000)).ToString(),
                TotalSizeGb = (1 + _randomSource.Next(0, 800)).ToString(),
                PrivacyType = i % 4 == 0 ? "Public" : "Private"
            });
        }
    }

    private void CreateAccessGrants(
        SyntheticEnterpriseWorld world,
        Company company,
        IReadOnlyList<Department> departments,
        IReadOnlyList<DirectoryGroup> groups)
    {
        foreach (var db in world.Databases.Where(d => d.CompanyId == company.Id))
        {
            var group = FindDepartmentGroup(groups, departments, db.OwnerDepartmentId)
                ?? groups.FirstOrDefault(g => g.CompanyId == company.Id && g.Name == "SG-AllEmployees");

            if (group is null) continue;

            world.RepositoryAccessGrants.Add(new RepositoryAccessGrant
            {
                Id = _idFactory.Next("RAG"),
                RepositoryId = db.Id,
                RepositoryType = "Database",
                PrincipalObjectId = group.Id,
                PrincipalType = "Group",
                AccessLevel = "Modify"
            });
        }

        foreach (var share in world.FileShares.Where(s => s.CompanyId == company.Id))
        {
            var group = FindDepartmentGroup(groups, departments, share.OwnerDepartmentId)
                ?? groups.FirstOrDefault(g => g.CompanyId == company.Id && g.Name == "SG-AllEmployees");

            if (group is null) continue;

            world.RepositoryAccessGrants.Add(new RepositoryAccessGrant
            {
                Id = _idFactory.Next("RAG"),
                RepositoryId = share.Id,
                RepositoryType = "FileShare",
                PrincipalObjectId = group.Id,
                PrincipalType = "Group",
                AccessLevel = share.AccessModel == "Mixed" ? "FullControl" : "Modify"
            });
        }

        foreach (var site in world.CollaborationSites.Where(s => s.CompanyId == company.Id))
        {
            var group = FindDepartmentGroup(groups, departments, site.OwnerDepartmentId)
                ?? groups.FirstOrDefault(g => g.CompanyId == company.Id && g.Name == "M365-AllEmployees");

            if (group is null) continue;

            world.RepositoryAccessGrants.Add(new RepositoryAccessGrant
            {
                Id = _idFactory.Next("RAG"),
                RepositoryId = site.Id,
                RepositoryType = "CollaborationSite",
                PrincipalObjectId = group.Id,
                PrincipalType = "Group",
                AccessLevel = site.PrivacyType == "Public" ? "Read" : "Member"
            });
        }
    }

    private static DirectoryGroup? FindDepartmentGroup(
        IReadOnlyList<DirectoryGroup> groups,
        IReadOnlyList<Department> departments,
        string departmentId)
    {
        var dept = departments.FirstOrDefault(d => d.Id == departmentId);
        if (dept is null) return null;
        var expected = $"SG-{Slug(dept.Name)}-Users";
        return groups.FirstOrDefault(g => string.Equals(g.Name, expected, StringComparison.OrdinalIgnoreCase));
    }

    private static string Slug(string value)
        => new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
}
