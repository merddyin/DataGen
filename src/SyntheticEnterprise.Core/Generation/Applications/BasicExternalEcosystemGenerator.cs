namespace SyntheticEnterprise.Core.Generation.Applications;

using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Core.Abstractions;

public sealed class BasicExternalEcosystemGenerator : IExternalEcosystemGenerator
{
    private readonly IIdFactory _idFactory;
    private readonly IRandomSource _randomSource;

    public BasicExternalEcosystemGenerator(IIdFactory idFactory, IRandomSource randomSource)
    {
        _idFactory = idFactory;
        _randomSource = randomSource;
    }

    public void GenerateEcosystem(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs)
    {
        foreach (var company in world.Companies)
        {
            var departments = world.Departments.Where(department => department.CompanyId == company.Id).ToList();
            var applications = world.Applications.Where(application => application.CompanyId == company.Id).ToList();
            var processes = world.BusinessProcesses.Where(process => process.CompanyId == company.Id).ToList();
            var offices = world.Offices.Where(office => office.CompanyId == company.Id).ToList();
            var people = world.People.Where(person => person.CompanyId == company.Id).ToList();
            var country = offices.FirstOrDefault()?.Country ?? "United States";
            var taxonomy = ResolveIndustryTaxonomy(company.Industry ?? string.Empty, catalogs);
            var applicationCounterpartyPatterns = ReadApplicationCounterpartyPatterns(catalogs, company.Industry ?? string.Empty, taxonomy, people.Count);
            var processCounterpartyPatterns = ReadProcessCounterpartyPatterns(catalogs, company.Industry ?? string.Empty, taxonomy, people.Count);

            var vendors = CreateVendors(company, departments, applications, processes, country, people.Count, catalogs, taxonomy);
            var counterparties = CreateCounterparties(company, departments, country, people.Count, catalogs, taxonomy, vendors);

            world.ExternalOrganizations.AddRange(vendors);
            world.ExternalOrganizations.AddRange(counterparties);

            CreateApplicationCounterpartyLinks(world, company, applications, vendors, counterparties, applicationCounterpartyPatterns);
            CreateProcessCounterpartyLinks(world, company, processes, vendors, counterparties, processCounterpartyPatterns);
        }
    }

