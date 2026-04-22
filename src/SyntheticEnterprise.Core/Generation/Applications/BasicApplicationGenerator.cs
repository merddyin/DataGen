namespace SyntheticEnterprise.Core.Generation.Applications;

using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Core.Abstractions;

public sealed class BasicApplicationGenerator : IApplicationGenerator
{
    private readonly IIdFactory _idFactory;
    private readonly IRandomSource _randomSource;

    public BasicApplicationGenerator(IIdFactory idFactory, IRandomSource randomSource)
    {
        _idFactory = idFactory;
        _randomSource = randomSource;
    }

    public void GenerateApplications(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs)
    {
        if (!context.Scenario.Applications.IncludeApplications)
        {
            return;
        }

        var softwareTemplates = ReadSoftwareCatalog(catalogs);
        var essentialSoftwarePatterns = ReadEssentialSoftwarePatterns(catalogs);
        var enterprisePlatformPatterns = ReadEnterprisePlatformPatterns(catalogs);
        var catalogTemplates = ReadApplicationTemplates(catalogs);
        var industryApplicationPatterns = ReadIndustryApplicationPatterns(catalogs);
        var internalPatterns = ReadInternalApplicationPatterns(catalogs);
        var departmentPatterns = ReadDepartmentApplicationPatterns(catalogs);
        var officePatterns = ReadOfficeApplicationPatterns(catalogs);
        var industrySystemPatterns = ReadIndustrySystemPatterns(catalogs);
        var dependencyPatterns = ReadApplicationDependencyPatterns(catalogs);
        var suiteTemplates = ReadApplicationSuiteTemplates(catalogs);
        var vendorProfiles = ReadVendorProfiles(catalogs);

        foreach (var company in world.Companies)
        {
            var companyDefinition = context.Scenario.Companies
                .FirstOrDefault(item => string.Equals(item.Name, company.Name, StringComparison.OrdinalIgnoreCase));
            var departments = world.Departments.Where(department => department.CompanyId == company.Id).ToList();
            var companyPeople = world.People.Where(person => person.CompanyId == company.Id).ToList();
            var offices = world.Offices.Where(office => office.CompanyId == company.Id).ToList();
            if (departments.Count == 0)
            {
                continue;
            }

            var industry = companyDefinition?.Industry ?? company.Industry;
            var taxonomy = ResolveIndustryTaxonomy(industry, catalogs);
            var effectiveEmployeeCount = companyDefinition?.EmployeeCount ?? companyPeople.Count;
            var desiredCount = CalculateApplicationCount(companyDefinition, companyPeople.Count, context.Scenario, context.Scenario.Applications, catalogs);
            var selectedTemplates = SelectSoftwareTemplates(
                softwareTemplates,
                Math.Max(0, desiredCount),
                industry,
                taxonomy,
                effectiveEmployeeCount,
                essentialSoftwarePatterns);
            var applications = new List<ApplicationRecord>();

            foreach (var template in selectedTemplates)
            {
                applications.Add(CreateApplication(company, template, departments, context.Scenario.Applications, vendorProfiles));
            }

            applications.AddRange(CreateEnterprisePlatformApplications(
                company,
                departments,
                companyPeople.Count,
                offices.Count,
                industry,
                taxonomy,
                enterprisePlatformPatterns,
                catalogTemplates,
                internalPatterns,
                vendorProfiles));

            if (context.Scenario.Applications.IncludeLineOfBusinessApplications)
            {
                applications.AddRange(CreateLineOfBusinessApplications(
                    company,
                    companyDefinition,
                    departments,
                    taxonomy,
                    industryApplicationPatterns,
                    catalogTemplates,
                    internalPatterns,
                    vendorProfiles));
            }

            applications.AddRange(CreateIndustrySystemApplications(
                company,
                departments,
                companyDefinition?.EmployeeCount ?? companyPeople.Count,
                industry,
                taxonomy,
                industrySystemPatterns,
                vendorProfiles));

            applications.AddRange(CreateVendorSuiteApplications(
                company,
                departments,
                companyDefinition?.EmployeeCount ?? companyPeople.Count,
                industry,
                taxonomy,
                applications,
                suiteTemplates,
                vendorProfiles));

            applications.AddRange(CreateDepartmentApplications(company, departments, industry, taxonomy, effectiveEmployeeCount, departmentPatterns));
            applications.AddRange(CreateDepartmentSpecialtyApplications(company, departments, industry, taxonomy, effectiveEmployeeCount, departmentPatterns));
            applications.AddRange(CreateOfficeApplications(company, offices, departments, industry, taxonomy, effectiveEmployeeCount, officePatterns));

            var companyApplications = applications
                .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            foreach (var application in companyApplications)
            {
                world.Applications.Add(application);
            }

            foreach (var dependency in CreateApplicationDependencies(
                         company,
                         companyApplications,
                         industry,
                         taxonomy,
                         companyDefinition?.EmployeeCount ?? companyPeople.Count,
                         dependencyPatterns))
            {
                world.ApplicationDependencies.Add(dependency);
            }
        }
    }

    private int CalculateApplicationCount(
        ScenarioCompanyDefinition? companyDefinition,
        int generatedEmployeeCount,
        ScenarioDefinition scenario,
        ApplicationProfile profile,
        CatalogSet catalogs)
    {
        var scenarioAverage = scenario.EmployeeSize.Minimum > 0 && scenario.EmployeeSize.Maximum >= scenario.EmployeeSize.Minimum
            ? (scenario.EmployeeSize.Minimum + scenario.EmployeeSize.Maximum) / 2
            : 0;
        var employeeCount = generatedEmployeeCount > 0
            ? generatedEmployeeCount
            : companyDefinition?.EmployeeCount ?? scenarioAverage;
        if (employeeCount <= 0)
        {
            employeeCount = 250;
        }

        var scaled = Math.Max(profile.BaseApplicationCount, employeeCount / 180 + profile.BaseApplicationCount);
        var bandBonus = ResolveCompanySizeBand(employeeCount, catalogs) switch
        {
            "Micro" => 0,
            "Small" => 1,
            "Medium" => 2,
            "Large" => 3,
            "MicroEnt" => 4,
            "SmallEnt" => 5,
            "MediumEnt" => 6,
            "LargeEnt" => 8,
            _ => 1
        };

        return Math.Clamp(scaled + bandBonus, profile.BaseApplicationCount, 36);
    }

    private ApplicationRecord CreateApplication(
        Company company,
        SoftwareTemplate template,
        IReadOnlyList<Department> departments,
        ApplicationProfile profile,
        IReadOnlyDictionary<string, VendorProfile> vendorProfiles)
    {
        var ownerDepartment = SelectOwnerDepartment(template.Category, departments, null, template.Vendor, vendorProfiles);
        var hostingModel = SelectHostingModel(template.Category, profile);
        var vendorProfile = ResolveVendorProfile(template.Vendor, vendorProfiles);

        return new ApplicationRecord
        {
            Id = _idFactory.Next("APP"),
            CompanyId = company.Id,
            Name = template.Name,
            Category = template.Category,
            Vendor = template.Vendor,
            BusinessCapability = InferBusinessCapability(template.Category, template.Name),
            HostingModel = hostingModel,
            Environment = "Production",
            Criticality = InferCriticality(template.Category, template.Name),
            DataSensitivity = InferDataSensitivity(template.Category, template.Name),
            UserScope = InferUserScope(template.Category, template.Name),
            OwnerDepartmentId = ownerDepartment.Id,
            Url = ResolveApplicationUrl(company, template.Name, template.Vendor, hostingModel, vendorProfile),
            SsoEnabled = template.Category is "Productivity" or "Collaboration" or "Security" or "Analytics",
            MfaRequired = hostingModel == "SaaS" || template.Category is "Security" or "Analytics"
        };
    }

    private IEnumerable<ApplicationRecord> CreateLineOfBusinessApplications(
        Company company,
        ScenarioCompanyDefinition? companyDefinition,
        IReadOnlyList<Department> departments,
        IndustryTaxonomyMatch? taxonomy,
        IReadOnlyList<IndustryApplicationPattern> industryApplicationPatterns,
        IReadOnlyList<CatalogApplicationTemplate> catalogTemplates,
        IReadOnlyList<InternalApplicationPattern> internalPatterns,
        IReadOnlyDictionary<string, VendorProfile> vendorProfiles)
    {
        var industry = companyDefinition?.Industry ?? company.Industry;
        var employeeCount = companyDefinition?.EmployeeCount ?? 0;
        var templates = SelectIndustryApplicationPatterns(industryApplicationPatterns, industry, taxonomy, employeeCount).ToList();
        if (templates.Count == 0)
        {
            templates = BuildFallbackIndustryApplicationTemplates(industry, taxonomy);
        }

        foreach (var template in templates)
        {
            yield return CreateNamedApplication(
                company,
                template.Name.Replace("{Company}", company.Name, StringComparison.OrdinalIgnoreCase),
                template.Category,
                template.HostingModel,
                departments);
        }

        foreach (var template in SelectCatalogApplicationTemplates("Industry", catalogTemplates, industry, taxonomy, employeeCount))
        {
            yield return CreateCatalogApplication(company, template, departments, vendorProfiles);
        }

        foreach (var pattern in SelectInternalApplicationPatterns("Industry", internalPatterns, industry, taxonomy, employeeCount))
        {
            yield return CreateInternalPatternApplication(company, pattern, departments);
        }
    }

