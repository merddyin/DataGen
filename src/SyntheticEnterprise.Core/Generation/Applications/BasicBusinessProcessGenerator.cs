namespace SyntheticEnterprise.Core.Generation.Applications;

using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Core.Abstractions;

public sealed class BasicBusinessProcessGenerator : IBusinessProcessGenerator
{
    private readonly IIdFactory _idFactory;

    public BasicBusinessProcessGenerator(IIdFactory idFactory)
    {
        _idFactory = idFactory;
    }

    public void GenerateBusinessProcesses(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs)
    {
        var applicationProcessPatterns = ReadApplicationProcessPatterns(catalogs);

        foreach (var company in world.Companies)
        {
            var departments = world.Departments.Where(department => department.CompanyId == company.Id).ToList();
            var offices = world.Offices.Where(office => office.CompanyId == company.Id).ToList();
            var applications = world.Applications.Where(application => application.CompanyId == company.Id).ToList();
            var peopleCount = world.People.Count(person => person.CompanyId == company.Id && !string.Equals(person.PersonType, "Guest", StringComparison.OrdinalIgnoreCase));
            var industry = company.Industry ?? string.Empty;

            var processes = BuildProcessTemplates(company, departments, offices, peopleCount, catalogs);
            var created = new List<BusinessProcess>();

            foreach (var template in processes)
            {
                var ownerDepartment = SelectOwnerDepartment(departments, template.OwnerHints);
                if (ownerDepartment is null)
                {
                    continue;
                }

                var process = new BusinessProcess
                {
                    Id = _idFactory.Next("PROC"),
                    CompanyId = company.Id,
                    Name = template.Name,
                    Domain = template.Domain,
                    BusinessCapability = template.BusinessCapability,
                    OwnerDepartmentId = ownerDepartment.Id,
                    OperatingModel = template.OperatingModel,
                    ProcessScope = template.ProcessScope,
                    Criticality = template.Criticality,
                    CustomerFacing = template.CustomerFacing
                };

                world.BusinessProcesses.Add(process);
                created.Add(process);
            }

            foreach (var process in created)
            {
                foreach (var link in SelectApplicationLinksForProcess(applications, process, applicationProcessPatterns, industry, peopleCount))
                {
                    world.ApplicationBusinessProcessLinks.Add(new ApplicationBusinessProcessLink
                    {
                        Id = _idFactory.Next("APPPROC"),
                        CompanyId = company.Id,
                        ApplicationId = link.Application.Id,
                        BusinessProcessId = process.Id,
                        RelationshipType = link.RelationshipType,
                        IsPrimary = link.IsPrimary
                    });
                }
            }
        }
    }

