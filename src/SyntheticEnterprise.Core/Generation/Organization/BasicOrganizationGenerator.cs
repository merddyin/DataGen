namespace SyntheticEnterprise.Core.Generation.Organization;

using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Core.Abstractions;

public sealed class BasicOrganizationGenerator : IOrganizationGenerator
{
    private readonly IIdFactory _idFactory;
    private readonly IRandomSource _randomSource;

    public BasicOrganizationGenerator(IIdFactory idFactory, IRandomSource randomSource)
    {
        _idFactory = idFactory;
        _randomSource = randomSource;
    }

    public void GenerateOrganizations(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs)
    {
        foreach (var companyDefinition in context.Scenario.Companies)
        {
            var company = new Company
            {
                Id = _idFactory.Next("COMP"),
                Name = companyDefinition.Name,
                Industry = companyDefinition.Industry
            };

            world.Companies.Add(company);

            var businessUnits = CreateBusinessUnits(company, companyDefinition, catalogs);
            world.BusinessUnits.AddRange(businessUnits);

            var departments = CreateDepartments(company, businessUnits, companyDefinition, catalogs);
            world.Departments.AddRange(departments);

            var teams = CreateTeams(company, departments, companyDefinition, catalogs);
            world.Teams.AddRange(teams);

            var people = CreatePeople(company, teams, departments, companyDefinition, catalogs);
            world.People.AddRange(people);
        }
    }

    private List<BusinessUnit> CreateBusinessUnits(
        Company company,
        ScenarioCompanyDefinition companyDefinition,
        CatalogSet catalogs)
    {
        var names = ReadCatalogValues(catalogs, "business_units", "Name", new[]
        {
            "Corporate Services", "Revenue Operations", "Product Engineering", "Customer Success", "Field Operations"
        });

        var units = new List<BusinessUnit>();

        for (var i = 0; i < companyDefinition.BusinessUnitCount; i++)
        {
            units.Add(new BusinessUnit
            {
                Id = _idFactory.Next("BU"),
                CompanyId = company.Id,
                Name = names[i % names.Count]
            });
        }

        return units;
    }

    private List<Department> CreateDepartments(
        Company company,
        IReadOnlyList<BusinessUnit> businessUnits,
        ScenarioCompanyDefinition companyDefinition,
        CatalogSet catalogs)
    {
        var names = ReadCatalogValues(catalogs, "departments", "Name", new[]
        {
            "Finance", "Human Resources", "Information Technology", "Sales", "Marketing",
            "Operations", "Engineering", "Legal", "Procurement", "Support"
        });

        var departments = new List<Department>();

        foreach (var businessUnit in businessUnits)
        {
            for (var i = 0; i < companyDefinition.DepartmentCountPerBusinessUnit; i++)
            {
                departments.Add(new Department
                {
                    Id = _idFactory.Next("DEPT"),
                    CompanyId = company.Id,
                    BusinessUnitId = businessUnit.Id,
                    Name = names[(departments.Count + i) % names.Count]
                });
            }
        }

        return departments;
    }

    private List<Team> CreateTeams(
        Company company,
        IReadOnlyList<Department> departments,
        ScenarioCompanyDefinition companyDefinition,
        CatalogSet catalogs)
    {
        var names = ReadCatalogValues(catalogs, "teams", "Name", new[]
        {
            "Platform", "Service Desk", "Identity", "Infrastructure", "Analytics",
            "Business Systems", "Regional Sales", "Demand Generation", "Payroll", "Procurement"
        });

        var teams = new List<Team>();

        foreach (var department in departments)
        {
            for (var i = 0; i < companyDefinition.TeamCountPerDepartment; i++)
            {
                teams.Add(new Team
                {
                    Id = _idFactory.Next("TEAM"),
                    CompanyId = company.Id,
                    DepartmentId = department.Id,
                    Name = names[(teams.Count + i) % names.Count]
                });
            }
        }

        return teams;
    }

