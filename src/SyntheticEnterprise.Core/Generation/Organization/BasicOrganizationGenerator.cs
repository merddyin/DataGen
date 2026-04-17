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
            var primaryCountry = ResolvePrimaryCountry(companyDefinition.Countries);
            var legalName = BuildLegalName(companyDefinition.Name, companyDefinition.Industry, catalogs);
            var primaryDomain = BuildPrimaryDomain(companyDefinition.Name, primaryCountry, catalogs);
            var company = new Company
            {
                Id = _idFactory.Next("COMP"),
                Name = companyDefinition.Name,
                Industry = companyDefinition.Industry,
                LegalName = legalName,
                PrimaryCountry = primaryCountry,
                PrimaryDomain = primaryDomain,
                Website = BuildWebsite(primaryDomain),
                Tagline = BuildTagline(companyDefinition.Industry, catalogs)
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
        var fallbackNames = ReadCatalogValues(catalogs, "business_units", "Name", new[]
        {
            "Corporate Services", "Revenue Operations", "Product Engineering", "Customer Success", "Field Operations"
        });
        var templateNames = SelectOrganizationTemplates(catalogs, "BusinessUnit", companyDefinition.Industry, companyDefinition.EmployeeCount)
            .Select(template => template.Name)
            .ToList();
        var names = ExpandOrganizationNames(templateNames, fallbackNames, companyDefinition.BusinessUnitCount);

        var units = new List<BusinessUnit>();

        for (var i = 0; i < names.Count; i++)
        {
            units.Add(new BusinessUnit
            {
                Id = _idFactory.Next("BU"),
                CompanyId = company.Id,
                Name = names[i]
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
        var fallbackNames = ReadCatalogValues(catalogs, "departments", "Name", new[]
        {
            "Finance", "Human Resources", "Information Technology", "Sales", "Marketing",
            "Operations", "Engineering", "Legal", "Procurement", "Support"
        });
        var templates = SelectOrganizationTemplates(catalogs, "Department", companyDefinition.Industry, companyDefinition.EmployeeCount);

        var departments = new List<Department>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var businessUnit in businessUnits)
        {
            var preferredNames = templates
                .Where(template => MatchesAnyHint(businessUnit.Name, template.ParentHints))
                .Select(template => template.Name)
                .ToList();
            var names = ExpandOrganizationNames(preferredNames, fallbackNames, companyDefinition.DepartmentCountPerBusinessUnit, usedNames);

            foreach (var name in names)
            {
                departments.Add(new Department
                {
                    Id = _idFactory.Next("DEPT"),
                    CompanyId = company.Id,
                    BusinessUnitId = businessUnit.Id,
                    Name = name
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
        var fallbackNames = ReadCatalogValues(catalogs, "teams", "Name", new[]
        {
            "Platform", "Service Desk", "Identity", "Infrastructure", "Analytics",
            "Business Systems", "Regional Sales", "Demand Generation", "Payroll", "Procurement"
        });
        var templates = SelectOrganizationTemplates(catalogs, "Team", companyDefinition.Industry, companyDefinition.EmployeeCount);

        var teams = new List<Team>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var department in departments)
        {
            var preferredNames = templates
                .Where(template => MatchesAnyHint(department.Name, template.ParentHints))
                .Select(template => template.Name)
                .ToList();
            var names = ExpandOrganizationNames(preferredNames, fallbackNames, companyDefinition.TeamCountPerDepartment, usedNames);

            foreach (var name in names)
            {
                teams.Add(new Team
                {
                    Id = _idFactory.Next("TEAM"),
                    CompanyId = company.Id,
                    DepartmentId = department.Id,
                    Name = name
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

        if (maleFirstNames.Count == 0) maleFirstNames = ReadSimpleNameCatalog(catalogs, "given_names_male", companyDefinition.Countries);
        if (femaleFirstNames.Count == 0) femaleFirstNames = ReadSimpleNameCatalog(catalogs, "given_names_female", companyDefinition.Countries);
        if (lastNames.Count == 0) lastNames = ReadSimpleNameCatalog(catalogs, "surnames_reference", companyDefinition.Countries, "Value");

        if (maleFirstNames.Count == 0) maleFirstNames = GetFallbackFirstNames("Male", companyDefinition.Countries);
        if (femaleFirstNames.Count == 0) femaleFirstNames = GetFallbackFirstNames("Female", companyDefinition.Countries);
        if (lastNames.Count == 0) lastNames = GetFallbackLastNames(companyDefinition.Countries);

        var fallbackTitles = ReadCatalogValues(catalogs, "titles", "Title", new[]
        {
            "Analyst", "Engineer", "Senior Engineer", "Manager", "Director",
            "Specialist", "Administrator", "Architect", "Consultant", "Coordinator"
        });
        var titleProfiles = ReadTitleProfiles(catalogs, fallbackTitles);

        var results = new List<Person>();
        var domain = string.IsNullOrWhiteSpace(company.PrimaryDomain)
            ? BuildDomain(company.Name)
            : company.PrimaryDomain;
        var issuedUpns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var departmentsById = departments.ToDictionary(department => department.Id, department => department, StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < companyDefinition.EmployeeCount; i++)
        {
            var isFemale = _randomSource.NextDouble() >= 0.5;
            var firstPool = isFemale ? femaleFirstNames : maleFirstNames;
            var first = firstPool[_randomSource.Next(firstPool.Count)];
            var last = lastNames[_randomSource.Next(lastNames.Count)];
            var team = teams[i % teams.Count];
            var departmentId = team.DepartmentId;
            var employeeNumber = (100000 + i).ToString();
            var departmentName = departmentsById.TryGetValue(departmentId, out var department)
                ? department.Name
                : string.Empty;
            var title = PickTitle(i, departmentName, titleProfiles, fallbackTitles);

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
                UserPrincipalName = BuildUniqueUserPrincipalName(first.Name, last.Name, domain, employeeNumber, issuedUpns)
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

    private static IReadOnlyList<OrganizationTemplate> SelectOrganizationTemplates(
        CatalogSet catalogs,
        string layer,
        string industry,
        int employeeCount)
    {
        if (!catalogs.CsvCatalogs.TryGetValue("organization_templates", out var rows))
        {
            return Array.Empty<OrganizationTemplate>();
        }

        var taxonomy = ResolveIndustryTaxonomy(industry, catalogs);
        var industryTokens = BuildIndustryTokens(industry, taxonomy);

        return rows
            .Where(row => string.Equals(Read(row, "Layer"), layer, StringComparison.OrdinalIgnoreCase))
            .Select(row => new OrganizationTemplate(
                Read(row, "Layer"),
                Read(row, "Name"),
                SplitPipe(Read(row, "IndustryTags")),
                SplitPipe(Read(row, "ParentHints")),
                int.TryParse(Read(row, "MinimumEmployees"), out var minimumEmployees) ? minimumEmployees : 0))
            .Where(template => !string.IsNullOrWhiteSpace(template.Name))
            .Where(template => template.MinimumEmployees <= Math.Max(1, employeeCount))
            .Where(template => template.IndustryTags.Count == 0
                || template.IndustryTags.Contains("All", StringComparer.OrdinalIgnoreCase)
                || template.IndustryTags.Any(tag => industryTokens.Contains(tag)))
            .OrderBy(template => template.IndustryTags.Contains("All", StringComparer.OrdinalIgnoreCase) ? 1 : 0)
            .ThenByDescending(template => template.ParentHints.Count)
            .ThenBy(template => template.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> ExpandOrganizationNames(
        IReadOnlyList<string> preferredNames,
        IReadOnlyList<string> fallbackNames,
        int desiredCount,
        ISet<string>? usedNames = null)
    {
        var baseNames = preferredNames
            .Concat(fallbackNames)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (baseNames.Count == 0)
        {
            baseNames.Add("Operations");
        }

        var results = new List<string>();
        for (var i = 0; i < desiredCount; i++)
        {
            var baseName = baseNames[i % baseNames.Count];
            results.Add(EnsureUniqueName(baseName, usedNames));
        }

        return results;
    }

    private static string EnsureUniqueName(string baseName, ISet<string>? usedNames)
    {
        if (usedNames is null)
        {
            return baseName;
        }

        if (usedNames.Add(baseName))
        {
            return baseName;
        }

        for (var suffix = 2; suffix < 1000; suffix++)
        {
            var candidate = $"{baseName} {suffix}";
            if (usedNames.Add(candidate))
            {
                return candidate;
            }
        }

        var fallback = $"{baseName} {Guid.NewGuid():N}";
        usedNames.Add(fallback);
        return fallback;
    }

    private static bool MatchesAnyHint(string parentName, IReadOnlyList<string> hints)
    {
        if (hints.Count == 0)
        {
            return true;
        }

        return hints.Any(hint =>
            parentName.Contains(hint, StringComparison.OrdinalIgnoreCase)
            || hint.Contains(parentName, StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolvePrimaryCountry(IReadOnlyCollection<string> countries)
        => NormalizeCountries(countries).FirstOrDefault() ?? "United States";

    private static string BuildLegalName(string companyName, string industry, CatalogSet catalogs)
    {
        if (LooksLikeLegalName(companyName))
        {
            return companyName;
        }

        var suffixes = ReadCatalogValues(catalogs, "company_suffixes", "Value", new[] { "Inc", "LLC", "Ltd", "Corp", "Group", "Co" })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Where(value => !value.Contains("Association", StringComparison.OrdinalIgnoreCase)
                && !value.Contains("College", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (suffixes.Count == 0)
        {
            suffixes.Add("Inc");
        }

        var selectedSuffix = suffixes[GetDeterministicIndex($"{companyName}|{industry}", suffixes.Count)];
        return $"{companyName} {selectedSuffix}".Trim();
    }

    private static string BuildPrimaryDomain(string companyName, string primaryCountry, CatalogSet catalogs)
    {
        var slug = new string(companyName.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = "example";
        }

        var availableSuffixes = ReadCatalogValues(catalogs, "domain_suffixes", "Value", new[] { "com", "net", "org" })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().TrimStart('.'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (availableSuffixes.Count == 0)
        {
            availableSuffixes.Add("com");
        }

        var preferredSuffix = ResolvePreferredDomainSuffix(primaryCountry, catalogs);
        if (string.IsNullOrWhiteSpace(preferredSuffix))
        {
            preferredSuffix = "com";
        }
        else if (!CountryHasIdentityRule(primaryCountry, catalogs)
            && !availableSuffixes.Contains(preferredSuffix, StringComparer.OrdinalIgnoreCase)
            && preferredSuffix.Contains('.'))
        {
            var lastLabel = preferredSuffix.Split('.').Last();
            if (availableSuffixes.Contains(lastLabel, StringComparer.OrdinalIgnoreCase))
            {
                preferredSuffix = lastLabel;
            }
        }

        if (!CountryHasIdentityRule(primaryCountry, catalogs)
            && !availableSuffixes.Contains(preferredSuffix, StringComparer.OrdinalIgnoreCase)
            && !preferredSuffix.Contains('.'))
        {
            preferredSuffix = availableSuffixes.Contains("com", StringComparer.OrdinalIgnoreCase)
                ? "com"
                : availableSuffixes[GetDeterministicIndex(primaryCountry, availableSuffixes.Count)];
        }

        return $"{slug}.{preferredSuffix}";
    }

    private static string BuildWebsite(string primaryDomain)
        => string.IsNullOrWhiteSpace(primaryDomain) ? "https://www.example.test" : $"https://www.{primaryDomain}";

    private static string BuildTagline(string industry, CatalogSet catalogs)
    {
        var verbs = ReadCatalogValues(catalogs, "taglines", "Value", new[] { "transform", "optimize", "enable", "architect" })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (verbs.Count == 0)
        {
            verbs.Add("optimize");
        }

        var verb = verbs[GetDeterministicIndex(industry, verbs.Count)];
        var phrase = industry switch
        {
            var value when value.Contains("Manufact", StringComparison.OrdinalIgnoreCase) => "resilient manufacturing operations",
            var value when value.Contains("Finance", StringComparison.OrdinalIgnoreCase) => "trusted financial operations",
            var value when value.Contains("Health", StringComparison.OrdinalIgnoreCase) => "connected care workflows",
            var value when value.Contains("Technology", StringComparison.OrdinalIgnoreCase) => "secure digital platforms",
            var value when value.Contains("Commun", StringComparison.OrdinalIgnoreCase) => "always-on customer connectivity",
            _ => "reliable enterprise operations"
        };

        return $"{Capitalize(verb)} {phrase}";
    }

    private static bool LooksLikeLegalName(string companyName)
    {
        var legalSuffixes = new[] { "Inc", "Inc.", "LLC", "Ltd", "PLC", "Corp", "Corporation", "Company", "Co", "Group" };
        return legalSuffixes.Any(suffix => companyName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolvePreferredDomainSuffix(string primaryCountry, CatalogSet catalogs)
    {
        if (catalogs.CsvCatalogs.TryGetValue("country_identity_rules", out var rows))
        {
            var match = rows.FirstOrDefault(row =>
                string.Equals(Read(row, "Country"), primaryCountry, StringComparison.OrdinalIgnoreCase));
            var configured = match is null ? string.Empty : Read(match, "PrimaryDomainSuffix");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured.Trim().TrimStart('.');
            }
        }

        return primaryCountry switch
        {
            "United Kingdom" => "co.uk",
            "Australia" => "com.au",
            "New Zealand" => "co.nz",
            "Japan" => "co.jp",
            "Germany" => "de",
            "France" => "fr",
            "Canada" => "ca",
            "India" => "in",
            "Mexico" => "mx",
            "Brazil" => "com.br",
            _ => "com"
        };
    }

    private static bool CountryHasIdentityRule(string primaryCountry, CatalogSet catalogs)
        => catalogs.CsvCatalogs.TryGetValue("country_identity_rules", out var rows)
           && rows.Any(row => string.Equals(Read(row, "Country"), primaryCountry, StringComparison.OrdinalIgnoreCase));

    private static int GetDeterministicIndex(string seed, int count)
    {
        if (count <= 1)
        {
            return 0;
        }

        unchecked
        {
            var hash = 17;
            foreach (var character in seed)
            {
                hash = (hash * 31) + char.ToUpperInvariant(character);
            }

            return Math.Abs(hash % count);
        }
    }

    private static string Capitalize(string value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();

    private static IndustryTaxonomyMatch? ResolveIndustryTaxonomy(string industry, CatalogSet catalogs)
    {
        if (!catalogs.CsvCatalogs.TryGetValue("industries", out var rows))
        {
            return null;
        }

        var normalizedInput = industry.Trim();
        if (string.IsNullOrWhiteSpace(normalizedInput))
        {
            return null;
        }

        var exact = rows.FirstOrDefault(row =>
            MatchesIndustryField(normalizedInput, Read(row, "Sector"))
            || MatchesIndustryField(normalizedInput, Read(row, "IndustryGroup"))
            || MatchesIndustryField(normalizedInput, Read(row, "Industry"))
            || MatchesIndustryField(normalizedInput, Read(row, "SubIndustry")));

        if (exact is null)
        {
            return null;
        }

        return new IndustryTaxonomyMatch(
            Read(exact, "Sector"),
            Read(exact, "IndustryGroup"),
            Read(exact, "Industry"),
            Read(exact, "SubIndustry"));
    }

    private static bool MatchesIndustryField(string requested, string candidate)
    {
        if (string.IsNullOrWhiteSpace(requested) || string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        return candidate.Contains(requested, StringComparison.OrdinalIgnoreCase)
               || requested.Contains(candidate, StringComparison.OrdinalIgnoreCase);
    }

    private static HashSet<string> BuildIndustryTokens(string industry, IndustryTaxonomyMatch? taxonomy)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddTokens(tokens, industry);
        AddTokens(tokens, taxonomy?.Sector);
        AddTokens(tokens, taxonomy?.IndustryGroup);
        AddTokens(tokens, taxonomy?.Industry);
        AddTokens(tokens, taxonomy?.SubIndustry);
        return tokens;
    }

    private static void AddTokens(ISet<string> values, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        foreach (var token in raw.Split(['|', ',', '/', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            values.Add(token);
        }
    }

    private static IReadOnlyList<string> SplitPipe(string value)
        => value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Read(IReadOnlyDictionary<string, string?> row, string key)
        => row.TryGetValue(key, out var value) ? value ?? string.Empty : string.Empty;

    private static string Sanitize(string value)
    {
        return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }

    private static string BuildUniqueUserPrincipalName(
        string firstName,
        string lastName,
        string domain,
        string employeeNumber,
        ISet<string> issuedUpns)
    {
        var baseLocalPart = $"{Sanitize(firstName)}.{Sanitize(lastName)}";
        var candidate = $"{baseLocalPart}@{domain}";
        if (issuedUpns.Add(candidate))
        {
            return candidate;
        }

        candidate = $"{baseLocalPart}{employeeNumber[^4..]}@{domain}";
        if (issuedUpns.Add(candidate))
        {
            return candidate;
        }

        for (var suffix = 2; suffix < 10000; suffix++)
        {
            candidate = $"{baseLocalPart}{suffix}@{domain}";
            if (issuedUpns.Add(candidate))
            {
                return candidate;
            }
        }

        return $"{Guid.NewGuid():N}@{domain}";
    }

    private string PickTitle(
        int index,
        string departmentName,
        IReadOnlyList<TitleProfile> titleProfiles,
        IReadOnlyList<string> fallbackTitles)
    {
        if (index == 0) return "Chief Executive Officer";
        if (index < 5) return "Vice President";
        if (index < 15) return "Director";
        if (index < 40) return "Manager";

        var desiredLevel = index < 120 ? "Experienced" : "Entry";
        var departmentMatches = titleProfiles
            .Where(profile =>
                !string.IsNullOrWhiteSpace(profile.Title)
                && (string.IsNullOrWhiteSpace(profile.Department)
                    || departmentName.Contains(profile.Department, StringComparison.OrdinalIgnoreCase)
                    || profile.Department.Contains(departmentName, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var levelMatches = departmentMatches
            .Where(profile => string.Equals(profile.Level, desiredLevel, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var candidatePool = levelMatches.Count > 0
            ? levelMatches
            : departmentMatches.Count > 0
                ? departmentMatches
                : titleProfiles.Where(profile => !string.IsNullOrWhiteSpace(profile.Title)).ToList();

        if (candidatePool.Count > 0)
        {
            return candidatePool[_randomSource.Next(candidatePool.Count)].Title;
        }

        return fallbackTitles[index % fallbackTitles.Count];
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

    private static List<(string Name, string Country)> ReadSimpleNameCatalog(
        CatalogSet catalogs,
        string catalogName,
        IReadOnlyCollection<string> countries,
        string field = "Name")
    {
        if (!catalogs.CsvCatalogs.TryGetValue(catalogName, out var rows))
        {
            return new();
        }

        var defaultCountry = NormalizeCountries(countries).FirstOrDefault() ?? "United States";
        return rows
            .Where(row => row.TryGetValue(field, out var name) && !string.IsNullOrWhiteSpace(name))
            .Select(row => (
                Name: row[field] ?? string.Empty,
                Country: row.TryGetValue("Country", out var country) && !string.IsNullOrWhiteSpace(country)
                    ? country!
                    : defaultCountry))
            .Distinct()
            .ToList();
    }

    private static List<(string Name, string Country)> GetFallbackFirstNames(string gender, IReadOnlyCollection<string> countries)
    {
        var normalizedGender = gender.Equals("Female", StringComparison.OrdinalIgnoreCase) ? "Female" : "Male";
        var values = new List<(string Name, string Country)>();
        foreach (var country in NormalizeCountries(countries))
        {
            values.AddRange(GetCountryFallbackFirstNames(country, normalizedGender));
        }

        if (values.Count == 0)
        {
            values.AddRange(GetCountryFallbackFirstNames("United States", normalizedGender));
        }

        return values
            .Distinct()
            .ToList();
    }

    private static List<(string Name, string Country)> GetFallbackLastNames(IReadOnlyCollection<string> countries)
    {
        var values = new List<(string Name, string Country)>();
        foreach (var country in NormalizeCountries(countries))
        {
            values.AddRange(GetCountryFallbackLastNames(country));
        }

        if (values.Count == 0)
        {
            values.AddRange(GetCountryFallbackLastNames("United States"));
        }

        return values
            .Distinct()
            .ToList();
    }

    private static IEnumerable<string> NormalizeCountries(IReadOnlyCollection<string> countries)
        => countries.Count == 0 ? new[] { "United States" } : countries.Distinct(StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<(string Name, string Country)> GetCountryFallbackFirstNames(string country, string gender)
        => (country, gender) switch
        {
            ("United States", "Female") => CreateNamePairs(country, "Emma", "Olivia", "Sophia", "Ava", "Grace", "Mia"),
            ("United States", _) => CreateNamePairs(country, "James", "William", "Benjamin", "Michael", "Daniel", "Lucas"),
            ("United Kingdom", "Female") => CreateNamePairs(country, "Amelia", "Isla", "Freya", "Charlotte", "Florence"),
            ("United Kingdom", _) => CreateNamePairs(country, "George", "Oliver", "Arthur", "Harry", "Noah"),
            ("India", "Female") => CreateNamePairs(country, "Priya", "Ananya", "Aisha", "Ishita", "Neha"),
            ("India", _) => CreateNamePairs(country, "Rahul", "Arjun", "Vikram", "Aarav", "Rohan"),
            ("Japan", "Female") => CreateNamePairs(country, "Yui", "Sakura", "Aoi", "Mio", "Hina"),
            ("Japan", _) => CreateNamePairs(country, "Haruto", "Yuki", "Ren", "Takashi", "Sota"),
            ("Germany", "Female") => CreateNamePairs(country, "Emma", "Mia", "Hannah", "Lea", "Anna"),
            ("Germany", _) => CreateNamePairs(country, "Lukas", "Leon", "Jonas", "Finn", "Paul"),
            ("Mexico", "Female") => CreateNamePairs(country, "Sofia", "Valentina", "Camila", "Renata", "Ximena"),
            ("Mexico", _) => CreateNamePairs(country, "Santiago", "Mateo", "Sebastian", "Diego", "Emilio"),
            ("Brazil", "Female") => CreateNamePairs(country, "Ana", "Beatriz", "Julia", "Laura", "Mariana"),
            ("Brazil", _) => CreateNamePairs(country, "Gabriel", "Lucas", "Pedro", "Miguel", "Rafael"),
            _ when gender == "Female" => CreateNamePairs(country, "Emma", "Olivia", "Sophia", "Ava"),
            _ => CreateNamePairs(country, "James", "William", "Benjamin", "Daniel")
        };

    private static IEnumerable<(string Name, string Country)> GetCountryFallbackLastNames(string country)
        => country switch
        {
            "United States" => CreateNamePairs(country, "Smith", "Johnson", "Brown", "Davis", "Miller", "Wilson"),
            "United Kingdom" => CreateNamePairs(country, "Smith", "Jones", "Taylor", "Williams", "Davies"),
            "India" => CreateNamePairs(country, "Patel", "Singh", "Sharma", "Kumar", "Gupta"),
            "Japan" => CreateNamePairs(country, "Sato", "Suzuki", "Takahashi", "Tanaka", "Watanabe"),
            "Germany" => CreateNamePairs(country, "Muller", "Schmidt", "Schneider", "Fischer", "Weber"),
            "Mexico" => CreateNamePairs(country, "Garcia", "Hernandez", "Lopez", "Martinez", "Gonzalez"),
            "Brazil" => CreateNamePairs(country, "Silva", "Santos", "Oliveira", "Souza", "Costa"),
            _ => CreateNamePairs(country, "Smith", "Patel", "Garcia", "Brown")
        };

    private static IEnumerable<(string Name, string Country)> CreateNamePairs(string country, params string[] names)
        => names.Select(name => (name, country));

    private static string FirstNonEmpty(params string[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static List<TitleProfile> ReadTitleProfiles(CatalogSet catalogs, IReadOnlyList<string> fallbackTitles)
    {
        if (!catalogs.CsvCatalogs.TryGetValue("titles", out var rows))
        {
            return fallbackTitles.Select(title => new TitleProfile(title, string.Empty, string.Empty)).ToList();
        }

        var profiles = rows
            .Where(row => row.TryGetValue("Title", out var title) && !string.IsNullOrWhiteSpace(title))
            .Select(row => new TitleProfile(
                row["Title"] ?? string.Empty,
                row.TryGetValue("Department", out var department) ? department ?? string.Empty : string.Empty,
                NormalizeTitleLevel(
                    row.TryGetValue("Level", out var level) ? level : null,
                    row.TryGetValue("Type", out var type) ? type : null)))
            .GroupBy(profile => $"{profile.Title}|{profile.Department}|{profile.Level}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        return profiles.Count > 0
            ? profiles
            : fallbackTitles.Select(title => new TitleProfile(title, string.Empty, string.Empty)).ToList();
    }

    private static string NormalizeTitleLevel(string? level, string? type)
    {
        var normalized = FirstNonEmpty(level ?? string.Empty, type ?? string.Empty);
        return normalized.Trim() switch
        {
            var value when value.Contains("Entry", StringComparison.OrdinalIgnoreCase)
                || value.Contains("Junior", StringComparison.OrdinalIgnoreCase)
                || value.Contains("Intern", StringComparison.OrdinalIgnoreCase) => "Entry",
            var value when value.Contains("Lead", StringComparison.OrdinalIgnoreCase)
                || value.Contains("Senior", StringComparison.OrdinalIgnoreCase)
                || value.Contains("Experienced", StringComparison.OrdinalIgnoreCase)
                || value.Contains("Professional", StringComparison.OrdinalIgnoreCase) => "Experienced",
            var value when value.Contains("Leadership", StringComparison.OrdinalIgnoreCase)
                || value.Contains("Manager", StringComparison.OrdinalIgnoreCase)
                || value.Contains("Director", StringComparison.OrdinalIgnoreCase)
                || value.Contains("Executive", StringComparison.OrdinalIgnoreCase) => "Leadership",
            _ => normalized
        };
    }

    private sealed record TitleProfile(string Title, string Department, string Level);
    private sealed record OrganizationTemplate(string Layer, string Name, IReadOnlyList<string> IndustryTags, IReadOnlyList<string> ParentHints, int MinimumEmployees);
    private sealed record IndustryTaxonomyMatch(string Sector, string IndustryGroup, string Industry, string SubIndustry);
}