    private Department SelectOwnerDepartment(
        string category,
        IReadOnlyList<Department> departments,
        IReadOnlyList<string>? ownerHints = null,
        string? vendor = null,
        IReadOnlyDictionary<string, VendorProfile>? vendorProfiles = null)
    {
        static bool Matches(Department department, params string[] names)
            => names.Any(name => department.Name.Contains(name, StringComparison.OrdinalIgnoreCase));

        if (ownerHints is not null && ownerHints.Count > 0)
        {
            var hintedDepartment = departments.FirstOrDefault(department =>
                ownerHints.Any(hint => department.Name.Contains(hint, StringComparison.OrdinalIgnoreCase)));
            if (hintedDepartment is not null)
            {
                return hintedDepartment;
            }
        }

        var vendorProfile = ResolveVendorProfile(vendor, vendorProfiles);
        if (vendorProfile is not null && vendorProfile.OwnerHints.Count > 0)
        {
            var hintedDepartment = departments.FirstOrDefault(department =>
                vendorProfile.OwnerHints.Any(hint => department.Name.Contains(hint, StringComparison.OrdinalIgnoreCase)));
            if (hintedDepartment is not null)
            {
                return hintedDepartment;
            }
        }

        var preferred = category switch
        {
            "Collaboration" or "Productivity" or "Security" or "Browser" or "Utility" => departments.FirstOrDefault(department => Matches(department, "Information Technology", "Engineering", "Security")),
            "Database" or "Web" or "Developer" => departments.FirstOrDefault(department => Matches(department, "Engineering", "Information Technology")),
            "Analytics" => departments.FirstOrDefault(department => Matches(department, "Finance", "Operations", "Engineering")),
            "Sales" or "CRM" => departments.FirstOrDefault(department => Matches(department, "Sales", "Marketing")),
            "HR" => departments.FirstOrDefault(department => Matches(department, "Human Resources")),
            "Finance" => departments.FirstOrDefault(department => Matches(department, "Finance")),
            _ => departments.FirstOrDefault(department => Matches(department, "Operations", "Information Technology", "Engineering"))
        };

        return preferred ?? departments[_randomSource.Next(departments.Count)];
    }