    private List<Person> CreatePeople(
        Company company,
        IReadOnlyList<Team> teams,
        IReadOnlyList<Department> departments,
        ScenarioCompanyDefinition companyDefinition,
        CatalogSet catalogs)
    {
        var maleFirstNames = ReadFilteredNameCatalog(catalogs, "first_names_country", "Male", companyDefinition.Countries);
        var femaleFirstNames = ReadFilteredNameCatalog(catalogs, "first_names_country", "Female", companyDefinition.Countries);
        var lastNames = ReadFilteredLastNameCatalog(catalogs, "last_names_country", companyDefinition.Countries);

        if (maleFirstNames.Count == 0) maleFirstNames = new() { ("James", "United States"), ("Rahul", "India"), ("Takashi", "Japan") };
        if (femaleFirstNames.Count == 0) femaleFirstNames = new() { ("Mary", "United States"), ("Priya", "India"), ("Yuna", "South Korea") };
        if (lastNames.Count == 0) lastNames = new() { ("Smith", "United States"), ("Singh", "India"), ("Sato", "Japan") };

        var titles = ReadCatalogValues(catalogs, "titles", "Title", new[]
        {
            "Analyst", "Engineer", "Senior Engineer", "Manager", "Director",
            "Specialist", "Administrator", "Architect", "Consultant", "Coordinator"
        });

        var results = new List<Person>();
        var domain = BuildDomain(company.Name);

        for (var i = 0; i < companyDefinition.EmployeeCount; i++)
        {
            var isFemale = _randomSource.NextDouble() >= 0.5;
            var firstPool = isFemale ? femaleFirstNames : maleFirstNames;
            var first = firstPool[_randomSource.Next(firstPool.Count)];
            var last = lastNames[_randomSource.Next(lastNames.Count)];
            var team = teams[i % teams.Count];
            var departmentId = team.DepartmentId;
            var employeeNumber = (100000 + i).ToString();
            var title = PickTitle(i, titles);

            var person = new Person
            {
                Id = _idFactory.Next("PERS"),
                CompanyId = company.Id,
                TeamId = team.Id,
                DepartmentId = departmentId,
                FirstName = first.Name,
                LastName = last.Name,
                DisplayName = $"{first.Name} {last.Name}",
                Title = title,
                EmployeeId = employeeNumber,
                Country = !string.IsNullOrWhiteSpace(first.Country) ? first.Country : last.Country,
                UserPrincipalName = $"{Sanitize(first.Name)}.{Sanitize(last.Name)}@{domain}"
            };

            results.Add(person);
        }

        // Simple reporting structure:
        // first person is CEO, next BU leaders report to CEO, remaining employees report to an earlier manager
        if (results.Count > 0)
        {
            results[0] = results[0] with { Title = "Chief Executive Officer", ManagerPersonId = null };
        }

        var managerIndices = new List<int> { 0 };
        for (var i = 1; i < results.Count; i++)
        {
            var title = results[i].Title;
            var isManager = title.Contains("Manager", StringComparison.OrdinalIgnoreCase)
                || title.Contains("Director", StringComparison.OrdinalIgnoreCase)
                || i <= Math.Min(10, results.Count - 1);

            var managerIndex = managerIndices[_randomSource.Next(managerIndices.Count)];
            results[i] = results[i] with { ManagerPersonId = results[managerIndex].Id };

            if (isManager)
            {
                managerIndices.Add(i);
            }
        }

        return results;
    }

    private static string BuildDomain(string companyName)
    {
        var normalized = new string(companyName.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? "example.test" : normalized + ".test";
    }

    private static string Sanitize(string value)
    {
        return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }

    private static string PickTitle(int index, IReadOnlyList<string> titles)
    {
        if (index == 0) return "Chief Executive Officer";
        if (index < 5) return "Vice President";
        if (index < 15) return "Director";
        if (index < 40) return "Manager";
        return titles[index % titles.Count];
    }

    private static List<string> ReadCatalogValues(CatalogSet catalogs, string catalogName, string field, IEnumerable<string> fallback)
    {
        if (catalogs.CsvCatalogs.TryGetValue(catalogName, out var rows))
        {
            var values = rows
                .Where(row => row.TryGetValue(field, out var value) && !string.IsNullOrWhiteSpace(value))
                .Select(row => row[field]!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (values.Count > 0)
            {
                return values;
            }
        }

        return fallback.ToList();
    }

    private static List<(string Name, string Country)> ReadFilteredNameCatalog(
        CatalogSet catalogs,
        string catalogName,
        string gender,
        IReadOnlyCollection<string> countries)
    {
        if (!catalogs.CsvCatalogs.TryGetValue(catalogName, out var rows))
        {
            return new();
        }

        var filtered = rows
            .Where(row => row.TryGetValue("Gender", out var g) && string.Equals(g, gender, StringComparison.OrdinalIgnoreCase))
            .Where(row => countries.Count == 0 || (row.TryGetValue("Country", out var c) && c is not null && countries.Contains(c)))
            .Where(row => row.TryGetValue("Name", out var n) && !string.IsNullOrWhiteSpace(n))
            .Select(row => (
                Name: row["Name"] ?? "",
                Country: row.TryGetValue("Country", out var country) ? country ?? "" : ""
            ))
            .Distinct()
            .ToList();

        return filtered;
    }

    private static List<(string Name, string Country)> ReadFilteredLastNameCatalog(
        CatalogSet catalogs,
        string catalogName,
        IReadOnlyCollection<string> countries)
    {
        if (!catalogs.CsvCatalogs.TryGetValue(catalogName, out var rows))
        {
            return new();
        }

        var filtered = rows
            .Where(row => countries.Count == 0 || (row.TryGetValue("Country", out var c) && c is not null && countries.Contains(c)))
            .Where(row => row.TryGetValue("Name", out var n) && !string.IsNullOrWhiteSpace(n))
            .Select(row => (
                Name: row["Name"] ?? "",
                Country: row.TryGetValue("Country", out var country) ? country ?? "" : ""
            ))
            .Distinct()
            .ToList();

        return filtered;
    }
}