    private List<ExternalOrganization> CreateVendors(
        Company company,
        IReadOnlyList<Department> departments,
        IReadOnlyList<ApplicationRecord> applications,
        IReadOnlyList<BusinessProcess> processes,
        string country,
        int peopleCount,
        CatalogSet catalogs,
        IndustryTaxonomyMatch? taxonomy)
    {
        var ownerDepartmentId = ResolveOwnerDepartmentId(departments, "Procurement", "Finance", "Operations", "Information Technology");
        var vendorCatalog = ReadVendorReference(catalogs);
        var domainSuffixes = ReadCatalogValues(catalogs, "domain_suffixes", "Value", new[] { "com", "net", "org", "global" });
        var vendorNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<ExternalOrganization>();
        var industryTokens = BuildIndustryTokens(company.Industry ?? string.Empty, taxonomy);

        foreach (var vendor in applications
                     .Select(application => application.Vendor)
                     .Where(vendor => !string.IsNullOrWhiteSpace(vendor))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!vendorNames.Add(vendor))
            {
                continue;
            }

            var definition = vendorCatalog.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, vendor, StringComparison.OrdinalIgnoreCase));

            results.Add(CreateExternalOrganization(
                company,
                vendor,
                "Vendor",
                definition?.Industry ?? "Technology",
                country,
                ownerDepartmentId,
                definition?.Segment ?? "StrategicSupplier",
                definition?.Criticality ?? "High",
                definition?.RevenueBand ?? "Enterprise",
                primaryDomain: BuildPrimaryDomain(vendor, "Vendor", country, domainSuffixes, definition?.PrimaryDomain, catalogs)));
        }

        var desiredAdditionalVendors = Math.Clamp(Math.Max(peopleCount / 180, processes.Count / 2), 4, 12);
        foreach (var definition in vendorCatalog
                     .Where(candidate => candidate.MinimumEmployees <= Math.Max(1, peopleCount))
                     .Where(candidate => candidate.IndustryTags.Count == 0
                                         || candidate.IndustryTags.Contains("All", StringComparer.OrdinalIgnoreCase)
                                         || candidate.IndustryTags.Any(tag => industryTokens.Contains(tag)))
                     .OrderByDescending(candidate => candidate.Criticality == "High")
                     .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (results.Count >= applications.Select(application => application.Vendor).Distinct(StringComparer.OrdinalIgnoreCase).Count() + desiredAdditionalVendors)
            {
                break;
            }

            if (!vendorNames.Add(definition.Name))
            {
                continue;
            }

            results.Add(CreateExternalOrganization(
                company,
                definition.Name,
                "Vendor",
                definition.Industry,
                country,
                ResolveOwnerDepartmentId(departments, definition.OwnerHints.ToArray()),
                definition.Segment,
                definition.Criticality,
                definition.RevenueBand,
                primaryDomain: BuildPrimaryDomain(definition.Name, "Vendor", country, domainSuffixes, definition.PrimaryDomain, catalogs)));
        }

        return results;
    }

    private List<ExternalOrganization> CreateCounterparties(
        Company company,
        IReadOnlyList<Department> departments,
        string country,
        int peopleCount,
        CatalogSet catalogs,
        IndustryTaxonomyMatch? taxonomy,
        IReadOnlyList<ExternalOrganization> vendors)
    {
        var count = Math.Max(6, Math.Min(24, Math.Max(1, peopleCount) / 120));
        var usedNames = new HashSet<string>(vendors.Select(vendor => vendor.Name), StringComparer.OrdinalIgnoreCase)
        {
            company.Name
        };
        var fakeCompanyProfiles = ReadFakeCompanyProfiles(catalogs)
            .Where(profile => usedNames.Add(profile.Name))
            .ToList();
        var counterpartyPrefixes = ReadCatalogValues(catalogs, "counterparty_name_prefixes", "Value", CustomerPrefixes);
        var counterpartyTerms = ReadCatalogValues(catalogs, "counterparty_name_terms", "Value", CustomerSuffixes);
        var companyTerms = ReadIndustryCompanyTerms(catalogs, company.Industry, taxonomy);
        var companySuffixes = ReadCatalogValues(catalogs, "company_suffixes", "Value", new[] { "LLC", "Group", "Holdings", "Partners" });
        var domainSuffixes = ReadCatalogValues(catalogs, "domain_suffixes", "Value", new[] { "com", "net", "org", "global" });
        var taglines = ReadCatalogValues(catalogs, "taglines", "Value", new[] { "Deliver measurable outcomes", "Operate with trusted precision" });
        var counterpartyPatterns = ReadCounterpartyPatterns(catalogs, company.Industry, taxonomy, peopleCount);

        var results = new List<ExternalOrganization>();
        for (var i = 0; i < count; i++)
        {
            var pattern = i < counterpartyPatterns.Count ? counterpartyPatterns[i] : null;
            var segment = pattern?.Segment ?? (i % 4) switch
            {
                0 => "StrategicAccount",
                1 => "RegionalAccount",
                2 => "ChannelPartner",
                _ => "DistributionPartner"
            };
            var relationship = pattern?.RelationshipType
                               ?? (segment.Contains("Partner", StringComparison.OrdinalIgnoreCase) ? "Partner" : "Customer");
            var profile = fakeCompanyProfiles.Count > 0 ? TakeRandom(fakeCompanyProfiles) : null;
            var name = profile?.Name ?? BuildCatalogCompanyName(i, counterpartyPrefixes, counterpartyTerms, companyTerms, companySuffixes);
            name = EnsureUniqueOrganizationName(name, usedNames, i + 1);
            var organizationIndustry = FirstNonEmpty(
                profile is null ? pattern?.Industry : null,
                InferCounterpartyIndustry(company.Industry, taxonomy, segment, i));
            var primaryDomain = BuildPrimaryDomain(name, relationship, country, domainSuffixes, profile?.PrimaryDomain, catalogs);
            var contactEmail = FirstNonEmpty(profile?.ContactEmail, BuildContactEmail(relationship, primaryDomain));
            var tagline = FirstNonEmpty(profile?.Tagline, SelectFallbackTagline(taglines, i));
            var description = FirstNonEmpty(profile?.Description, BuildOrganizationDescription(relationship, organizationIndustry, segment));
            var taxIdentifier = FirstNonEmpty(profile?.TaxIdentifier, BuildSyntheticTaxIdentifier(country, i));
            var ownerDepartmentId = ResolveOwnerDepartmentId(
                departments,
                pattern?.OwnerHints?.ToArray() ?? new[] { "Sales", "Marketing", "Operations" });
            var criticality = pattern?.Criticality ?? (segment == "StrategicAccount" ? "High" : "Medium");
            var revenueBand = pattern?.RevenueBand ?? (segment == "StrategicAccount" ? "Enterprise" : "MidMarket");

            results.Add(CreateExternalOrganization(
                company,
                name,
                relationship,
                organizationIndustry,
                country,
                ownerDepartmentId,
                segment,
                criticality,
                revenueBand,
                legalName: name,
                description: description,
                tagline: tagline,
                primaryDomain: primaryDomain,
                contactEmail: contactEmail,
                taxIdentifier: taxIdentifier));
        }

        return results;
    }

    private void CreateApplicationCounterpartyLinks(
        SyntheticEnterpriseWorld world,
        Company company,
        IReadOnlyList<ApplicationRecord> applications,
        IReadOnlyList<ExternalOrganization> vendors,
        IReadOnlyList<ExternalOrganization> counterparties,
        IReadOnlyList<ApplicationCounterpartyPattern> patterns)
    {
        var existingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var application in applications)
        {
            var addedCustomerPartnerLink = false;
            var addedSupplierLink = false;

            if (!string.IsNullOrWhiteSpace(application.Vendor))
            {
                var vendor = vendors.FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, application.Vendor, StringComparison.OrdinalIgnoreCase));
                if (vendor is not null)
                {
                    AddApplicationCounterpartyLink(
                        world,
                        company.Id,
                        application,
                        vendor,
                        "VendorProvided",
                        application.HostingModel == "SaaS" ? "SaaSControlPlane" : "SoftwareSupplier",
                        application.Criticality,
                        existingKeys);
                }
            }

            foreach (var pattern in patterns.Where(pattern => MatchesApplicationCounterpartyPattern(pattern, application)))
            {
                var candidates = SelectApplicationCounterpartyTargets(pattern, vendors, counterparties)
                    .Take(pattern.MaximumLinks)
                    .ToList();

                foreach (var target in candidates)
                {
                    AddApplicationCounterpartyLink(
                        world,
                        company.Id,
                        application,
                        target,
                        pattern.LinkRelationshipType,
                        pattern.IntegrationType,
                        FirstNonEmpty(pattern.Criticality, target.Criticality, application.Criticality),
                        existingKeys);

                    if (target.RelationshipType is "Customer" or "Partner")
                    {
                        addedCustomerPartnerLink = true;
                    }

                    if (target.RelationshipType == "Vendor")
                    {
                        addedSupplierLink = true;
                    }
                }
            }

            if (!addedCustomerPartnerLink
                && (application.Name.Contains("CRM", StringComparison.OrdinalIgnoreCase)
                    || application.Name.Contains("Sales", StringComparison.OrdinalIgnoreCase)
                    || application.Name.Contains("Customer", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(application.BusinessCapability, "Customer Relationship Management", StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var counterparty in counterparties.Where(candidate => candidate.RelationshipType != "Vendor").Take(Math.Min(4, counterparties.Count)))
                {
                    AddApplicationCounterpartyLink(
                        world,
                        company.Id,
                        application,
                        counterparty,
                        counterparty.RelationshipType == "Partner" ? "PartnerIntegration" : "CustomerIntegration",
                        counterparty.RelationshipType == "Partner" ? "PartnerPortalOrEDI" : "PortalOrEDI",
                        counterparty.Criticality,
                        existingKeys);
                }
            }

            if (!addedSupplierLink
                && (application.Name.Contains("Procure", StringComparison.OrdinalIgnoreCase)
                    || application.Name.Contains("Supplier", StringComparison.OrdinalIgnoreCase)
                    || application.Name.Contains("ERP", StringComparison.OrdinalIgnoreCase)
                    || application.Name.Contains("Warehouse", StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var vendor in vendors.Take(Math.Min(6, vendors.Count)))
                {
                    AddApplicationCounterpartyLink(
                        world,
                        company.Id,
                        application,
                        vendor,
                        "SupplierIntegration",
                        vendor.Segment.Contains("ManagedService", StringComparison.OrdinalIgnoreCase) ? "ManagedService" : "B2BOrManagedService",
                        vendor.Criticality,
                        existingKeys);
                }
            }
        }
    }

    private void AddApplicationCounterpartyLink(
        SyntheticEnterpriseWorld world,
        string companyId,
        ApplicationRecord application,
        ExternalOrganization organization,
        string relationshipType,
        string integrationType,
        string criticality,
        ISet<string> existingKeys)
    {
        var key = $"{application.Id}|{organization.Id}|{relationshipType}|{integrationType}";
        if (!existingKeys.Add(key))
        {
            return;
        }

        world.ApplicationCounterpartyLinks.Add(new ApplicationCounterpartyLink
        {
            Id = _idFactory.Next("APPEXT"),
            CompanyId = companyId,
            ApplicationId = application.Id,
            ExternalOrganizationId = organization.Id,
            RelationshipType = relationshipType,
            IntegrationType = integrationType,
            Criticality = FirstNonEmpty(criticality, organization.Criticality, application.Criticality)
        });
    }

    private void CreateProcessCounterpartyLinks(
        SyntheticEnterpriseWorld world,
        Company company,
        IReadOnlyList<BusinessProcess> processes,
        IReadOnlyList<ExternalOrganization> vendors,
        IReadOnlyList<ExternalOrganization> counterparties,
        IReadOnlyList<ProcessCounterpartyPattern> patterns)
    {
        var existingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var process in processes)
        {
            var addedCustomerLinks = false;
            var addedSupplierLinks = false;
            var addedPartnerLinks = false;

            foreach (var pattern in patterns.Where(pattern => MatchesProcessCounterpartyPattern(pattern, process)))
            {
                var targets = SelectProcessCounterpartyTargets(pattern, vendors, counterparties)
                    .Take(pattern.MaximumLinks)
                    .ToList();

                foreach (var target in targets)
                {
                    AddBusinessProcessCounterpartyLink(
                        world,
                        company.Id,
                        process,
                        target,
                        pattern.LinkRelationshipType,
                        pattern.IsPrimary,
                        existingKeys);

                    switch (target.RelationshipType)
                    {
                        case "Customer":
                            addedCustomerLinks = true;
                            break;
                        case "Vendor":
                            addedSupplierLinks = true;
                            break;
                        case "Partner":
                            addedPartnerLinks = true;
                            break;
                    }
                }
            }

            if (!addedCustomerLinks
                && (process.Name.Contains("Order to Cash", StringComparison.OrdinalIgnoreCase)
                    || process.Domain is "Revenue" or "Customer Service"))
            {
                foreach (var counterparty in counterparties.Where(candidate => candidate.RelationshipType == "Customer").Take(Math.Min(8, counterparties.Count)))
                {
                    AddBusinessProcessCounterpartyLink(
                        world,
                        company.Id,
                        process,
                        counterparty,
                        "Customer",
                        counterparty.Segment == "StrategicAccount",
                        existingKeys);
                }
            }

            if (!addedSupplierLinks
                && (process.Name.Contains("Procure to Pay", StringComparison.OrdinalIgnoreCase)
                    || process.Name.Contains("Plan to Produce", StringComparison.OrdinalIgnoreCase)
                    || process.Domain is "Supply Chain" or "Manufacturing"))
            {
                foreach (var vendor in vendors.Take(Math.Min(8, vendors.Count)))
                {
                    AddBusinessProcessCounterpartyLink(
                        world,
                        company.Id,
                        process,
                        vendor,
                        "Supplier",
                        vendor.Segment is "StrategicSupplier" or "ManagedServiceProvider",
                        existingKeys);
                }
            }

            if (!addedPartnerLinks && process.Domain is "Engineering" or "Operations")
            {
                foreach (var partner in counterparties.Where(candidate => candidate.RelationshipType == "Partner").Take(3))
                {
                    AddBusinessProcessCounterpartyLink(
                        world,
                        company.Id,
                        process,
                        partner,
                        "Partner",
                        partner.Segment == "DistributionPartner",
                        existingKeys);
                }
            }
        }
    }

    private void AddBusinessProcessCounterpartyLink(
        SyntheticEnterpriseWorld world,
        string companyId,
        BusinessProcess process,
        ExternalOrganization organization,
        string relationshipType,
        bool isPrimary,
        ISet<string> existingKeys)
    {
        var key = $"{process.Id}|{organization.Id}|{relationshipType}";
        if (!existingKeys.Add(key))
        {
            return;
        }

        world.BusinessProcessCounterpartyLinks.Add(new BusinessProcessCounterpartyLink
        {
            Id = _idFactory.Next("PROCEXT"),
            CompanyId = companyId,
            BusinessProcessId = process.Id,
            ExternalOrganizationId = organization.Id,
            RelationshipType = relationshipType,
            IsPrimary = isPrimary
        });
    }

    private ExternalOrganization CreateExternalOrganization(
        Company company,
        string name,
        string relationshipType,
        string industry,
        string country,
        string ownerDepartmentId,
        string segment,
        string criticality,
        string revenueBand,
        string? legalName = null,
        string? description = null,
        string? tagline = null,
        string? primaryDomain = null,
        string? contactEmail = null,
        string? taxIdentifier = null)
    {
        var resolvedPrimaryDomain = FirstNonEmpty(primaryDomain, $"{Slug(name)}.example.test");
        return new ExternalOrganization
        {
            Id = _idFactory.Next("EXT"),
            CompanyId = company.Id,
            Name = name,
            LegalName = FirstNonEmpty(legalName, name),
            Description = FirstNonEmpty(description, BuildOrganizationDescription(relationshipType, industry, segment)),
            Tagline = FirstNonEmpty(tagline, BuildDefaultTagline(name, relationshipType)),
            RelationshipType = relationshipType,
            Industry = industry,
            Country = country,
            PrimaryDomain = resolvedPrimaryDomain,
            Website = $"https://www.{resolvedPrimaryDomain}",
            ContactEmail = FirstNonEmpty(contactEmail, BuildContactEmail(relationshipType, resolvedPrimaryDomain)),
            TaxIdentifier = FirstNonEmpty(taxIdentifier),
            Segment = segment,
            RevenueBand = revenueBand,
            OwnerDepartmentId = ownerDepartmentId,
            Criticality = criticality
        };
    }

    private string ResolveOwnerDepartmentId(IReadOnlyList<Department> departments, params string[] ownerHints)
    {
        foreach (var hint in ownerHints)
        {
            var match = departments.FirstOrDefault(department =>
                department.Name.Contains(hint, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match.Id;
            }
        }

        return departments.FirstOrDefault()?.Id ?? string.Empty;
    }

    private static string EnsureUniqueOrganizationName(string baseName, ISet<string> usedNames, int ordinal)
    {
        if (usedNames.Add(baseName))
        {
            return baseName;
        }

        for (var suffix = ordinal; suffix < ordinal + 1000; suffix++)
        {
            var candidate = $"{baseName} {suffix}";
            if (usedNames.Add(candidate))
            {
                return candidate;
            }
        }

        return $"{baseName} {Guid.NewGuid():N}";
    }

    private static List<VendorDefinition> ReadVendorReference(CatalogSet catalogs)
    {
        var definitions = new List<VendorDefinition>();

        if (!catalogs.CsvCatalogs.TryGetValue("vendor_reference", out var rows))
        {
            definitions = VendorFallbacks
                .Select(item => new VendorDefinition(
                    item.Name,
                    item.Industry,
                    item.Segment,
                    item.Criticality,
                    new[] { "All" },
                    Array.Empty<string>(),
                    0,
                    item.Segment == "StrategicSupplier" ? "Enterprise" : "MidMarket",
                    string.Empty))
                .ToList();
        }
        else
        {
            definitions = rows
                .Where(row => !string.IsNullOrWhiteSpace(Read(row, "Name")))
                .Select(row => new VendorDefinition(
                    Read(row, "Name"),
                    Read(row, "Industry"),
                    Read(row, "Segment"),
                    Read(row, "Criticality"),
                    SplitPipe(Read(row, "IndustryTags")),
                    SplitPipe(Read(row, "OwnerHints")),
                    int.TryParse(Read(row, "MinimumEmployees"), out var minimumEmployees) ? minimumEmployees : 0,
                    InferRevenueBand(Read(row, "Segment")),
                    Read(row, "PrimaryDomain")))
                .ToList();
        }

        var definitionsByName = definitions
            .GroupBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        if (catalogs.CsvCatalogs.TryGetValue("vendor_aliases", out var aliasRows))
        {
            foreach (var row in aliasRows)
            {
                var alias = Read(row, "Alias");
                var canonicalName = Read(row, "CanonicalName");
                if (string.IsNullOrWhiteSpace(alias)
                    || string.IsNullOrWhiteSpace(canonicalName)
                    || definitionsByName.ContainsKey(alias)
                    || !definitionsByName.TryGetValue(canonicalName, out var canonicalDefinition))
                {
                    continue;
                }

                definitionsByName[alias] = canonicalDefinition with { Name = alias };
            }
        }

        return definitionsByName.Values
            .OrderBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<CounterpartyPattern> ReadCounterpartyPatterns(
        CatalogSet catalogs,
        string companyIndustry,
        IndustryTaxonomyMatch? taxonomy,
        int peopleCount)
    {
        if (!catalogs.CsvCatalogs.TryGetValue("industry_counterparty_patterns", out var rows))
        {
            return new List<CounterpartyPattern>();
        }

        var industryTokens = BuildIndustryTokens(companyIndustry, taxonomy);
        return rows
            .Select((row, index) => new
            {
                Index = index,
                Pattern = new CounterpartyPattern(
                    Read(row, "RelationshipType"),
                    Read(row, "Segment"),
                    Read(row, "Industry"),
                    Read(row, "Criticality"),
                    Read(row, "RevenueBand"),
                    SplitPipe(Read(row, "OwnerHints")),
                    SplitPipe(Read(row, "IndustryTags")),
                    int.TryParse(Read(row, "MinimumEmployees"), out var minimumEmployees) ? minimumEmployees : 0)
            })
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Pattern.RelationshipType))
            .Where(entry => entry.Pattern.MinimumEmployees <= Math.Max(1, peopleCount))
            .Where(entry => entry.Pattern.IndustryTags.Count == 0
                            || entry.Pattern.IndustryTags.Contains("All", StringComparer.OrdinalIgnoreCase)
                            || entry.Pattern.IndustryTags.Any(tag => industryTokens.Contains(tag)))
            .OrderByDescending(entry => entry.Pattern.Criticality == "High")
            .ThenBy(entry => entry.Pattern.IndustryTags.Contains("All", StringComparer.OrdinalIgnoreCase))
            .ThenBy(entry => entry.Index)
            .Select(entry => entry.Pattern)
            .ToList();
    }

    private static List<ApplicationCounterpartyPattern> ReadApplicationCounterpartyPatterns(
        CatalogSet catalogs,
        string companyIndustry,
        IndustryTaxonomyMatch? taxonomy,
        int peopleCount)
    {
        if (!catalogs.CsvCatalogs.TryGetValue("application_counterparty_patterns", out var rows))
        {
            return new List<ApplicationCounterpartyPattern>();
        }

        var industryTokens = BuildIndustryTokens(companyIndustry, taxonomy);
        var entries = new List<(int Index, ApplicationCounterpartyPattern Pattern)>();

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var maximumLinks = int.TryParse(Read(row, "MaximumLinks"), out var parsedMaximumLinks)
                ? Math.Max(1, parsedMaximumLinks)
                : 3;
            var minimumEmployees = int.TryParse(Read(row, "MinimumEmployees"), out var parsedMinimumEmployees)
                ? parsedMinimumEmployees
                : 0;

            var pattern = new ApplicationCounterpartyPattern(
                SplitPipe(Read(row, "IndustryTags")),
                Read(row, "MatchCategory"),
                Read(row, "MatchCapability"),
                Read(row, "MatchNameContains"),
                Read(row, "TargetRelationshipType"),
                Read(row, "TargetSegment"),
                Read(row, "LinkRelationshipType"),
                Read(row, "IntegrationType"),
                Read(row, "Criticality"),
                maximumLinks,
                minimumEmployees);

            entries.Add((index, pattern));
        }

        return entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Pattern.TargetRelationshipType))
            .Where(entry => entry.Pattern.MinimumEmployees <= Math.Max(1, peopleCount))
            .Where(entry => entry.Pattern.IndustryTags.Count == 0
                            || entry.Pattern.IndustryTags.Contains("All", StringComparer.OrdinalIgnoreCase)
                            || entry.Pattern.IndustryTags.Any(tag => industryTokens.Contains(tag)))
            .OrderByDescending(entry => GetApplicationCounterpartyPatternSpecificity(entry.Pattern))
            .ThenBy(entry => entry.Index)
            .Select(entry => entry.Pattern)
            .ToList();
    }

    private static List<ProcessCounterpartyPattern> ReadProcessCounterpartyPatterns(
        CatalogSet catalogs,
        string companyIndustry,
        IndustryTaxonomyMatch? taxonomy,
        int peopleCount)
    {
        if (!catalogs.CsvCatalogs.TryGetValue("business_process_counterparty_patterns", out var rows))
        {
            return new List<ProcessCounterpartyPattern>();
        }

        var industryTokens = BuildIndustryTokens(companyIndustry, taxonomy);
        var entries = new List<(int Index, ProcessCounterpartyPattern Pattern)>();

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var maximumLinks = int.TryParse(Read(row, "MaximumLinks"), out var parsedMaximumLinks)
                ? Math.Max(1, parsedMaximumLinks)
                : 4;
            var minimumEmployees = int.TryParse(Read(row, "MinimumEmployees"), out var parsedMinimumEmployees)
                ? parsedMinimumEmployees
                : 0;
            var isPrimary = bool.TryParse(Read(row, "IsPrimary"), out var parsedIsPrimary) && parsedIsPrimary;

            var pattern = new ProcessCounterpartyPattern(
                SplitPipe(Read(row, "IndustryTags")),
                Read(row, "MatchProcessName"),
                Read(row, "MatchDomain"),
                Read(row, "TargetRelationshipType"),
                Read(row, "TargetSegment"),
                Read(row, "LinkRelationshipType"),
                isPrimary,
                maximumLinks,
                minimumEmployees);

            entries.Add((index, pattern));
        }

        return entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Pattern.TargetRelationshipType))
            .Where(entry => entry.Pattern.MinimumEmployees <= Math.Max(1, peopleCount))
            .Where(entry => entry.Pattern.IndustryTags.Count == 0
                            || entry.Pattern.IndustryTags.Contains("All", StringComparer.OrdinalIgnoreCase)
                            || entry.Pattern.IndustryTags.Any(tag => industryTokens.Contains(tag)))
            .OrderByDescending(entry => GetProcessCounterpartyPatternSpecificity(entry.Pattern))
            .ThenBy(entry => entry.Index)
            .Select(entry => entry.Pattern)
            .ToList();
    }

    private static bool MatchesApplicationCounterpartyPattern(ApplicationCounterpartyPattern pattern, ApplicationRecord application)
    {
        if (!string.IsNullOrWhiteSpace(pattern.MatchCategory)
            && !application.Category.Contains(pattern.MatchCategory, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchCapability)
            && !application.BusinessCapability.Contains(pattern.MatchCapability, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchNameContains)
            && !application.Name.Contains(pattern.MatchNameContains, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static bool MatchesProcessCounterpartyPattern(ProcessCounterpartyPattern pattern, BusinessProcess process)
    {
        if (!string.IsNullOrWhiteSpace(pattern.MatchProcessName)
            && !process.Name.Contains(pattern.MatchProcessName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchDomain)
            && !process.Domain.Contains(pattern.MatchDomain, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static int GetApplicationCounterpartyPatternSpecificity(ApplicationCounterpartyPattern pattern)
    {
        var score = pattern.MinimumEmployees > 1 ? 1 : 0;

        if (!string.IsNullOrWhiteSpace(pattern.MatchCategory))
        {
            score += 3;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchCapability))
        {
            score += 5;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchNameContains))
        {
            score += 7;
        }

        if (!string.IsNullOrWhiteSpace(pattern.TargetSegment))
        {
            score += 2;
        }

        if (pattern.IndustryTags.Count > 0 && !pattern.IndustryTags.Contains("All", StringComparer.OrdinalIgnoreCase))
        {
            score += 2;
        }

        return score;
    }

    private static int GetProcessCounterpartyPatternSpecificity(ProcessCounterpartyPattern pattern)
    {
        var score = pattern.MinimumEmployees > 1 ? 1 : 0;

        if (!string.IsNullOrWhiteSpace(pattern.MatchProcessName))
        {
            score += 6;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchDomain))
        {
            score += 4;
        }

        if (!string.IsNullOrWhiteSpace(pattern.TargetSegment))
        {
            score += 2;
        }

        if (pattern.IndustryTags.Count > 0 && !pattern.IndustryTags.Contains("All", StringComparer.OrdinalIgnoreCase))
        {
            score += 2;
        }

        return score;
    }

    private static IEnumerable<ExternalOrganization> SelectApplicationCounterpartyTargets(
        ApplicationCounterpartyPattern pattern,
        IReadOnlyList<ExternalOrganization> vendors,
        IReadOnlyList<ExternalOrganization> counterparties)
    {
        IEnumerable<ExternalOrganization> candidates = string.Equals(pattern.TargetRelationshipType, "Vendor", StringComparison.OrdinalIgnoreCase)
            ? vendors
            : counterparties.Where(candidate => string.Equals(candidate.RelationshipType, pattern.TargetRelationshipType, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(pattern.TargetSegment))
        {
            candidates = candidates.Where(candidate =>
                string.Equals(candidate.Segment, pattern.TargetSegment, StringComparison.OrdinalIgnoreCase));
        }

        return candidates;
    }

    private static IEnumerable<ExternalOrganization> SelectProcessCounterpartyTargets(
        ProcessCounterpartyPattern pattern,
        IReadOnlyList<ExternalOrganization> vendors,
        IReadOnlyList<ExternalOrganization> counterparties)
    {
        IEnumerable<ExternalOrganization> candidates = string.Equals(pattern.TargetRelationshipType, "Vendor", StringComparison.OrdinalIgnoreCase)
            ? vendors
            : counterparties.Where(candidate => string.Equals(candidate.RelationshipType, pattern.TargetRelationshipType, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(pattern.TargetSegment))
        {
            candidates = candidates.Where(candidate =>
                string.Equals(candidate.Segment, pattern.TargetSegment, StringComparison.OrdinalIgnoreCase));
        }

        return candidates;
    }

    private static List<FakeCompanyProfile> ReadFakeCompanyProfiles(CatalogSet catalogs)
    {
        if (!catalogs.CsvCatalogs.TryGetValue("fake_companies_reference", out var rows))
        {
            return new List<FakeCompanyProfile>();
        }

        return rows
            .Where(row => !string.IsNullOrWhiteSpace(Read(row, "CompanyName")))
            .Select(row => new FakeCompanyProfile(
                Read(row, "CompanyName"),
                Read(row, "Description"),
                Read(row, "Tagline"),
                Read(row, "CompanyEmail"),
                FirstNonEmpty(Read(row, "TaxIdentifier"), Read(row, "ein")),
                DeriveDomainFromEmail(Read(row, "CompanyEmail"))))
            .GroupBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private T TakeRandom<T>(List<T> values)
    {
        var index = _randomSource.Next(values.Count);
        var value = values[index];
        values.RemoveAt(index);
        return value;
    }

    private string BuildCatalogCompanyName(
        int index,
        IReadOnlyList<string> counterpartyPrefixes,
        IReadOnlyList<string> counterpartyTerms,
        IReadOnlyList<string> companyTerms,
        IReadOnlyList<string> companySuffixes)
    {
        var prefixes = counterpartyPrefixes.Count == 0 ? CustomerPrefixes : counterpartyPrefixes;
        var terms = counterpartyTerms.Count == 0 ? CustomerSuffixes : counterpartyTerms;
        var prefix = prefixes[index % prefixes.Count];
        var term = companyTerms.Count == 0
            ? terms[(index + _randomSource.Next(0, terms.Count)) % terms.Count]
            : companyTerms[_randomSource.Next(companyTerms.Count)];
        var suffix = companySuffixes.Count == 0
            ? "LLC"
            : companySuffixes[_randomSource.Next(companySuffixes.Count)];

        return $"{prefix} {term} {suffix}";
    }

    private static List<string> ReadIndustryCompanyTerms(CatalogSet catalogs, string companyIndustry, IndustryTaxonomyMatch? taxonomy)
    {
        if (!catalogs.CsvCatalogs.TryGetValue("company_name_elements", out var rows))
        {
            return new List<string>();
        }

        var targets = new[]
        {
            taxonomy?.Sector,
            taxonomy?.IndustryGroup,
            taxonomy?.Industry,
            companyIndustry
        }.Where(value => !string.IsNullOrWhiteSpace(value)).ToList();

        var matches = rows
            .Where(row =>
            {
                var sector = Read(row, "Sector");
                return targets.Count == 0 || targets.Any(target => MatchesIndustryField(target!, sector));
            })
            .Select(row => Read(row, "Term"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return matches;
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

    private string BuildPrimaryDomain(string name, string relationshipType, string country, IReadOnlyList<string> domainSuffixes, string? preferredDomain, CatalogSet catalogs)
    {
        if (!string.IsNullOrWhiteSpace(preferredDomain))
        {
            return preferredDomain!;
        }

        var slug = Slug(name);
        var suffix = ResolveCountryPreferredDomainSuffix(catalogs, country);
        if (string.IsNullOrWhiteSpace(suffix))
        {
            suffix = SelectDomainSuffix(domainSuffixes, relationshipType, country);
        }

        return $"{slug}.{suffix}";
    }

    private static string ResolveCountryPreferredDomainSuffix(CatalogSet catalogs, string country)
    {
        if (!catalogs.CsvCatalogs.TryGetValue("country_identity_rules", out var rows))
        {
            return string.Empty;
        }

        var match = rows.FirstOrDefault(row =>
            string.Equals(Read(row, "Country"), country, StringComparison.OrdinalIgnoreCase));
        return match is null ? string.Empty : Read(match, "PrimaryDomainSuffix").Trim().TrimStart('.');
    }

    private string SelectDomainSuffix(IReadOnlyList<string> domainSuffixes, string relationshipType, string country)
    {
        if (domainSuffixes.Count == 0)
        {
            return "com";
        }

        var weighted = new List<string>();
        foreach (var suffix in domainSuffixes)
        {
            var weight = suffix switch
            {
                "com" => 8,
                "net" => relationshipType == "Vendor" ? 4 : 2,
                "org" => relationshipType.Contains("Partner", StringComparison.OrdinalIgnoreCase) ? 4 : 2,
                "global" => country != "United States" ? 3 : 1,
                "info" or "site" or "work" => 1,
                _ => 1
            };

            for (var i = 0; i < weight; i++)
            {
                weighted.Add(suffix);
            }
        }

        return weighted[_randomSource.Next(weighted.Count)];
    }

    private static string DeriveDomainFromEmail(string email)
    {
        var atIndex = email.IndexOf('@');
        return atIndex >= 0 && atIndex < email.Length - 1
            ? email[(atIndex + 1)..].Trim().ToLowerInvariant()
            : string.Empty;
    }

    private static string BuildContactEmail(string relationshipType, string primaryDomain)
    {
        if (string.IsNullOrWhiteSpace(primaryDomain))
        {
            return string.Empty;
        }

        var localPart = relationshipType.Contains("Partner", StringComparison.OrdinalIgnoreCase)
            ? "alliances"
            : relationshipType.Equals("Vendor", StringComparison.OrdinalIgnoreCase)
                ? "vendorops"
                : "sales";
        return $"{localPart}@{primaryDomain}";
    }

    private static string SelectFallbackTagline(IReadOnlyList<string> taglines, int index)
        => taglines.Count == 0 ? string.Empty : taglines[index % taglines.Count];

    private static string BuildOrganizationDescription(string relationshipType, string industry, string segment)
    {
        var relationshipLabel = relationshipType switch
        {
            "Vendor" => "supplier",
            "Partner" => "partner",
            _ => "customer"
        };

        return $"{segment} {relationshipLabel} operating in {industry}.";
    }

    private static string BuildDefaultTagline(string name, string relationshipType)
        => relationshipType switch
        {
            "Vendor" => $"Supporting {name} delivery and operations",
            "Partner" => $"Extending collaboration with {name}",
            _ => $"Serving customers through {name}"
        };

    private string BuildSyntheticTaxIdentifier(string country, int ordinal)
    {
        var normalizedCountry = country.Trim();
        return normalizedCountry switch
        {
            "United States" or "US" or "USA" => $"{_randomSource.Next(10, 99):00}-{_randomSource.Next(0, 9_999_999):0000000}",
            "Canada" or "CA" => $"{_randomSource.Next(100_000_000, 999_999_999)}RT{(ordinal % 9000) + 1000}",
            "United Kingdom" or "UK" or "GB" => $"{(char)('A' + (_randomSource.Next(0, 26)))}{(char)('A' + (_randomSource.Next(0, 26)))}{_randomSource.Next(100_000_00, 999_999_99)}",
            _ => $"{Slug(country).ToUpperInvariant()}-{_randomSource.Next(100_000, 999_999)}-{ordinal + 1:000}"
        };
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static string InferRevenueBand(string segment)
        => segment switch
        {
            "StrategicSupplier" or "StrategicAccount" => "Enterprise",
            "ManagedServiceProvider" or "TechnologySupplier" => "Enterprise",
            _ => "MidMarket"
        };

    private static string InferCounterpartyIndustry(string companyIndustry, IndustryTaxonomyMatch? taxonomy, string segment, int index)
    {
        var normalized = string.Join(" ", new[] { companyIndustry, taxonomy?.Sector, taxonomy?.IndustryGroup, taxonomy?.Industry, taxonomy?.SubIndustry });

        if (normalized.Contains("manufact", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("industr", StringComparison.OrdinalIgnoreCase))
        {
            return (index % 3) switch
            {
                0 => "Industrial Distribution",
                1 => "Automotive Components",
                _ => segment.Contains("Partner", StringComparison.OrdinalIgnoreCase) ? "Logistics" : "Industrial Equipment"
            };
        }

        if (normalized.Contains("health", StringComparison.OrdinalIgnoreCase))
        {
            return index % 2 == 0 ? "Healthcare Services" : "Medical Devices";
        }

        if (normalized.Contains("finance", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("bank", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("insurance", StringComparison.OrdinalIgnoreCase))
        {
            return index % 2 == 0 ? "Financial Services" : "Insurance";
        }

        if (normalized.Contains("retail", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("consumer", StringComparison.OrdinalIgnoreCase))
        {
            return index % 2 == 0 ? "Retail" : "Distribution";
        }

        if (normalized.Contains("technology", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("software", StringComparison.OrdinalIgnoreCase))
        {
            return index % 2 == 0 ? "Technology Services" : "Software";
        }

        return string.IsNullOrWhiteSpace(companyIndustry) ? "Business Services" : companyIndustry;
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

    private static string Slug(string value)
        => new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private sealed record VendorDefinition(
        string Name,
        string Industry,
        string Segment,
        string Criticality,
        IReadOnlyList<string> IndustryTags,
        IReadOnlyList<string> OwnerHints,
        int MinimumEmployees,
        string RevenueBand,
        string PrimaryDomain);

    private sealed record FakeCompanyProfile(
        string Name,
        string Description,
        string Tagline,
        string ContactEmail,
        string TaxIdentifier,
        string PrimaryDomain);

    private sealed record CounterpartyPattern(
        string RelationshipType,
        string Segment,
        string Industry,
        string Criticality,
        string RevenueBand,
        IReadOnlyList<string> OwnerHints,
        IReadOnlyList<string> IndustryTags,
        int MinimumEmployees);

    private sealed record ApplicationCounterpartyPattern(
        IReadOnlyList<string> IndustryTags,
        string MatchCategory,
        string MatchCapability,
        string MatchNameContains,
        string TargetRelationshipType,
        string TargetSegment,
        string LinkRelationshipType,
        string IntegrationType,
        string Criticality,
        int MaximumLinks,
        int MinimumEmployees);

    private sealed record ProcessCounterpartyPattern(
        IReadOnlyList<string> IndustryTags,
        string MatchProcessName,
        string MatchDomain,
        string TargetRelationshipType,
        string TargetSegment,
        string LinkRelationshipType,
        bool IsPrimary,
        int MaximumLinks,
        int MinimumEmployees);

    private sealed record IndustryTaxonomyMatch(string Sector, string IndustryGroup, string Industry, string SubIndustry);

    private static readonly (string Name, string Industry, string Segment, string Criticality)[] VendorFallbacks =
    [
        ("Grainger", "Industrial Supply", "OperationalSupplier", "Medium"),
        ("CDW", "Technology Reseller", "TechnologySupplier", "Medium"),
        ("SHI International", "Technology Reseller", "TechnologySupplier", "Medium"),
        ("Deloitte", "Professional Services", "ManagedServiceProvider", "Medium"),
        ("FedEx", "Logistics", "LogisticsProvider", "Medium"),
        ("UPS", "Logistics", "LogisticsProvider", "Medium"),
        ("ADP", "Payroll Services", "SharedServiceProvider", "High")
    ];

    private static readonly string[] CustomerPrefixes =
    [
        "Northwind",
        "Blue Ridge",
        "Crescent",
        "Summit",
        "Harbor",
        "Prairie",
        "Granite",
        "Redwood"
    ];

    private static readonly string[] CustomerSuffixes =
    [
        "Distribution",
        "Retail Group",
        "Industrial Supply",
        "Logistics",
        "Wholesale",
        "Components",
        "Manufacturing",
        "Partners"
    ];
}