    private IEnumerable<ApplicationRecord> CreateDepartmentApplications(
        Company company,
        IReadOnlyList<Department> departments,
        string industry,
        IndustryTaxonomyMatch? taxonomy,
        int employeeCount,
        IReadOnlyList<DepartmentApplicationPattern> departmentPatterns)
    {
        foreach (var department in departments
                     .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First()))
        {
            var curatedPatterns = SelectDepartmentApplicationPatterns("Workspace", departmentPatterns, department.Name, industry, taxonomy, employeeCount).ToList();
            if (curatedPatterns.Count > 0)
            {
                foreach (var pattern in curatedPatterns)
                {
                    yield return CreateNamedApplication(
                        company,
                        pattern.Name.Replace("{Company}", company.Name, StringComparison.OrdinalIgnoreCase),
                        pattern.Category,
                        pattern.HostingModel,
                        department);
                }

                continue;
            }

            var applicationName = department.Name switch
            {
                var name when name.Contains("Finance", StringComparison.OrdinalIgnoreCase) => $"{company.Name} Finance Workspace",
                var name when name.Contains("Human Resources", StringComparison.OrdinalIgnoreCase) => $"{company.Name} Talent Hub",
                var name when name.Contains("Information Technology", StringComparison.OrdinalIgnoreCase) => $"{company.Name} IT Service Portal",
                var name when name.Contains("Engineering", StringComparison.OrdinalIgnoreCase) => $"{company.Name} Engineering Tracker",
                var name when name.Contains("Sales", StringComparison.OrdinalIgnoreCase) => $"{company.Name} Sales Pipeline",
                var name when name.Contains("Marketing", StringComparison.OrdinalIgnoreCase) => $"{company.Name} Campaign Studio",
                var name when name.Contains("Operations", StringComparison.OrdinalIgnoreCase) => $"{company.Name} Operations Console",
                var name when name.Contains("Procurement", StringComparison.OrdinalIgnoreCase) => $"{company.Name} Supplier Requests",
                var name when name.Contains("Support", StringComparison.OrdinalIgnoreCase) => $"{company.Name} Service Center",
                var name when name.Contains("Security", StringComparison.OrdinalIgnoreCase) => $"{company.Name} Security Caseboard",
                _ => $"{company.Name} {department.Name} Workspace"
            };

            yield return CreateNamedApplication(
                company,
                applicationName,
                InferDepartmentCategory(department.Name),
                "SaaS",
                department);
        }
    }

    private IEnumerable<ApplicationRecord> CreateDepartmentSpecialtyApplications(
        Company company,
        IReadOnlyList<Department> departments,
        string industry,
        IndustryTaxonomyMatch? taxonomy,
        int employeeCount,
        IReadOnlyList<DepartmentApplicationPattern> departmentPatterns)
    {
        foreach (var department in departments
                     .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First()))
        {
            var curatedPatterns = SelectDepartmentApplicationPatterns("Specialty", departmentPatterns, department.Name, industry, taxonomy, employeeCount).ToList();
            if (curatedPatterns.Count > 0)
            {
                foreach (var pattern in curatedPatterns)
                {
                    yield return CreateNamedApplication(
                        company,
                        pattern.Name.Replace("{Company}", company.Name, StringComparison.OrdinalIgnoreCase),
                        pattern.Category,
                        pattern.HostingModel,
                        department);
                }

                continue;
            }

            if (department.Name.Contains("Finance", StringComparison.OrdinalIgnoreCase))
            {
                yield return CreateNamedApplication(company, $"{company.Name} Expense Approvals", "Finance", "SaaS", department);
                yield return CreateNamedApplication(company, $"{company.Name} Close Calendar", "Finance", "SaaS", department);
                continue;
            }

            if (department.Name.Contains("Human Resources", StringComparison.OrdinalIgnoreCase))
            {
                yield return CreateNamedApplication(company, $"{company.Name} Recruiting Desk", "HR", "SaaS", department);
                yield return CreateNamedApplication(company, $"{company.Name} Learning Portal", "HR", "SaaS", department);
                continue;
            }

            if (department.Name.Contains("Information Technology", StringComparison.OrdinalIgnoreCase))
            {
                yield return CreateNamedApplication(company, $"{company.Name} Change Calendar", "Operations", "SaaS", department);
                yield return CreateNamedApplication(company, $"{company.Name} Endpoint Operations", "Security", "Hybrid", department);
                continue;
            }

            if (department.Name.Contains("Engineering", StringComparison.OrdinalIgnoreCase))
            {
                yield return CreateNamedApplication(company, $"{company.Name} Release Dashboard", "Developer", "Hybrid", department);
                yield return CreateNamedApplication(company, $"{company.Name} Architecture Registry", "Developer", "SaaS", department);
                continue;
            }

            if (department.Name.Contains("Sales", StringComparison.OrdinalIgnoreCase))
            {
                yield return CreateNamedApplication(company, $"{company.Name} Quote Desk", "Sales", "SaaS", department);
                yield return CreateNamedApplication(company, $"{company.Name} Territory Planner", "Sales", "SaaS", department);
                continue;
            }

            if (department.Name.Contains("Marketing", StringComparison.OrdinalIgnoreCase))
            {
                yield return CreateNamedApplication(company, $"{company.Name} Web Content Studio", "Marketing", "SaaS", department);
                yield return CreateNamedApplication(company, $"{company.Name} Event Pipeline", "Marketing", "SaaS", department);
                continue;
            }

            if (department.Name.Contains("Operations", StringComparison.OrdinalIgnoreCase))
            {
                yield return CreateNamedApplication(company, $"{company.Name} Scheduling Board", "Operations", "Hybrid", department);
                yield return CreateNamedApplication(company, $"{company.Name} Dispatch Monitor", "Operations", "Hybrid", department);
                continue;
            }

            if (department.Name.Contains("Procurement", StringComparison.OrdinalIgnoreCase))
            {
                yield return CreateNamedApplication(company, $"{company.Name} Vendor Intake", "Procurement", "SaaS", department);
                yield return CreateNamedApplication(company, $"{company.Name} Purchase Queue", "Procurement", "Hybrid", department);
                continue;
            }

            if (department.Name.Contains("Support", StringComparison.OrdinalIgnoreCase))
            {
                yield return CreateNamedApplication(company, $"{company.Name} Escalation Desk", "Operations", "SaaS", department);
                continue;
            }

            if (department.Name.Contains("Security", StringComparison.OrdinalIgnoreCase))
            {
                yield return CreateNamedApplication(company, $"{company.Name} Threat Intel Workbench", "Security", "Hybrid", department);
                yield return CreateNamedApplication(company, $"{company.Name} Exception Review Board", "Security", "SaaS", department);
            }
        }
    }

    private IEnumerable<ApplicationRecord> CreateEnterprisePlatformApplications(
        Company company,
        IReadOnlyList<Department> departments,
        int employeeCount,
        int officeCount,
        string industry,
        IndustryTaxonomyMatch? taxonomy,
        IReadOnlyList<EnterprisePlatformPattern> enterprisePlatformPatterns,
        IReadOnlyList<CatalogApplicationTemplate> catalogTemplates,
        IReadOnlyList<InternalApplicationPattern> internalPatterns,
        IReadOnlyDictionary<string, VendorProfile> vendorProfiles)
    {
        var selectedPatterns = SelectEnterprisePlatformPatterns(enterprisePlatformPatterns, industry, taxonomy, employeeCount, officeCount).ToList();
        var templates = selectedPatterns.Count > 0
            ? selectedPatterns.Select(pattern => new EnterpriseApplicationTemplate(
                    pattern.Name.Replace("{Company}", company.Name, StringComparison.OrdinalIgnoreCase),
                    pattern.Category,
                    pattern.HostingModel))
                .ToList()
            : BuildFallbackEnterprisePlatformTemplates(company, employeeCount, officeCount, industry, taxonomy);

        var results = templates.Select(template => CreateNamedApplication(
            company,
            template.Name,
            template.Category,
            template.HostingModel,
            departments)).ToList();

        results.AddRange(SelectCatalogApplicationTemplates("Enterprise", catalogTemplates, industry, taxonomy, employeeCount)
            .Select(template => CreateCatalogApplication(company, template, departments, vendorProfiles)));

        results.AddRange(SelectInternalApplicationPatterns("Enterprise", internalPatterns, industry, taxonomy, employeeCount)
            .Select(template => CreateInternalPatternApplication(company, template, departments)));

        return results;
    }

    private static List<EnterpriseApplicationTemplate> BuildFallbackEnterprisePlatformTemplates(
        Company company,
        int employeeCount,
        int officeCount,
        string industry,
        IndustryTaxonomyMatch? taxonomy)
    {
        var templates = new List<EnterpriseApplicationTemplate>
        {
            new($"{company.Name} ERP Core", "Operations", "Hybrid"),
            new($"{company.Name} HRIS", "HR", "SaaS"),
            new($"{company.Name} Identity Portal", "Security", "Hybrid"),
            new($"{company.Name} IT Service Management", "Operations", "SaaS"),
            new($"{company.Name} Data Warehouse", "Analytics", "Hybrid"),
            new($"{company.Name} Document Control", "Productivity", "SaaS")
        };

        if (employeeCount >= 250)
        {
            templates.AddRange(new[]
            {
                new EnterpriseApplicationTemplate($"{company.Name} CRM Hub", "Sales", "SaaS"),
                new EnterpriseApplicationTemplate($"{company.Name} Procurement Portal", "Procurement", "SaaS"),
                new EnterpriseApplicationTemplate($"{company.Name} Expense and Travel", "Finance", "SaaS"),
                new EnterpriseApplicationTemplate($"{company.Name} Learning and Compliance", "HR", "SaaS")
            });
        }

        if (employeeCount >= 750)
        {
            templates.AddRange(new[]
            {
                new EnterpriseApplicationTemplate($"{company.Name} Contract Lifecycle", "Finance", "SaaS"),
                new EnterpriseApplicationTemplate($"{company.Name} Asset Lifecycle", "Operations", "Hybrid"),
                new EnterpriseApplicationTemplate($"{company.Name} Executive Reporting", "Analytics", "SaaS"),
                new EnterpriseApplicationTemplate($"{company.Name} Knowledge Portal", "Productivity", "SaaS")
            });
        }

        if (employeeCount >= 1500 || officeCount >= 3)
        {
            templates.AddRange(new[]
            {
                new EnterpriseApplicationTemplate($"{company.Name} Vendor Risk Exchange", "Security", "SaaS"),
                new EnterpriseApplicationTemplate($"{company.Name} Integration Hub", "Developer", "Hybrid"),
                new EnterpriseApplicationTemplate($"{company.Name} Master Data Governance", "Analytics", "Hybrid"),
                new EnterpriseApplicationTemplate($"{company.Name} Business Continuity Console", "Security", "Hybrid")
            });
        }

        var normalized = string.Join(" ", new[] { industry, taxonomy?.Sector, taxonomy?.IndustryGroup, taxonomy?.Industry, taxonomy?.SubIndustry });
        if (normalized.Contains("manufact", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("industr", StringComparison.OrdinalIgnoreCase))
        {
            templates.AddRange(new[]
            {
                new EnterpriseApplicationTemplate($"{company.Name} Warehouse Management", "Operations", "Hybrid"),
                new EnterpriseApplicationTemplate($"{company.Name} Maintenance Management", "Operations", "Hybrid"),
                new EnterpriseApplicationTemplate($"{company.Name} Product Lifecycle Management", "Developer", "Hybrid"),
                new EnterpriseApplicationTemplate($"{company.Name} Safety and Compliance", "Security", "SaaS")
            });
        }

        return templates;
    }

    private IEnumerable<ApplicationRecord> CreateOfficeApplications(
        Company company,
        IReadOnlyList<Office> offices,
        IReadOnlyList<Department> departments,
        string industry,
        IndustryTaxonomyMatch? taxonomy,
        int employeeCount,
        IReadOnlyList<OfficeApplicationPattern> officePatterns)
    {
        foreach (var office in offices
                     .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First()))
        {
            var sitePrefix = $"{company.Name} {office.City}".Trim();
            var curatedBasePatterns = SelectOfficeApplicationPatterns("Base", officePatterns, industry, taxonomy, employeeCount).ToList();
            var curatedIndustryPatterns = SelectOfficeApplicationPatterns("Industry", officePatterns, industry, taxonomy, employeeCount).ToList();
            var curatedGeneralPatterns = SelectOfficeApplicationPatterns("General", officePatterns, industry, taxonomy, employeeCount).ToList();

            if (curatedBasePatterns.Count > 0 || curatedIndustryPatterns.Count > 0 || curatedGeneralPatterns.Count > 0)
            {
                foreach (var pattern in curatedBasePatterns)
                {
                    yield return CreateNamedApplication(
                        company,
                        ApplyOfficePatternName(pattern.Name, company, office, sitePrefix),
                        pattern.Category,
                        pattern.HostingModel,
                        departments);
                }

                var selectedPatterns = curatedIndustryPatterns.Count > 0 ? curatedIndustryPatterns : curatedGeneralPatterns;
                foreach (var pattern in selectedPatterns)
                {
                    yield return CreateNamedApplication(
                        company,
                        ApplyOfficePatternName(pattern.Name, company, office, sitePrefix),
                        pattern.Category,
                        pattern.HostingModel,
                        departments);
                }

                continue;
            }

            yield return CreateNamedApplication(company, $"{sitePrefix} Workplace Services", "Operations", "SaaS", departments);
            yield return CreateNamedApplication(company, $"{sitePrefix} Visitor Desk", "Security", "SaaS", departments);

            var normalized = string.Join(" ", new[] { industry, taxonomy?.Sector, taxonomy?.IndustryGroup, taxonomy?.Industry, taxonomy?.SubIndustry });
            if (normalized.Contains("manufact", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("industr", StringComparison.OrdinalIgnoreCase))
            {
                yield return CreateNamedApplication(company, $"{sitePrefix} Plant Floor Console", "Operations", "Hybrid", departments);
                yield return CreateNamedApplication(company, $"{sitePrefix} Shipping Gate Scheduler", "Operations", "Hybrid", departments);
                yield return CreateNamedApplication(company, $"{sitePrefix} Safety Observation Center", "Security", "SaaS", departments);
            }
            else
            {
                yield return CreateNamedApplication(company, $"{sitePrefix} Branch Operations Portal", "Operations", "Hybrid", departments);
            }
        }
    }

    private IEnumerable<ApplicationRecord> CreateVendorSuiteApplications(
        Company company,
        IReadOnlyList<Department> departments,
        int employeeCount,
        string industry,
        IndustryTaxonomyMatch? taxonomy,
        IReadOnlyList<ApplicationRecord> currentApplications,
        IReadOnlyList<ApplicationSuiteTemplate> suiteTemplates,
        IReadOnlyDictionary<string, VendorProfile> vendorProfiles)
    {
        if (suiteTemplates.Count == 0 || currentApplications.Count == 0)
        {
            yield break;
        }

        var industryTokens = BuildIndustryTokens(industry, taxonomy);
        var emittedNames = new HashSet<string>(currentApplications.Select(application => application.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var template in suiteTemplates
                     .Where(template => template.MinimumEmployees <= Math.Max(1, employeeCount))
                     .Where(template => template.IndustryTags.Count == 0
                                        || template.IndustryTags.Contains("All", StringComparer.OrdinalIgnoreCase)
                                        || template.IndustryTags.Any(tag => industryTokens.Contains(tag))))
        {
            if (!MatchesApplicationSuite(template, currentApplications))
            {
                continue;
            }

            var applicationName = template.Name.Replace("{Company}", company.Name, StringComparison.OrdinalIgnoreCase);
            if (!emittedNames.Add(applicationName))
            {
                continue;
            }

            yield return CreateCatalogApplication(
                company,
                new CatalogApplicationTemplate(
                    "Suite",
                    template.Name,
                    template.Category,
                    template.Vendor,
                    template.BusinessCapability,
                template.HostingModel,
                template.IndustryTags,
                template.MinimumEmployees,
                template.UserScope,
                template.Criticality,
                template.DataSensitivity,
                Array.Empty<string>()),
                departments,
                vendorProfiles);
        }
    }

    private IEnumerable<ApplicationRecord> CreateIndustrySystemApplications(
        Company company,
        IReadOnlyList<Department> departments,
        int employeeCount,
        string industry,
        IndustryTaxonomyMatch? taxonomy,
        IReadOnlyList<CatalogApplicationTemplate> industrySystemPatterns,
        IReadOnlyDictionary<string, VendorProfile> vendorProfiles)
    {
        foreach (var template in SelectCatalogApplicationTemplates("IndustryPattern", industrySystemPatterns, industry, taxonomy, employeeCount))
        {
            yield return CreateCatalogApplication(company, template, departments, vendorProfiles);
        }
    }

    private ApplicationRecord CreateNamedApplication(
        Company company,
        string applicationName,
        string category,
        string hostingModel,
        IReadOnlyList<Department> departments)
    {
        var ownerDepartment = SelectOwnerDepartment(category, departments);
        return CreateNamedApplication(company, applicationName, category, hostingModel, ownerDepartment);
    }

    private ApplicationRecord CreateNamedApplication(
        Company company,
        string applicationName,
        string category,
        string hostingModel,
        Department ownerDepartment)
    {
        return new ApplicationRecord
        {
            Id = _idFactory.Next("APP"),
            CompanyId = company.Id,
            Name = applicationName,
            Category = category,
            Vendor = company.Name,
            BusinessCapability = InferBusinessCapability(category, applicationName),
            HostingModel = hostingModel,
            Environment = "Production",
            Criticality = InferCriticality(category, applicationName),
            DataSensitivity = InferDataSensitivity(category, applicationName),
            UserScope = InferUserScope(category, applicationName),
            OwnerDepartmentId = ownerDepartment.Id,
            Url = ResolveInternalApplicationUrl(company, applicationName, hostingModel),
            SsoEnabled = true,
            MfaRequired = hostingModel is "SaaS" or "Hybrid" || category is "Security" or "Analytics"
        };
    }

    private ApplicationRecord CreateCatalogApplication(
        Company company,
        CatalogApplicationTemplate template,
        IReadOnlyList<Department> departments,
        IReadOnlyDictionary<string, VendorProfile> vendorProfiles)
    {
        var ownerDepartment = SelectOwnerDepartment(template.Category, departments, template.OwnerHints, template.Vendor, vendorProfiles);
        var applicationName = template.Name.Replace("{Company}", company.Name, StringComparison.OrdinalIgnoreCase);
        var vendorProfile = ResolveVendorProfile(template.Vendor, vendorProfiles);

        return new ApplicationRecord
        {
            Id = _idFactory.Next("APP"),
            CompanyId = company.Id,
            Name = applicationName,
            Category = template.Category,
            Vendor = template.Vendor,
            BusinessCapability = string.IsNullOrWhiteSpace(template.BusinessCapability)
                ? InferBusinessCapability(template.Category, applicationName)
                : template.BusinessCapability,
            HostingModel = template.HostingModel,
            Environment = "Production",
            Criticality = string.IsNullOrWhiteSpace(template.Criticality) ? InferCriticality(template.Category, applicationName) : template.Criticality,
            DataSensitivity = string.IsNullOrWhiteSpace(template.DataSensitivity) ? InferDataSensitivity(template.Category, applicationName) : template.DataSensitivity,
            UserScope = string.IsNullOrWhiteSpace(template.UserScope) ? InferUserScope(template.Category, applicationName) : template.UserScope,
            OwnerDepartmentId = ownerDepartment.Id,
            Url = ResolveApplicationUrl(company, applicationName, template.Vendor, template.HostingModel, vendorProfile),
            SsoEnabled = true,
            MfaRequired = template.HostingModel is "SaaS" or "Hybrid" || template.Category is "Security" or "Analytics"
        };
    }

    private ApplicationRecord CreateInternalPatternApplication(
        Company company,
        InternalApplicationPattern pattern,
        IReadOnlyList<Department> departments)
    {
        var ownerDepartment = SelectOwnerDepartment(pattern.Category, departments, pattern.OwnerHints);
        var applicationName = pattern.Name.Replace("{Company}", company.Name, StringComparison.OrdinalIgnoreCase);

        return new ApplicationRecord
        {
            Id = _idFactory.Next("APP"),
            CompanyId = company.Id,
            Name = applicationName,
            Category = pattern.Category,
            Vendor = company.Name,
            BusinessCapability = string.IsNullOrWhiteSpace(pattern.BusinessCapability)
                ? InferBusinessCapability(pattern.Category, applicationName)
                : pattern.BusinessCapability,
            HostingModel = pattern.HostingModel,
            Environment = "Production",
            Criticality = string.IsNullOrWhiteSpace(pattern.Criticality)
                ? InferCriticality(pattern.Category, applicationName)
                : pattern.Criticality,
            DataSensitivity = string.IsNullOrWhiteSpace(pattern.DataSensitivity)
                ? InferDataSensitivity(pattern.Category, applicationName)
                : pattern.DataSensitivity,
            UserScope = string.IsNullOrWhiteSpace(pattern.UserScope)
                ? InferUserScope(pattern.Category, applicationName)
                : pattern.UserScope,
            OwnerDepartmentId = ownerDepartment.Id,
            Url = ResolveInternalApplicationUrl(company, applicationName, pattern.HostingModel),
            SsoEnabled = true,
            MfaRequired = pattern.HostingModel is "SaaS" or "Hybrid" || pattern.Category is "Security" or "Analytics"
        };
    }

    private IEnumerable<ApplicationDependency> CreateApplicationDependencies(
        Company company,
        IReadOnlyList<ApplicationRecord> applications,
        string industry,
        IndustryTaxonomyMatch? taxonomy,
        int employeeCount,
        IReadOnlyList<ApplicationDependencyPattern> dependencyPatterns)
    {
        if (applications.Count == 0)
        {
            yield break;
        }

        var identityPortal = FindApplication(applications, "Identity Portal");
        var erpCore = FindApplication(applications, "ERP Core");
        var dataWarehouse = FindApplication(applications, "Data Warehouse");
        var crmHub = FindApplication(applications, "CRM Hub");
        var documentControl = FindApplication(applications, "Document Control");
        var itsm = FindApplication(applications, "IT Service Management");

        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var industryTokens = BuildIndustryTokens(industry, taxonomy);

        foreach (var pattern in SelectApplicationDependencyPatterns(dependencyPatterns, industryTokens, employeeCount))
        {
            var sourceApplications = applications
                .Where(application => MatchesDependencyPattern(
                    application,
                    pattern.MatchSourceNameContains,
                    pattern.MatchSourceVendor,
                    pattern.MatchSourceCategory))
                .ToList();
            if (sourceApplications.Count == 0)
            {
                continue;
            }

            var targetApplications = applications
                .Where(application => MatchesDependencyPattern(
                    application,
                    pattern.MatchTargetNameContains,
                    pattern.MatchTargetVendor,
                    pattern.MatchTargetCategory))
                .ToList();
            if (targetApplications.Count == 0)
            {
                continue;
            }

            foreach (var source in sourceApplications)
            {
                foreach (var target in targetApplications.Where(candidate => candidate.Id != source.Id))
                {
                    if (TryCreateDependency(
                            company,
                            source,
                            target,
                            pattern.DependencyType,
                            pattern.InterfaceType,
                            pattern.Criticality,
                            emitted,
                            out var dependency))
                    {
                        yield return dependency;
                    }
                }
            }
        }

        foreach (var app in applications)
        {
            if (identityPortal is not null
                && app.Id != identityPortal.Id
                && app.SsoEnabled)
            {
                if (TryCreateDependency(company, app, identityPortal, "Identity", "SSO", "High", emitted, out var dependency))
                {
                    yield return dependency;
                }
            }

            if (erpCore is not null
                && app.Id != erpCore.Id
                && app.Category is "Finance" or "HR" or "Operations" or "Procurement")
            {
                if (TryCreateDependency(company, app, erpCore, "SystemOfRecord", "API", "High", emitted, out var dependency))
                {
                    yield return dependency;
                }
            }

            if (dataWarehouse is not null
                && app.Id != dataWarehouse.Id
                && app.Category is "Analytics" or "Finance")
            {
                if (TryCreateDependency(company, app, dataWarehouse, "Reporting", "ETL", "Medium", emitted, out var dependency))
                {
                    yield return dependency;
                }
            }

            if (crmHub is not null
                && app.Id != crmHub.Id
                && app.Category is "Sales" or "Marketing")
            {
                if (TryCreateDependency(company, app, crmHub, "CustomerData", "API", "Medium", emitted, out var dependency))
                {
                    yield return dependency;
                }
            }

            if (documentControl is not null
                && app.Id != documentControl.Id
                && app.Name.Contains("Portal", StringComparison.OrdinalIgnoreCase))
            {
                if (TryCreateDependency(company, app, documentControl, "Content", "Web", "Low", emitted, out var dependency))
                {
                    yield return dependency;
                }
            }

            if (itsm is not null
                && app.Id != itsm.Id
                && (app.Name.Contains("Service", StringComparison.OrdinalIgnoreCase)
                    || app.Name.Contains("Operations", StringComparison.OrdinalIgnoreCase)
                    || app.Category is "Security"))
            {
                if (TryCreateDependency(company, app, itsm, "OperationalWorkflow", "API", "Medium", emitted, out var dependency))
                {
                    yield return dependency;
                }
            }
        }
    }

    private static bool MatchesDependencyPattern(
        ApplicationRecord application,
        string? matchNameContains,
        string? matchVendor,
        string? matchCategory)
    {
        if (!string.IsNullOrWhiteSpace(matchNameContains)
            && !application.Name.Contains(matchNameContains, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(matchVendor)
            && !string.Equals(application.Vendor, matchVendor, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(matchCategory)
            && !string.Equals(application.Category, matchCategory, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private string SelectHostingModel(string category, ApplicationProfile profile)
    {
        if (!profile.IncludeSaaSApplications)
        {
            return "OnPrem";
        }

        return category switch
        {
            "Productivity" or "Collaboration" or "Analytics" => "SaaS",
            "Security" => "Hybrid",
            "Database" or "Web" => _randomSource.NextDouble() >= 0.5 ? "OnPrem" : "Hybrid",
            _ => _randomSource.NextDouble() >= 0.6 ? "SaaS" : "Hybrid"
        };
    }

    private List<SoftwareTemplate> SelectSoftwareTemplates(
        IReadOnlyList<SoftwareTemplate> templates,
        int desiredCount,
        string industry,
        IndustryTaxonomyMatch? taxonomy,
        int employeeCount,
        IReadOnlyList<EssentialSoftwarePattern> essentialPatterns)
    {
        var appCandidates = templates
            .Where(IsEnterpriseApplicationCandidate)
            .ToList();

        if (desiredCount <= 0 || appCandidates.Count == 0)
        {
            return new List<SoftwareTemplate>();
        }

        if (appCandidates.Count <= desiredCount)
        {
            return appCandidates.ToList();
        }

        var selected = new List<SoftwareTemplate>();
        var selectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var preferredEssentialNames = SelectEssentialSoftwarePatterns(essentialPatterns, industry, taxonomy, employeeCount)
            .Select(pattern => pattern.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (preferredEssentialNames.Count == 0)
        {
            preferredEssentialNames.AddRange(new[]
            {
                "Microsoft 365 Apps",
                "Microsoft Teams",
                "SQL Server",
                "Splunk Enterprise"
            });
        }

        foreach (var essentialName in preferredEssentialNames)
        {
            var template = appCandidates.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, essentialName, StringComparison.OrdinalIgnoreCase));
            if (template is null || !selectedNames.Add(template.Name))
            {
                continue;
            }

            selected.Add(template);
            if (selected.Count >= desiredCount)
            {
                return selected;
            }
        }

        var remaining = appCandidates
            .Where(template => !selectedNames.Contains(template.Name))
            .ToList();

        foreach (var categoryGroup in remaining
                     .GroupBy(template => template.Category, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (selected.Count >= desiredCount)
            {
                break;
            }

            var categoryOptions = categoryGroup.ToList();
            var template = categoryOptions[_randomSource.Next(categoryOptions.Count)];
            if (!selectedNames.Add(template.Name))
            {
                continue;
            }

            selected.Add(template);
        }

        remaining = remaining
            .Where(template => !selectedNames.Contains(template.Name))
            .ToList();

        while (selected.Count < desiredCount && remaining.Count > 0)
        {
            var index = _randomSource.Next(remaining.Count);
            var template = remaining[index];
            remaining.RemoveAt(index);
            if (!selectedNames.Add(template.Name))
            {
                continue;
            }

            selected.Add(template);
        }

        return selected;
    }

    private static bool IsEnterpriseApplicationCandidate(SoftwareTemplate template)
    {
        if (string.IsNullOrWhiteSpace(template.Name))
        {
            return false;
        }

        if (template.Category is "Browser" or "Utility" or "VPN" or "Backup" or "Developer")
        {
            return false;
        }

        var excludedNameTokens = new[]
        {
            "Sync Client",
            "Agent",
            "Plugin",
            "CLI",
            "Tools",
            "Tooling",
            "Backup",
            "Updater",
            "VMware Tools",
            "Server Backup",
            "Power BI Desktop",
            "Visual Studio Code",
            "Docker Desktop",
            "Citrix Workspace",
            "Workday Mobile",
            "Postman",
            "IIS",
            "Forwarder",
            "Drive",
            "Acrobat",
            "Desktop",
            "Sync"
        };

        return !excludedNameTokens.Any(token => template.Name.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static List<SoftwareTemplate> ReadSoftwareCatalog(CatalogSet catalogs)
    {
        if (catalogs.CsvCatalogs.TryGetValue("software_catalog", out var rows))
        {
            var templates = rows
                .Where(row => row.TryGetValue("Name", out var name) && !string.IsNullOrWhiteSpace(name))
                .Select(row => new SoftwareTemplate(
                    row["Name"] ?? "Application",
                    row.TryGetValue("Category", out var category) && !string.IsNullOrWhiteSpace(category) ? category! : "General",
                    row.TryGetValue("Vendor", out var vendor) && !string.IsNullOrWhiteSpace(vendor) ? vendor! : "Unknown"))
                .GroupBy(template => template.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            if (templates.Count > 0)
            {
                return templates;
            }
        }

        return new List<SoftwareTemplate>
        {
            new("Microsoft 365 Apps", "Productivity", "Microsoft"),
            new("Microsoft Teams", "Collaboration", "Microsoft"),
            new("Power BI Desktop", "Analytics", "Microsoft"),
            new("SQL Server", "Database", "Microsoft"),
            new("IIS", "Web", "Microsoft"),
            new("CrowdStrike Falcon", "Security", "CrowdStrike")
        };
    }

    private static IReadOnlyList<EssentialSoftwarePattern> ReadEssentialSoftwarePatterns(CatalogSet catalogs)
    {
        if (!catalogs.CsvCatalogs.TryGetValue("essential_software_patterns", out var rows))
        {
            return Array.Empty<EssentialSoftwarePattern>();
        }

        return rows
            .Where(row => !string.IsNullOrWhiteSpace(Read(row, "Name")))
            .Select(row => new EssentialSoftwarePattern(
                Read(row, "Name"),
                SplitPipeSeparated(Read(row, "IndustryTags")),
                int.TryParse(Read(row, "MinimumEmployees"), out var minimumEmployees) ? minimumEmployees : 0,
                int.TryParse(Read(row, "Priority"), out var priority) ? priority : int.MaxValue))
            .ToList();
    }

    private static IReadOnlyList<EnterprisePlatformPattern> ReadEnterprisePlatformPatterns(CatalogSet catalogs)
    {
        if (!catalogs.CsvCatalogs.TryGetValue("enterprise_platform_patterns", out var rows))
        {
            return Array.Empty<EnterprisePlatformPattern>();
        }

        return rows
            .Where(row => !string.IsNullOrWhiteSpace(Read(row, "Name")))
            .Select(row => new EnterprisePlatformPattern(
                SplitPipeSeparated(Read(row, "IndustryTags")),
                int.TryParse(Read(row, "MinimumEmployees"), out var minimumEmployees) ? minimumEmployees : 0,
                int.TryParse(Read(row, "MinimumOfficeCount"), out var minimumOfficeCount) ? minimumOfficeCount : 0,
                Read(row, "Name"),
                Read(row, "Category"),
                Read(row, "HostingModel")))
            .ToList();
    }

    private static IReadOnlyList<CatalogApplicationTemplate> ReadApplicationTemplates(CatalogSet catalogs)
    {
        if (!catalogs.CsvCatalogs.TryGetValue("application_templates", out var rows))
        {
            return Array.Empty<CatalogApplicationTemplate>();
        }

        return rows
            .Where(row => !string.IsNullOrWhiteSpace(Read(row, "Name")))
            .Select(row => new CatalogApplicationTemplate(
                Read(row, "TemplateType"),
                Read(row, "Name"),
                Read(row, "Category"),
                Read(row, "Vendor"),
                Read(row, "BusinessCapability"),
                Read(row, "HostingModel"),
                Read(row, "IndustryTags"),
                int.TryParse(Read(row, "MinimumEmployees"), out var minimumEmployees) ? minimumEmployees : 0,
                Read(row, "UserScope"),
                Read(row, "Criticality"),
                Read(row, "DataSensitivity"),
                Read(row, "OwnerHints")))
            .ToList();
    }

    private static IReadOnlyList<CatalogApplicationTemplate> ReadIndustrySystemPatterns(CatalogSet catalogs)
    {
        if (!catalogs.CsvCatalogs.TryGetValue("industry_system_patterns", out var rows))
        {
            return Array.Empty<CatalogApplicationTemplate>();
        }

        return rows
            .Where(row => !string.IsNullOrWhiteSpace(Read(row, "Name")))
            .Select(row => new CatalogApplicationTemplate(
                "IndustryPattern",
                Read(row, "Name"),
                Read(row, "Category"),
                Read(row, "Vendor"),
                Read(row, "BusinessCapability"),
                Read(row, "HostingModel"),
                Read(row, "IndustryTags"),
                int.TryParse(Read(row, "MinimumEmployees"), out var minimumEmployees) ? minimumEmployees : 0,
                Read(row, "UserScope"),
                Read(row, "Criticality"),
                Read(row, "DataSensitivity"),
                Read(row, "OwnerHints")))
            .ToList();
    }

    private static IReadOnlyList<IndustryApplicationPattern> ReadIndustryApplicationPatterns(CatalogSet catalogs)
    {
        if (!catalogs.CsvCatalogs.TryGetValue("industry_application_patterns", out var rows))
        {
            return Array.Empty<IndustryApplicationPattern>();
        }

        return rows
            .Where(row => !string.IsNullOrWhiteSpace(Read(row, "Name")))
            .Select(row => new IndustryApplicationPattern(
                SplitPipeSeparated(Read(row, "IndustryTags")),
                int.TryParse(Read(row, "MinimumEmployees"), out var minimumEmployees) ? minimumEmployees : 0,
                Read(row, "Name"),
                Read(row, "Category"),
                Read(row, "HostingModel")))
            .ToList();
    }

    private static IReadOnlyList<InternalApplicationPattern> ReadInternalApplicationPatterns(CatalogSet catalogs)
    {
        if (!catalogs.CsvCatalogs.TryGetValue("internal_application_patterns", out var rows))
        {
            return Array.Empty<InternalApplicationPattern>();
        }

        return rows
            .Where(row => !string.IsNullOrWhiteSpace(Read(row, "Name")))
            .Select(row => new InternalApplicationPattern(
                Read(row, "PatternType"),
                Read(row, "Name"),
                Read(row, "Category"),
                Read(row, "HostingModel"),
                SplitPipeSeparated(Read(row, "IndustryTags")),
                int.TryParse(Read(row, "MinimumEmployees"), out var minimumEmployees) ? minimumEmployees : 0,
                Read(row, "UserScope"),
                Read(row, "Criticality"),
                Read(row, "DataSensitivity"),
                SplitPipeSeparated(Read(row, "OwnerHints")),
                Read(row, "BusinessCapability")))
            .ToList();
    }

    private static IReadOnlyList<DepartmentApplicationPattern> ReadDepartmentApplicationPatterns(CatalogSet catalogs)
    {
        if (!catalogs.CsvCatalogs.TryGetValue("department_application_patterns", out var rows))
        {
            return Array.Empty<DepartmentApplicationPattern>();
        }

        return rows
            .Where(row =>
                !string.IsNullOrWhiteSpace(Read(row, "PatternType"))
                && !string.IsNullOrWhiteSpace(Read(row, "DepartmentMatch"))
                && !string.IsNullOrWhiteSpace(Read(row, "Name")))
            .Select(row => new DepartmentApplicationPattern(
                Read(row, "PatternType"),
                Read(row, "DepartmentMatch"),
                SplitPipeSeparated(Read(row, "IndustryTags")),
                int.TryParse(Read(row, "MinimumEmployees"), out var minimumEmployees) ? minimumEmployees : 0,
                Read(row, "Name"),
                Read(row, "Category"),
                Read(row, "HostingModel")))
            .ToList();
    }

    private static IReadOnlyList<OfficeApplicationPattern> ReadOfficeApplicationPatterns(CatalogSet catalogs)
    {
        if (!catalogs.CsvCatalogs.TryGetValue("office_application_patterns", out var rows))
        {
            return Array.Empty<OfficeApplicationPattern>();
        }

        return rows
            .Where(row =>
                !string.IsNullOrWhiteSpace(Read(row, "PatternType"))
                && !string.IsNullOrWhiteSpace(Read(row, "Name")))
            .Select(row => new OfficeApplicationPattern(
                Read(row, "PatternType"),
                SplitPipeSeparated(Read(row, "IndustryTags")),
                int.TryParse(Read(row, "MinimumEmployees"), out var minimumEmployees) ? minimumEmployees : 0,
                Read(row, "Name"),
                Read(row, "Category"),
                Read(row, "HostingModel")))
            .ToList();
    }

    private static IReadOnlyList<ApplicationDependencyPattern> ReadApplicationDependencyPatterns(CatalogSet catalogs)
    {
        if (!catalogs.CsvCatalogs.TryGetValue("application_dependency_patterns", out var rows))
        {
            return Array.Empty<ApplicationDependencyPattern>();
        }

        return rows
            .Where(row => !string.IsNullOrWhiteSpace(Read(row, "DependencyType")))
            .Select(row => new ApplicationDependencyPattern(
                Read(row, "MatchSourceNameContains"),
                Read(row, "MatchSourceVendor"),
                Read(row, "MatchSourceCategory"),
                Read(row, "MatchTargetNameContains"),
                Read(row, "MatchTargetVendor"),
                Read(row, "MatchTargetCategory"),
                SplitPipeSeparated(Read(row, "IndustryTags")),
                int.TryParse(Read(row, "MinimumEmployees"), out var minimumEmployees) ? minimumEmployees : 0,
                Read(row, "DependencyType"),
                Read(row, "InterfaceType"),
                Read(row, "Criticality")))
            .ToList();
    }

    private static IReadOnlyList<ApplicationSuiteTemplate> ReadApplicationSuiteTemplates(CatalogSet catalogs)
    {
        if (!catalogs.CsvCatalogs.TryGetValue("application_suite_templates", out var rows))
        {
            return Array.Empty<ApplicationSuiteTemplate>();
        }

        return rows
            .Where(row => !string.IsNullOrWhiteSpace(Read(row, "Name")))
            .Select(row => new ApplicationSuiteTemplate(
                Read(row, "MatchVendor"),
                Read(row, "MatchCategory"),
                Read(row, "MatchNameContains"),
                SplitPipeSeparated(Read(row, "IndustryTags")),
                int.TryParse(Read(row, "MinimumEmployees"), out var minimumEmployees) ? minimumEmployees : 0,
                Read(row, "Name"),
                Read(row, "Category"),
                Read(row, "Vendor"),
                Read(row, "BusinessCapability"),
                Read(row, "HostingModel"),
                Read(row, "UserScope"),
                Read(row, "Criticality"),
                Read(row, "DataSensitivity")))
            .ToList();
    }

    private static IReadOnlyDictionary<string, VendorProfile> ReadVendorProfiles(CatalogSet catalogs)
    {
        if (!catalogs.CsvCatalogs.TryGetValue("vendor_reference", out var rows))
        {
            return new Dictionary<string, VendorProfile>(StringComparer.OrdinalIgnoreCase);
        }

        var profiles = rows
            .Where(row => row.TryGetValue("Name", out var name) && !string.IsNullOrWhiteSpace(name))
            .Select(row => new VendorProfile(
                row["Name"] ?? string.Empty,
                row.TryGetValue("PrimaryDomain", out var primaryDomain) ? primaryDomain : null,
                SplitPipeSeparated(row.TryGetValue("OwnerHints", out var ownerHints) ? ownerHints : null)))
            .GroupBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        if (catalogs.CsvCatalogs.TryGetValue("vendor_aliases", out var aliasRows))
        {
            foreach (var row in aliasRows)
            {
                var alias = Read(row, "Alias");
                var canonicalName = Read(row, "CanonicalName");
                if (string.IsNullOrWhiteSpace(alias)
                    || string.IsNullOrWhiteSpace(canonicalName)
                    || profiles.ContainsKey(alias)
                    || !profiles.TryGetValue(canonicalName, out var canonicalProfile))
                {
                    continue;
                }

                profiles[alias] = canonicalProfile;
            }
        }

        return profiles;
    }

    private static IEnumerable<InternalApplicationPattern> SelectInternalApplicationPatterns(
        string patternType,
        IReadOnlyList<InternalApplicationPattern> patterns,
        string industry,
        IndustryTaxonomyMatch? taxonomy,
        int employeeCount)
    {
        var industryTokens = BuildIndustryTokens(industry, taxonomy);

        return patterns.Where(pattern =>
            string.Equals(pattern.PatternType, patternType, StringComparison.OrdinalIgnoreCase)
            && pattern.MinimumEmployees <= Math.Max(1, employeeCount)
            && (pattern.IndustryTags.Count == 0
                || pattern.IndustryTags.Contains("All", StringComparer.OrdinalIgnoreCase)
                || pattern.IndustryTags.Any(tag => industryTokens.Contains(tag))));
    }

    private static IEnumerable<DepartmentApplicationPattern> SelectDepartmentApplicationPatterns(
        string patternType,
        IReadOnlyList<DepartmentApplicationPattern> patterns,
        string departmentName,
        string industry,
        IndustryTaxonomyMatch? taxonomy,
        int employeeCount)
    {
        var industryTokens = BuildIndustryTokens(industry, taxonomy);

        return patterns.Where(pattern =>
            string.Equals(pattern.PatternType, patternType, StringComparison.OrdinalIgnoreCase)
            && departmentName.Contains(pattern.DepartmentMatch, StringComparison.OrdinalIgnoreCase)
            && pattern.MinimumEmployees <= Math.Max(1, employeeCount)
            && (pattern.IndustryTags.Count == 0
                || pattern.IndustryTags.Contains("All", StringComparer.OrdinalIgnoreCase)
                || pattern.IndustryTags.Any(tag => industryTokens.Contains(tag))));
    }

    private static IEnumerable<OfficeApplicationPattern> SelectOfficeApplicationPatterns(
        string patternType,
        IReadOnlyList<OfficeApplicationPattern> patterns,
        string industry,
        IndustryTaxonomyMatch? taxonomy,
        int employeeCount)
    {
        var industryTokens = BuildIndustryTokens(industry, taxonomy);

        return patterns.Where(pattern =>
            string.Equals(pattern.PatternType, patternType, StringComparison.OrdinalIgnoreCase)
            && pattern.MinimumEmployees <= Math.Max(1, employeeCount)
            && (pattern.IndustryTags.Count == 0
                || pattern.IndustryTags.Contains("All", StringComparer.OrdinalIgnoreCase)
                || pattern.IndustryTags.Any(tag => industryTokens.Contains(tag))));
    }

    private static IEnumerable<EnterprisePlatformPattern> SelectEnterprisePlatformPatterns(
        IReadOnlyList<EnterprisePlatformPattern> patterns,
        string industry,
        IndustryTaxonomyMatch? taxonomy,
        int employeeCount,
        int officeCount)
    {
        var industryTokens = BuildIndustryTokens(industry, taxonomy);

        return patterns.Where(pattern =>
            pattern.MinimumEmployees <= Math.Max(1, employeeCount)
            && officeCount >= pattern.MinimumOfficeCount
            && (pattern.IndustryTags.Count == 0
                || pattern.IndustryTags.Contains("All", StringComparer.OrdinalIgnoreCase)
                || pattern.IndustryTags.Any(tag => industryTokens.Contains(tag))));
    }

    private static List<IndustryApplicationTemplate> SelectIndustryApplicationPatterns(
        IReadOnlyList<IndustryApplicationPattern> patterns,
        string industry,
        IndustryTaxonomyMatch? taxonomy,
        int employeeCount)
    {
        var industryTokens = BuildIndustryTokens(industry, taxonomy);

        return patterns
            .Where(pattern =>
                pattern.MinimumEmployees <= Math.Max(0, employeeCount)
                && (pattern.IndustryTags.Count == 0
                    || pattern.IndustryTags.Contains("All", StringComparer.OrdinalIgnoreCase)
                    || pattern.IndustryTags.Any(tag => industryTokens.Contains(tag))))
            .Select(pattern => new IndustryApplicationTemplate(
                pattern.Name,
                pattern.Category,
                pattern.HostingModel))
            .ToList();
    }

    private static IEnumerable<ApplicationDependencyPattern> SelectApplicationDependencyPatterns(
        IReadOnlyList<ApplicationDependencyPattern> patterns,
        IReadOnlySet<string> industryTokens,
        int employeeCount)
    {
        return patterns.Where(pattern =>
            pattern.MinimumEmployees <= Math.Max(1, employeeCount)
            && (pattern.IndustryTags.Count == 0
                || pattern.IndustryTags.Contains("All", StringComparer.OrdinalIgnoreCase)
                || pattern.IndustryTags.Any(tag => industryTokens.Contains(tag))));
    }

    private static IEnumerable<EssentialSoftwarePattern> SelectEssentialSoftwarePatterns(
        IReadOnlyList<EssentialSoftwarePattern> patterns,
        string industry,
        IndustryTaxonomyMatch? taxonomy,
        int employeeCount)
    {
        var industryTokens = BuildIndustryTokens(industry, taxonomy);

        return patterns
            .Where(pattern =>
                pattern.MinimumEmployees <= Math.Max(1, employeeCount)
                && (pattern.IndustryTags.Count == 0
                    || pattern.IndustryTags.Contains("All", StringComparer.OrdinalIgnoreCase)
                    || pattern.IndustryTags.Any(tag => industryTokens.Contains(tag))))
            .OrderBy(pattern => pattern.Priority)
            .ThenBy(pattern => pattern.Name, StringComparer.OrdinalIgnoreCase);
    }

    private static VendorProfile? ResolveVendorProfile(string? vendor, IReadOnlyDictionary<string, VendorProfile>? vendorProfiles)
    {
        if (string.IsNullOrWhiteSpace(vendor) || vendorProfiles is null)
        {
            return null;
        }

        return vendorProfiles.TryGetValue(vendor, out var vendorProfile) ? vendorProfile : null;
    }

    private static string ResolveApplicationUrl(
        Company company,
        string applicationName,
        string? vendor,
        string hostingModel,
        VendorProfile? vendorProfile)
    {
        var appSlug = Slugify(applicationName);
        var internalAppSlug = BuildInternalApplicationSlug(company, applicationName);

        if (string.Equals(hostingModel, "OnPrem", StringComparison.OrdinalIgnoreCase))
        {
            return $"https://{internalAppSlug}.{ShortCompanyKey(company)}.internal";
        }

        if (!string.IsNullOrWhiteSpace(vendorProfile?.PrimaryDomain) &&
            !string.Equals(vendor, company.Name, StringComparison.OrdinalIgnoreCase))
        {
            return $"https://{vendorProfile.PrimaryDomain.TrimEnd('/')}/{appSlug}";
        }

        var inferredVendorDomain = InferVendorDomain(vendor, company);
        if (!string.IsNullOrWhiteSpace(inferredVendorDomain))
        {
            return $"https://{inferredVendorDomain}/{appSlug}";
        }

        return ResolveInternalApplicationUrl(company, applicationName, hostingModel);
    }

    private static string ResolveInternalApplicationUrl(Company company, string applicationName, string hostingModel)
    {
        var appSlug = BuildInternalApplicationSlug(company, applicationName);
        var companySlug = ShortCompanyKey(company);

        if (string.Equals(hostingModel, "OnPrem", StringComparison.OrdinalIgnoreCase))
        {
            return $"https://{appSlug}.{companySlug}.internal";
        }

        if (!string.IsNullOrWhiteSpace(company.PrimaryDomain))
        {
            return $"https://{appSlug}.{company.PrimaryDomain}";
        }

        return $"https://{appSlug}.{companySlug}.apps.test";
    }

    private static string BuildInternalApplicationSlug(Company company, string applicationName)
    {
        var appSpecificName = StripCompanyPrefix(applicationName, company.Name);
        appSpecificName = StripCompanyPrefix(appSpecificName, ShortCompanyKey(company));

        var appSlug = Slugify(appSpecificName);
        return string.Equals(appSlug, "app", StringComparison.OrdinalIgnoreCase)
            ? Slugify(applicationName)
            : appSlug;
    }

    private static string StripCompanyPrefix(string value, string? prefix)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(prefix))
        {
            return value;
        }

        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        return value[prefix.Length..].TrimStart(' ', '-', '_', '.', ':', '/');
    }

    private static string ApplyOfficePatternName(string template, Company company, Office office, string sitePrefix) =>
        template
            .Replace("{SitePrefix}", sitePrefix, StringComparison.OrdinalIgnoreCase)
            .Replace("{Company}", company.Name, StringComparison.OrdinalIgnoreCase)
            .Replace("{City}", office.City, StringComparison.OrdinalIgnoreCase);

    private static string ShortCompanyKey(Company company)
    {
        if (!string.IsNullOrWhiteSpace(company.PrimaryDomain))
        {
            return company.PrimaryDomain.Split('.', StringSplitOptions.RemoveEmptyEntries)[0];
        }

        return Slugify(company.Name);
    }

    private static string? InferVendorDomain(string? vendor, Company company)
    {
        if (string.IsNullOrWhiteSpace(vendor)
            || string.Equals(vendor, company.Name, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var slug = Slugify(vendor);
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        return $"{slug}.com";
    }

    private static List<string> SplitPipeSeparated(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? new List<string>()
            : value
                .Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

    private static IEnumerable<CatalogApplicationTemplate> SelectCatalogApplicationTemplates(
        string templateType,
        IReadOnlyList<CatalogApplicationTemplate> templates,
        string industry,
        IndustryTaxonomyMatch? taxonomy,
        int employeeCount)
    {
        var industryTokens = BuildIndustryTokens(industry, taxonomy);

        return templates.Where(template =>
            string.Equals(template.TemplateType, templateType, StringComparison.OrdinalIgnoreCase)
            && template.MinimumEmployees <= Math.Max(1, employeeCount)
            && (template.IndustryTags.Count == 0
                || template.IndustryTags.Contains("All", StringComparer.OrdinalIgnoreCase)
                || template.IndustryTags.Any(tag => industryTokens.Contains(tag))));
    }

    private static bool MatchesApplicationSuite(ApplicationSuiteTemplate template, IReadOnlyList<ApplicationRecord> currentApplications)
    {
        return currentApplications.Any(application =>
            (string.IsNullOrWhiteSpace(template.MatchVendor)
             || string.Equals(application.Vendor, template.MatchVendor, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(template.MatchCategory)
                || string.Equals(application.Category, template.MatchCategory, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(template.MatchNameContains)
                || application.Name.Contains(template.MatchNameContains, StringComparison.OrdinalIgnoreCase)));
    }

    private static List<IndustryApplicationTemplate> BuildFallbackIndustryApplicationTemplates(string industry, IndustryTaxonomyMatch? taxonomy)
    {
        var sector = taxonomy?.Sector ?? string.Empty;
        var industryGroup = taxonomy?.IndustryGroup ?? string.Empty;
        var normalized = string.Join(" ", new[] { industry, sector, industryGroup });

        if (normalized.Contains("industr", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("manufact", StringComparison.OrdinalIgnoreCase))
        {
            return new List<IndustryApplicationTemplate>
            {
                new IndustryApplicationTemplate("{Company} Production Planning", "Operations", "Hybrid"),
                new IndustryApplicationTemplate("{Company} Quality Portal", "Operations", "SaaS"),
                new IndustryApplicationTemplate("{Company} Supplier Exchange", "Procurement", "SaaS")
            };
        }

        if (normalized.Contains("health", StringComparison.OrdinalIgnoreCase))
        {
            return new List<IndustryApplicationTemplate>
            {
                new IndustryApplicationTemplate("{Company} Care Operations", "Operations", "Hybrid"),
                new IndustryApplicationTemplate("{Company} Compliance Tracker", "Security", "SaaS")
            };
        }

        if (normalized.Contains("finance", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("bank", StringComparison.OrdinalIgnoreCase))
        {
            return new List<IndustryApplicationTemplate>
            {
                new IndustryApplicationTemplate("{Company} Treasury Workspace", "Finance", "Hybrid"),
                new IndustryApplicationTemplate("{Company} Client Reporting Hub", "Analytics", "SaaS")
            };
        }

        if (normalized.Contains("technology", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("information technology", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("software", StringComparison.OrdinalIgnoreCase))
        {
            return new List<IndustryApplicationTemplate>
            {
                new IndustryApplicationTemplate("{Company} Engineering Hub", "Engineering", "SaaS"),
                new IndustryApplicationTemplate("{Company} Release Control", "Engineering", "Hybrid"),
                new IndustryApplicationTemplate("{Company} Customer Admin", "Operations", "SaaS")
            };
        }

        if (normalized.Contains("communication", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("telecom", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("media", StringComparison.OrdinalIgnoreCase))
        {
            return new List<IndustryApplicationTemplate>
            {
                new IndustryApplicationTemplate("{Company} Network Operations Console", "Operations", "Hybrid"),
                new IndustryApplicationTemplate("{Company} Subscriber Care", "Operations", "SaaS")
            };
        }

        if (industry.Contains("Manufact", StringComparison.OrdinalIgnoreCase))
        {
            return new List<IndustryApplicationTemplate>
            {
                new IndustryApplicationTemplate("{Company} Production Planning", "Operations", "Hybrid"),
                new IndustryApplicationTemplate("{Company} Quality Portal", "Operations", "SaaS"),
                new IndustryApplicationTemplate("{Company} Supplier Exchange", "Procurement", "SaaS")
            };
        }

        if (industry.Contains("Health", StringComparison.OrdinalIgnoreCase))
        {
            return new List<IndustryApplicationTemplate>
            {
                new IndustryApplicationTemplate("{Company} Care Operations", "Operations", "Hybrid"),
                new IndustryApplicationTemplate("{Company} Compliance Tracker", "Security", "SaaS")
            };
        }

        if (industry.Contains("Finance", StringComparison.OrdinalIgnoreCase) || industry.Contains("Bank", StringComparison.OrdinalIgnoreCase))
        {
            return new List<IndustryApplicationTemplate>
            {
                new IndustryApplicationTemplate("{Company} Treasury Workspace", "Finance", "Hybrid"),
                new IndustryApplicationTemplate("{Company} Client Reporting Hub", "Analytics", "SaaS")
            };
        }

        return new List<IndustryApplicationTemplate>
        {
            new IndustryApplicationTemplate("{Company} Operations Portal", "Operations", "Hybrid"),
            new IndustryApplicationTemplate("{Company} Reporting Hub", "Analytics", "SaaS")
        };
    }

    private static string ResolveCompanySizeBand(int employeeCount, CatalogSet catalogs)
    {
        if (catalogs.CsvCatalogs.TryGetValue("company_size_bands", out var rows))
        {
            foreach (var row in rows)
            {
                if (!int.TryParse(Read(row, "EmployeeMin"), out var min)
                    || !int.TryParse(Read(row, "EmployeeMax"), out var max))
                {
                    continue;
                }

                if (employeeCount >= min && employeeCount <= max)
                {
                    return Read(row, "Size");
                }
            }
        }

        // Prefer curated company-size bands when present so growth in the catalog immediately improves scaling.
        return "Unknown";
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

    private static string Slugify(string value)
    {
        var chars = value
            .ToLowerInvariant()
            .Where(character => char.IsLetterOrDigit(character))
            .ToArray();
        return chars.Length == 0 ? "app" : new string(chars);
    }

    private static string InferDepartmentCategory(string departmentName)
    {
        if (departmentName.Contains("Finance", StringComparison.OrdinalIgnoreCase))
        {
            return "Finance";
        }

        if (departmentName.Contains("Human Resources", StringComparison.OrdinalIgnoreCase))
        {
            return "HR";
        }

        if (departmentName.Contains("Sales", StringComparison.OrdinalIgnoreCase))
        {
            return "Sales";
        }

        if (departmentName.Contains("Marketing", StringComparison.OrdinalIgnoreCase))
        {
            return "Marketing";
        }

        if (departmentName.Contains("Engineering", StringComparison.OrdinalIgnoreCase))
        {
            return "Developer";
        }

        if (departmentName.Contains("Security", StringComparison.OrdinalIgnoreCase))
        {
            return "Security";
        }

        return "Operations";
    }

    private static string InferBusinessCapability(string category, string applicationName)
    {
        if (applicationName.Contains("ERP", StringComparison.OrdinalIgnoreCase))
        {
            return "Enterprise Resource Planning";
        }

        if (applicationName.Contains("CRM", StringComparison.OrdinalIgnoreCase))
        {
            return "Customer Relationship Management";
        }

        if (applicationName.Contains("Identity", StringComparison.OrdinalIgnoreCase))
        {
            return "Identity and Access";
        }

        return category switch
        {
            "Finance" => "Finance Operations",
            "HR" => "Workforce Management",
            "Sales" => "Revenue Operations",
            "Marketing" => "Demand Generation",
            "Security" => "Security Operations",
            "Analytics" => "Business Intelligence",
            "Developer" => "Engineering Delivery",
            "Operations" => "Operational Control",
            "Procurement" => "Supplier Management",
            "Productivity" => "Workplace Productivity",
            "Collaboration" => "Workplace Collaboration",
            "Database" => "Data Management",
            "Web" => "Digital Presence",
            _ => "Business Operations"
        };
    }

    private static string InferCriticality(string category, string applicationName)
    {
        if (applicationName.Contains("ERP", StringComparison.OrdinalIgnoreCase)
            || applicationName.Contains("Identity", StringComparison.OrdinalIgnoreCase)
            || applicationName.Contains("Warehouse", StringComparison.OrdinalIgnoreCase)
            || applicationName.Contains("Production", StringComparison.OrdinalIgnoreCase))
        {
            return "High";
        }

        return category switch
        {
            "Finance" or "Security" or "Operations" => "High",
            "Analytics" or "HR" or "Procurement" or "Database" => "Medium",
            _ => "Medium"
        };
    }

    private static string InferDataSensitivity(string category, string applicationName)
    {
        if (applicationName.Contains("Identity", StringComparison.OrdinalIgnoreCase)
            || applicationName.Contains("Security", StringComparison.OrdinalIgnoreCase))
        {
            return "Restricted";
        }

        return category switch
        {
            "Finance" or "HR" or "Security" => "Confidential",
            "Analytics" or "Procurement" or "Sales" => "Internal",
            _ => "Internal"
        };
    }

    private static string InferUserScope(string category, string applicationName)
    {
        if (applicationName.Contains("Executive", StringComparison.OrdinalIgnoreCase))
        {
            return "Executive";
        }

        if (applicationName.Contains("Site", StringComparison.OrdinalIgnoreCase)
            || applicationName.Contains("Plant", StringComparison.OrdinalIgnoreCase)
            || applicationName.Contains("Visitor", StringComparison.OrdinalIgnoreCase))
        {
            return "Office";
        }

        return category switch
        {
            "Finance" or "HR" or "Procurement" => "Department",
            "Sales" or "Marketing" => "BusinessUnit",
            "Operations" => "Enterprise",
            _ => "Enterprise"
        };
    }

    private static ApplicationRecord? FindApplication(IReadOnlyList<ApplicationRecord> applications, string nameFragment)
        => applications.FirstOrDefault(app => app.Name.Contains(nameFragment, StringComparison.OrdinalIgnoreCase));

    private bool TryCreateDependency(
        Company company,
        ApplicationRecord source,
        ApplicationRecord target,
        string dependencyType,
        string interfaceType,
        string criticality,
        ISet<string> emitted,
        out ApplicationDependency dependency)
    {
        dependency = default!;
        var key = $"{source.Id}|{target.Id}|{dependencyType}|{interfaceType}";
        if (!emitted.Add(key))
        {
            return false;
        }

        dependency = new ApplicationDependency
        {
            Id = _idFactory.Next("APPDEP"),
            CompanyId = company.Id,
            SourceApplicationId = source.Id,
            TargetApplicationId = target.Id,
            DependencyType = dependencyType,
            InterfaceType = interfaceType,
            Criticality = criticality
        };

        return true;
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

    private static string Read(IReadOnlyDictionary<string, string?> row, string key)
        => row.TryGetValue(key, out var value) ? value ?? string.Empty : string.Empty;

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

        foreach (var token in raw
                     .Split(['|', ',', '/', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            values.Add(token);
        }
    }

    private sealed record SoftwareTemplate(string Name, string Category, string Vendor);
    private sealed record EssentialSoftwarePattern(
        string Name,
        IReadOnlyList<string> IndustryTags,
        int MinimumEmployees,
        int Priority);
    private sealed record IndustryApplicationTemplate(string Name, string Category, string HostingModel);
    private sealed record EnterpriseApplicationTemplate(string Name, string Category, string HostingModel);
    private sealed record IndustryTaxonomyMatch(string Sector, string IndustryGroup, string Industry, string SubIndustry);
    private sealed record CatalogApplicationTemplate(
        string TemplateType,
        string Name,
        string Category,
        string Vendor,
        string BusinessCapability,
        string HostingModel,
        IReadOnlyList<string> IndustryTags,
        int MinimumEmployees,
        string UserScope,
        string Criticality,
        string DataSensitivity,
        IReadOnlyList<string> OwnerHints)
    {
        public CatalogApplicationTemplate(
            string templateType,
            string name,
            string category,
            string vendor,
            string businessCapability,
            string hostingModel,
            string industryTags,
            int minimumEmployees,
            string userScope,
            string criticality,
            string dataSensitivity,
            string ownerHints)
            : this(
                templateType,
                name,
                category,
                vendor,
                businessCapability,
                hostingModel,
                industryTags.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                minimumEmployees,
                userScope,
                criticality,
                dataSensitivity,
                ownerHints.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
        }
    }

    private sealed record VendorProfile(
        string Name,
        string? PrimaryDomain,
        IReadOnlyList<string> OwnerHints);

    private sealed record InternalApplicationPattern(
        string PatternType,
        string Name,
        string Category,
        string HostingModel,
        IReadOnlyList<string> IndustryTags,
        int MinimumEmployees,
        string UserScope,
        string Criticality,
        string DataSensitivity,
        IReadOnlyList<string> OwnerHints,
        string BusinessCapability);

    private sealed record DepartmentApplicationPattern(
        string PatternType,
        string DepartmentMatch,
        IReadOnlyList<string> IndustryTags,
        int MinimumEmployees,
        string Name,
        string Category,
        string HostingModel);

    private sealed record OfficeApplicationPattern(
        string PatternType,
        IReadOnlyList<string> IndustryTags,
        int MinimumEmployees,
        string Name,
        string Category,
        string HostingModel);

    private sealed record EnterprisePlatformPattern(
        IReadOnlyList<string> IndustryTags,
        int MinimumEmployees,
        int MinimumOfficeCount,
        string Name,
        string Category,
        string HostingModel);

    private sealed record IndustryApplicationPattern(
        IReadOnlyList<string> IndustryTags,
        int MinimumEmployees,
        string Name,
        string Category,
        string HostingModel);

    private sealed record ApplicationDependencyPattern(
        string MatchSourceNameContains,
        string MatchSourceVendor,
        string MatchSourceCategory,
        string MatchTargetNameContains,
        string MatchTargetVendor,
        string MatchTargetCategory,
        IReadOnlyList<string> IndustryTags,
        int MinimumEmployees,
        string DependencyType,
        string InterfaceType,
        string Criticality);

    private sealed record ApplicationSuiteTemplate(
        string MatchVendor,
        string MatchCategory,
        string MatchNameContains,
        IReadOnlyList<string> IndustryTags,
        int MinimumEmployees,
        string Name,
        string Category,
        string Vendor,
        string BusinessCapability,
        string HostingModel,
        string UserScope,
        string Criticality,
        string DataSensitivity);
}