    private static IReadOnlyList<BusinessProcessTemplate> BuildProcessTemplates(
        Company company,
        IReadOnlyList<Department> departments,
        IReadOnlyList<Office> offices,
        int employeeCount,
        CatalogSet catalogs)
    {
        var templates = new List<BusinessProcessTemplate>();

        var taxonomy = ResolveIndustryTaxonomy(company.Industry ?? string.Empty, catalogs);
        var industryTokens = BuildIndustryTokens(company.Industry ?? string.Empty, taxonomy);
        var contextualPatterns = ReadContextualProcessTemplates(catalogs);
        var catalogBackbone = ReadCatalogProcessTemplates(catalogs, "business_process_templates")
            .Where(template =>
                template.MinimumEmployees <= Math.Max(1, employeeCount)
                && (template.IndustryTags.Count == 0
                    || template.IndustryTags.Contains("All", StringComparer.OrdinalIgnoreCase)
                    || template.IndustryTags.Any(tag => industryTokens.Contains(tag))))
            .ToList();
        if (catalogBackbone.Count > 0)
        {
            templates.AddRange(catalogBackbone);
        }
        else
        {
            templates.AddRange(
            [
                new BusinessProcessTemplate("Hire to Retire", "Workforce", "Workforce Management", "Enterprise", "Enterprise", "High", false, "Human Resources", "Information Technology"),
                new BusinessProcessTemplate("Record to Report", "Finance", "Finance Operations", "Centralized", "Enterprise", "High", false, "Finance"),
                new BusinessProcessTemplate("Procure to Pay", "Supply Chain", "Supplier Management", "Centralized", "Enterprise", "High", false, "Procurement", "Finance"),
                new BusinessProcessTemplate("Order to Cash", "Revenue", "Revenue Operations", "Regional", "Enterprise", "High", true, "Sales", "Finance"),
                new BusinessProcessTemplate("Identity Access Lifecycle", "Security", "Identity and Access", "Centralized", "Enterprise", "High", false, "Security", "Information Technology"),
                new BusinessProcessTemplate("Incident to Resolution", "Operations", "Operational Control", "Regional", "Enterprise", "High", false, "Information Technology", "Operations"),
                new BusinessProcessTemplate("Demand to Pipeline", "Marketing", "Demand Generation", "Regional", "BusinessUnit", "Medium", true, "Marketing", "Sales")
            ]);
        }

        var contextualMatches = SelectContextualProcessTemplates(
            contextualPatterns,
            industryTokens,
            departments,
            offices.Count,
            employeeCount);

        if (contextualMatches.Count > 0)
        {
            templates.AddRange(contextualMatches);
        }
        else if (industryTokens.Contains("Manufacturing") || industryTokens.Contains("Industrials"))
        {
            templates.AddRange(
            [
                new BusinessProcessTemplate("Plan to Produce", "Manufacturing", "Manufacturing Planning", offices.Count > 2 ? "SiteBased" : "Regional", "Enterprise", "High", false, "Operations", "Engineering"),
                new BusinessProcessTemplate("Source to Deliver", "Supply Chain", "Logistics Coordination", offices.Count > 2 ? "SiteBased" : "Regional", "Enterprise", "High", true, "Operations", "Procurement"),
                new BusinessProcessTemplate("Quality to Release", "Manufacturing", "Quality Management", offices.Count > 1 ? "SiteBased" : "Centralized", "Enterprise", "High", false, "Operations", "Engineering"),
                new BusinessProcessTemplate("Maintain to Operate", "Manufacturing", "Asset Lifecycle", offices.Count > 1 ? "SiteBased" : "Regional", "Office", "Medium", false, "Operations", "Information Technology")
            ]);
            if (departments.Any(department => department.Name.Contains("Support", StringComparison.OrdinalIgnoreCase)))
            {
                templates.Add(new BusinessProcessTemplate("Case to Resolution", "Customer Service", "Customer Support", "Regional", "BusinessUnit", "Medium", true, "Support", "Operations"));
            }
        }

        templates.AddRange(ReadCatalogProcessTemplates(catalogs, "industry_process_templates")
            .Where(template =>
                template.MinimumEmployees <= Math.Max(1, employeeCount)
                && (template.IndustryTags.Count == 0
                    || template.IndustryTags.Contains("All", StringComparer.OrdinalIgnoreCase)
                    || template.IndustryTags.Any(tag => industryTokens.Contains(tag)))))
            ;

        return templates
            .GroupBy(template => template.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static IReadOnlyList<BusinessProcessTemplate> ReadCatalogProcessTemplates(CatalogSet catalogs, string catalogName)
    {
        if (!catalogs.CsvCatalogs.TryGetValue(catalogName, out var rows))
        {
            return Array.Empty<BusinessProcessTemplate>();
        }

        return rows
            .Where(row => !string.IsNullOrWhiteSpace(Read(row, "Name")))
            .Select(row => new BusinessProcessTemplate(
                Read(row, "Name"),
                Read(row, "Domain"),
                Read(row, "BusinessCapability"),
                Read(row, "OperatingModel"),
                Read(row, "ProcessScope"),
                Read(row, "Criticality"),
                bool.TryParse(Read(row, "CustomerFacing"), out var customerFacing) && customerFacing,
                SplitPipe(Read(row, "OwnerHints")),
                SplitPipe(Read(row, "IndustryTags")),
                int.TryParse(Read(row, "MinimumEmployees"), out var minimumEmployees) ? minimumEmployees : 0))
            .ToList();
    }

    private static IReadOnlyList<BusinessProcessTemplate> ReadContextualProcessTemplates(CatalogSet catalogs)
    {
        if (!catalogs.CsvCatalogs.TryGetValue("contextual_business_process_patterns", out var rows))
        {
            return Array.Empty<BusinessProcessTemplate>();
        }

        return rows
            .Where(row => !string.IsNullOrWhiteSpace(Read(row, "Name")))
            .Select(row => new BusinessProcessTemplate(
                Read(row, "Name"),
                Read(row, "Domain"),
                Read(row, "BusinessCapability"),
                Read(row, "OperatingModel"),
                Read(row, "ProcessScope"),
                Read(row, "Criticality"),
                bool.TryParse(Read(row, "CustomerFacing"), out var customerFacing) && customerFacing,
                SplitPipe(Read(row, "OwnerHints")),
                SplitPipe(Read(row, "IndustryTags")),
                int.TryParse(Read(row, "MinimumEmployees"), out var minimumEmployees) ? minimumEmployees : 0,
                SplitPipe(Read(row, "RequiredDepartmentHints")),
                int.TryParse(Read(row, "MinimumOfficeCount"), out var minimumOfficeCount) ? minimumOfficeCount : 0))
            .ToList();
    }

    private static IReadOnlyList<BusinessProcessTemplate> SelectContextualProcessTemplates(
        IReadOnlyList<BusinessProcessTemplate> templates,
        IReadOnlySet<string> industryTokens,
        IReadOnlyList<Department> departments,
        int officeCount,
        int employeeCount)
    {
        if (templates.Count == 0)
        {
            return Array.Empty<BusinessProcessTemplate>();
        }

        return templates
            .Where(template => template.MinimumEmployees <= Math.Max(1, employeeCount))
            .Where(template => template.MinimumOfficeCount <= Math.Max(1, officeCount))
            .Where(template => template.IndustryTags.Count == 0
                               || template.IndustryTags.Contains("All", StringComparer.OrdinalIgnoreCase)
                               || template.IndustryTags.Any(tag => industryTokens.Contains(tag)))
            .Where(template => template.RequiredDepartmentHints.Count == 0
                               || template.RequiredDepartmentHints.Any(hint =>
                                   departments.Any(department =>
                                       department.Name.Contains(hint, StringComparison.OrdinalIgnoreCase))))
            .OrderByDescending(GetContextualTemplateSpecificity)
            .ToList();
    }

    private static Department? SelectOwnerDepartment(
        IReadOnlyList<Department> departments,
        IReadOnlyList<string> ownerHints)
    {
        foreach (var hint in ownerHints)
        {
            var match = departments.FirstOrDefault(department =>
                department.Name.Contains(hint, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return departments.FirstOrDefault();
    }

    private static IEnumerable<ApplicationProcessLinkCandidate> SelectApplicationLinksForProcess(
        IReadOnlyList<ApplicationRecord> applications,
        BusinessProcess process,
        IReadOnlyList<ApplicationProcessPattern> patterns,
        string industry,
        int peopleCount)
    {
        var selected = new List<ApplicationProcessLinkCandidate>();
        var emittedApplicationIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pattern in SelectApplicationProcessPatterns(patterns, industry, peopleCount, process))
        {
            foreach (var application in applications.Where(application => MatchesApplicationProcessPattern(application, pattern)))
            {
                if (!emittedApplicationIds.Add(application.Id))
                {
                    continue;
                }

                selected.Add(new ApplicationProcessLinkCandidate(
                    application,
                    FirstNonEmpty(pattern.RelationshipType, DetermineRelationshipType(application, process)),
                    pattern.IsPrimary ?? IsPrimarySupport(application, process)));
            }
        }

        foreach (var application in applications.Where(application =>
                MatchesCapability(application.BusinessCapability, process.BusinessCapability)
                || MatchesDomain(application.Category, process.Domain)
                || application.Name.Contains(process.Domain, StringComparison.OrdinalIgnoreCase)
                || application.Name.Contains(process.Name.Split(' ')[0], StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(application => IsPrimarySupport(application, process))
            .ThenByDescending(application => application.Criticality == "High")
            .ThenBy(application => application.Name, StringComparer.OrdinalIgnoreCase)
            .Take(6))
        {
            if (!emittedApplicationIds.Add(application.Id))
            {
                continue;
            }

            selected.Add(new ApplicationProcessLinkCandidate(
                application,
                DetermineRelationshipType(application, process),
                IsPrimarySupport(application, process)));
        }

        return selected.Take(6).ToList();
    }

    private static bool MatchesCapability(string applicationCapability, string processCapability)
        => !string.IsNullOrWhiteSpace(applicationCapability)
           && !string.IsNullOrWhiteSpace(processCapability)
           && (applicationCapability.Contains(processCapability, StringComparison.OrdinalIgnoreCase)
               || processCapability.Contains(applicationCapability, StringComparison.OrdinalIgnoreCase));

    private static bool MatchesDomain(string category, string domain)
        => domain switch
        {
            "Workforce" => category is "HR" or "Security",
            "Finance" => category is "Finance" or "Analytics",
            "Supply Chain" => category is "Procurement" or "Operations" or "Finance",
            "Revenue" => category is "Sales" or "Marketing" or "Finance",
            "Security" => category is "Security",
            "Operations" => category is "Operations" or "Security",
            "Marketing" => category is "Marketing" or "Sales",
            "Manufacturing" => category is "Operations" or "Developer",
            "Customer Service" => category is "Operations" or "Sales",
            "Engineering" => category is "Developer" or "Operations",
            "Patient Services" => category is "Operations" or "Security",
            _ => false
        };

    private static string DetermineRelationshipType(ApplicationRecord application, BusinessProcess process)
        => IsPrimarySupport(application, process) ? "PrimarySystem" : "SupportingSystem";

    private static bool IsPrimarySupport(ApplicationRecord application, BusinessProcess process)
    {
        if (MatchesCapability(application.BusinessCapability, process.BusinessCapability))
        {
            return true;
        }

        if (process.Domain == "Finance" && application.Category == "Finance")
        {
            return true;
        }

        if (process.Domain == "Manufacturing" && application.Category == "Operations")
        {
            return true;
        }

        if (process.Domain == "Revenue" && application.Category == "Sales")
        {
            return true;
        }

        if (process.Domain == "Security" && application.Category == "Security")
        {
            return true;
        }

        if (process.Domain == "Engineering" && application.Category == "Developer")
        {
            return true;
        }

        return false;
    }

    private static IReadOnlyList<ApplicationProcessPattern> ReadApplicationProcessPatterns(CatalogSet catalogs)
    {
        if (!catalogs.CsvCatalogs.TryGetValue("application_process_patterns", out var rows))
        {
            return Array.Empty<ApplicationProcessPattern>();
        }

        return rows
            .Where(row => !string.IsNullOrWhiteSpace(Read(row, "MatchProcessName"))
                          && !string.IsNullOrWhiteSpace(Read(row, "MatchApplicationNameContains")))
            .Select(row => new ApplicationProcessPattern(
                Read(row, "MatchProcessName"),
                Read(row, "MatchApplicationNameContains"),
                Read(row, "MatchVendor"),
                Read(row, "MatchCategory"),
                SplitPipe(Read(row, "IndustryTags")),
                int.TryParse(Read(row, "MinimumEmployees"), out var minimumEmployees) ? minimumEmployees : 0,
                Read(row, "RelationshipType"),
                bool.TryParse(Read(row, "IsPrimary"), out var isPrimary) ? isPrimary : null))
            .ToList();
    }

    private static IEnumerable<ApplicationProcessPattern> SelectApplicationProcessPatterns(
        IReadOnlyList<ApplicationProcessPattern> patterns,
        string industry,
        int peopleCount,
        BusinessProcess process)
    {
        var industryTokens = BuildIndustryTokens(industry, null);
        return patterns
            .Where(pattern => pattern.MinimumEmployees <= Math.Max(1, peopleCount))
            .Where(pattern => string.Equals(pattern.MatchProcessName, process.Name, StringComparison.OrdinalIgnoreCase))
            .Where(pattern => pattern.IndustryTags.Count == 0
                              || pattern.IndustryTags.Contains("All", StringComparer.OrdinalIgnoreCase)
                              || pattern.IndustryTags.Any(tag => industryTokens.Contains(tag)))
            .OrderByDescending(GetPatternSpecificity)
            .ToList();
    }

    private static bool MatchesApplicationProcessPattern(ApplicationRecord application, ApplicationProcessPattern pattern)
    {
        if (!application.Name.Contains(pattern.MatchApplicationNameContains, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchVendor)
            && !string.Equals(application.Vendor, pattern.MatchVendor, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchCategory)
            && !string.Equals(application.Category, pattern.MatchCategory, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static int GetPatternSpecificity(ApplicationProcessPattern pattern)
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

    private static int GetContextualTemplateSpecificity(BusinessProcessTemplate template)
    {
        var score = 0;
        if (template.IndustryTags.Count > 0 && !template.IndustryTags.Contains("All", StringComparer.OrdinalIgnoreCase))
        {
            score += 2;
        }

        if (template.RequiredDepartmentHints.Count > 0)
        {
            score += 1;
        }

        if (template.MinimumOfficeCount > 0)
        {
            score += 1;
        }

        return score;
    }

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

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private sealed class BusinessProcessTemplate
    {
        public BusinessProcessTemplate(
            string name,
            string domain,
            string businessCapability,
            string operatingModel,
            string processScope,
            string criticality,
            bool customerFacing,
            params string[] ownerHints)
            : this(name, domain, businessCapability, operatingModel, processScope, criticality, customerFacing, ownerHints, Array.Empty<string>(), 0)
        {
        }

        public BusinessProcessTemplate(
            string name,
            string domain,
            string businessCapability,
            string operatingModel,
            string processScope,
            string criticality,
            bool customerFacing,
            IReadOnlyList<string> ownerHints,
            IReadOnlyList<string> industryTags,
            int minimumEmployees,
            IReadOnlyList<string>? requiredDepartmentHints = null,
            int minimumOfficeCount = 0)
        {
            Name = name;
            Domain = domain;
            BusinessCapability = businessCapability;
            OperatingModel = operatingModel;
            ProcessScope = processScope;
            Criticality = criticality;
            CustomerFacing = customerFacing;
            OwnerHints = ownerHints;
            IndustryTags = industryTags;
            MinimumEmployees = minimumEmployees;
            RequiredDepartmentHints = requiredDepartmentHints ?? Array.Empty<string>();
            MinimumOfficeCount = minimumOfficeCount;
        }

        public string Name { get; }
        public string Domain { get; }
        public string BusinessCapability { get; }
        public string OperatingModel { get; }
        public string ProcessScope { get; }
        public string Criticality { get; }
        public bool CustomerFacing { get; }
        public IReadOnlyList<string> OwnerHints { get; }
        public IReadOnlyList<string> IndustryTags { get; }
        public int MinimumEmployees { get; }
        public IReadOnlyList<string> RequiredDepartmentHints { get; }
        public int MinimumOfficeCount { get; }
    }

    private sealed record ApplicationProcessPattern(
        string MatchProcessName,
        string MatchApplicationNameContains,
        string MatchVendor,
        string MatchCategory,
        IReadOnlyList<string> IndustryTags,
        int MinimumEmployees,
        string RelationshipType,
        bool? IsPrimary);

    private sealed record ApplicationProcessLinkCandidate(
        ApplicationRecord Application,
        string RelationshipType,
        bool IsPrimary);

    private sealed record IndustryTaxonomyMatch(string Sector, string IndustryGroup, string Industry, string SubIndustry);
}
