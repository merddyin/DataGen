using Microsoft.Extensions.DependencyInjection;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Core.Abstractions;
using SyntheticEnterprise.Core.Catalogs;
using SyntheticEnterprise.Core.DependencyInjection;

namespace SyntheticEnterprise.Core.Tests;

public sealed class EcosystemGenerationTests
{
    [Fact]
    public void WorldGenerator_Populates_External_Organizations_And_Process_Links()
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
                    Name = "Ecosystem Test",
                    IndustryProfile = "Manufacturing",
                    Applications = new ApplicationProfile
                    {
                        IncludeApplications = true,
                        BaseApplicationCount = 6,
                        IncludeLineOfBusinessApplications = true,
                        IncludeSaaSApplications = true
                    },
                    Companies =
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Ecosystem Co",
                            Industry = "Manufacturing",
                            EmployeeCount = 800,
                            BusinessUnitCount = 3,
                            DepartmentCountPerBusinessUnit = 4,
                            TeamCountPerDepartment = 2,
                            OfficeCount = 3,
                            Countries = { "United States" }
                        }
                    }
                }
            },
            new FileSystemCatalogLoader().LoadFromPath(TestEnvironmentPaths.GetCatalogRoot()));

        Assert.NotEmpty(result.World.ExternalOrganizations);
        Assert.NotEmpty(result.World.ApplicationCounterpartyLinks);
        Assert.NotEmpty(result.World.BusinessProcessCounterpartyLinks);
        Assert.Contains(result.World.ExternalOrganizations, organization => organization.RelationshipType == "Vendor");
        Assert.Contains(result.World.ExternalOrganizations, organization => organization.RelationshipType is "Customer" or "Partner");
        Assert.Contains(result.World.ExternalOrganizations, organization => organization.RelationshipType == "Vendor" && organization.Segment != "StrategicSupplier");
        Assert.Contains(result.World.ExternalOrganizations, organization => organization.RelationshipType == "Vendor" && organization.Industry != "Technology");
        Assert.Contains(result.World.ExternalOrganizations, organization => organization.RelationshipType == "Partner");
        Assert.Contains(result.World.ExternalOrganizations, organization =>
            (organization.RelationshipType is "Vendor" or "Customer" or "Partner")
            && !string.IsNullOrWhiteSpace(organization.PrimaryDomain)
            && !organization.PrimaryDomain.EndsWith(".example.test", StringComparison.OrdinalIgnoreCase)
            && organization.Website.Contains(organization.PrimaryDomain, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.World.ExternalOrganizations, organization =>
            (organization.RelationshipType is "Vendor" or "Customer" or "Partner")
            && !string.IsNullOrWhiteSpace(organization.LegalName));
        Assert.Contains(result.World.ExternalOrganizations, organization =>
            organization.RelationshipType is "Customer" or "Partner"
            && !string.IsNullOrWhiteSpace(organization.Description)
            && !string.IsNullOrWhiteSpace(organization.Tagline)
            && !string.IsNullOrWhiteSpace(organization.ContactEmail)
            && organization.ContactEmail.Contains('@')
            && !string.IsNullOrWhiteSpace(organization.TaxIdentifier));
        Assert.Contains(result.World.ExternalOrganizations, organization =>
            organization.RelationshipType == "Vendor"
            && organization.Name == "Microsoft"
            && organization.PrimaryDomain == "microsoft.com");
        Assert.Contains(result.World.ExternalOrganizations, organization =>
            organization.RelationshipType == "Vendor"
            && !string.IsNullOrWhiteSpace(organization.RelationshipBasis)
            && !string.IsNullOrWhiteSpace(organization.RelationshipScope)
            && !string.IsNullOrWhiteSpace(organization.RelationshipDefinition));
        Assert.Contains(result.World.ExternalOrganizations, organization =>
            organization.RelationshipType == "Vendor"
            && organization.PrimaryDomain == "servicenow.com"
            && organization.Industry.Contains("Workflow", StringComparison.OrdinalIgnoreCase)
            && organization.Segment == "StrategicSupplier");
        Assert.Contains(result.World.ExternalOrganizations, organization =>
            organization.RelationshipType == "Vendor"
            && organization.PrimaryDomain == "siemens.com"
            && organization.Industry == "Industrial Automation");
        Assert.Contains(result.World.ExternalOrganizations, organization =>
            organization.RelationshipType == "Customer"
            && organization.Segment is "StrategicAccount" or "RegionalAccount"
            && organization.Industry is "Industrial Equipment" or "Industrial Distribution" or "Automotive Components");
        Assert.Contains(result.World.ExternalOrganizations, organization =>
            organization.RelationshipType == "Partner"
            && !string.IsNullOrWhiteSpace(organization.Industry)
            && !string.IsNullOrWhiteSpace(organization.Segment));
        Assert.DoesNotContain(result.World.ExternalOrganizations, organization =>
            !string.IsNullOrWhiteSpace(organization.Name)
            && System.Text.RegularExpressions.Regex.IsMatch(
                organization.Name,
                @"\b\d+\b$",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant));
        Assert.Contains(result.World.ApplicationCounterpartyLinks, link => link.RelationshipType == "VendorProvided");
        Assert.Contains(result.World.ApplicationCounterpartyLinks, link =>
            link.RelationshipType == "SupplierIntegration"
            && link.IntegrationType == "SupplierQualityExchange"
            && result.World.Applications.Any(application =>
                application.Id == link.ApplicationId
                && application.Name.Contains("Supplier Quality", StringComparison.OrdinalIgnoreCase))
            && result.World.ExternalOrganizations.Any(organization =>
                organization.Id == link.ExternalOrganizationId
                && organization.RelationshipType == "Vendor"
                && organization.Segment == "StrategicSupplier"));
        Assert.Contains(result.World.ApplicationCounterpartyLinks, link =>
            link.RelationshipType == "PartnerIntegration"
            && link.IntegrationType == "LogisticsVisibility"
            && result.World.Applications.Any(application =>
                application.Id == link.ApplicationId
                && application.Name.Contains("Warehouse", StringComparison.OrdinalIgnoreCase))
            && result.World.ExternalOrganizations.Any(organization =>
                organization.Id == link.ExternalOrganizationId
                && organization.RelationshipType == "Partner"
                && organization.Segment == "DistributionPartner"));
        Assert.Contains(result.World.ApplicationCounterpartyLinks, link =>
            link.RelationshipType == "CustomerIntegration"
            && link.IntegrationType == "DealerPortal"
            && result.World.Applications.Any(application =>
                application.Id == link.ApplicationId
                && string.Equals(application.Category, "Sales", StringComparison.OrdinalIgnoreCase))
            && result.World.ExternalOrganizations.Any(organization =>
                organization.Id == link.ExternalOrganizationId
                && organization.RelationshipType == "Customer"
                && organization.Segment == "StrategicAccount"));
        Assert.Contains(result.World.BusinessProcessCounterpartyLinks, link => link.RelationshipType == "Supplier");
        Assert.Contains(result.World.BusinessProcessCounterpartyLinks, link => link.RelationshipType == "Customer");
        Assert.Contains(result.World.BusinessProcessCounterpartyLinks, link => link.RelationshipType == "Partner");
        Assert.Contains(result.World.BusinessProcessCounterpartyLinks, link =>
            link.RelationshipType == "Customer"
            && link.IsPrimary
            && result.World.BusinessProcesses.Any(process =>
                process.Id == link.BusinessProcessId
                && process.Name == "Order to Cash")
            && result.World.ExternalOrganizations.Any(organization =>
                organization.Id == link.ExternalOrganizationId
                && organization.RelationshipType == "Customer"
                && organization.Segment == "StrategicAccount"));
        Assert.Contains(result.World.BusinessProcessCounterpartyLinks, link =>
            link.RelationshipType == "Supplier"
            && link.IsPrimary
            && result.World.BusinessProcesses.Any(process =>
                process.Id == link.BusinessProcessId
                && process.Name == "Plan to Produce")
            && result.World.ExternalOrganizations.Any(organization =>
                organization.Id == link.ExternalOrganizationId
                && organization.RelationshipType == "Vendor"
                && organization.Segment == "StrategicSupplier"));
        Assert.Contains(result.World.BusinessProcessCounterpartyLinks, link =>
            link.RelationshipType == "Partner"
            && result.World.BusinessProcesses.Any(process =>
                process.Id == link.BusinessProcessId
                && (process.Domain == "Operations" || process.Domain == "Engineering"))
            && result.World.ExternalOrganizations.Any(organization =>
                organization.Id == link.ExternalOrganizationId
                && organization.RelationshipType == "Partner"
                && organization.Segment is "DistributionPartner" or "ChannelPartner"));
        Assert.Contains(result.WorldMetadata!.AppliedLayers, layer => layer == "ExternalEcosystem");
    }

    [Fact]
    public void WorldGenerator_Produces_Stable_External_Organization_Names_For_Same_Seed()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var catalogs = new FileSystemCatalogLoader().LoadFromPath(TestEnvironmentPaths.GetCatalogRoot());
        var scenario = new ScenarioDefinition
        {
            Name = "Stable Ecosystem Naming Test",
            IndustryProfile = "Manufacturing",
            GeographyProfile = "Regional-US",
            EmployeeSize = new SizeBand { Minimum = 1800, Maximum = 2600 },
            Applications = new ApplicationProfile
            {
                IncludeApplications = true,
                BaseApplicationCount = 8,
                IncludeLineOfBusinessApplications = true,
                IncludeSaaSApplications = true
            },
            Companies =
            {
                new ScenarioCompanyDefinition
                {
                    Name = "Stable Ecosystem Co",
                    Industry = "Manufacturing",
                    EmployeeCount = 2200,
                    BusinessUnitCount = 4,
                    DepartmentCountPerBusinessUnit = 4,
                    TeamCountPerDepartment = 3,
                    OfficeCount = 4,
                    Countries = { "United States" }
                }
            }
        };

        var first = generator.Generate(new GenerationContext { Seed = 4242, Scenario = scenario }, catalogs);
        var second = generator.Generate(new GenerationContext { Seed = 4242, Scenario = scenario }, catalogs);

        var firstNames = first.World.ExternalOrganizations
            .Select(organization => organization.Name)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var secondNames = second.World.ExternalOrganizations
            .Select(organization => organization.Name)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(firstNames, secondNames);
    }

    [Fact]
    public void WorldGenerator_Resolves_Vendor_Aliases_For_External_Organizations()
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
                    Name = "Vendor Alias Ecosystem Test",
                    IndustryProfile = "Manufacturing",
                    GeographyProfile = "Regional-US",
                    EmployeeSize = new SizeBand { Minimum = 1800, Maximum = 2600 },
                    Applications = new ApplicationProfile
                    {
                        IncludeApplications = true,
                        BaseApplicationCount = 6,
                        IncludeLineOfBusinessApplications = true,
                        IncludeSaaSApplications = true
                    },
                    Companies =
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Vendor Alias Co",
                            Industry = "Manufacturing",
                            EmployeeCount = 2200,
                            BusinessUnitCount = 4,
                            DepartmentCountPerBusinessUnit = 4,
                            TeamCountPerDepartment = 3,
                            OfficeCount = 4,
                            Countries = { "United States" }
                        }
                    }
                }
            },
            new FileSystemCatalogLoader().LoadFromPath(TestEnvironmentPaths.GetCatalogRoot()));

        Assert.Contains(result.World.Applications, application =>
            string.Equals(application.Vendor, "Siemens", StringComparison.OrdinalIgnoreCase)
            || application.Name.Contains("Siemens", StringComparison.OrdinalIgnoreCase)
            || (application.Url?.Contains("siemens.com", StringComparison.OrdinalIgnoreCase) ?? false));
        Assert.Contains(result.World.ExternalOrganizations, organization =>
            string.Equals(organization.Name, "Siemens", StringComparison.OrdinalIgnoreCase)
            && organization.RelationshipType == "Vendor"
            && organization.PrimaryDomain == "siemens.com"
            && organization.Segment == "StrategicSupplier"
            && organization.Industry == "Industrial Automation");
    }

    [Fact]
    public void EcosystemGenerator_Does_Not_Materialize_Commodity_Publishers_As_External_Organizations()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IExternalEcosystemGenerator>();
        var catalogs = new FileSystemCatalogLoader().LoadFromPath(TestEnvironmentPaths.GetCatalogRoot());
        var world = new SyntheticEnterpriseWorld();
        world.Companies.Add(new Company
        {
            Id = "CO-001",
            Name = "Commodity Filter Co",
            Industry = "Manufacturing",
            PrimaryCountry = "United States"
        });
        world.Departments.Add(new Department
        {
            Id = "DEP-001",
            CompanyId = "CO-001",
            Name = "Information Technology"
        });
        world.Offices.Add(new Office
        {
            Id = "OFF-001",
            CompanyId = "CO-001",
            Name = "HQ",
            Country = "United States"
        });
        world.People.AddRange(Enumerable.Range(1, 600).Select(index => new Person
        {
            Id = $"P-{index}",
            CompanyId = "CO-001",
            FirstName = "Alex",
            LastName = $"User{index}",
            DisplayName = $"Alex User{index}",
            EmploymentType = "Employee"
        }));
        world.Applications.AddRange(
        [
            new ApplicationRecord
            {
                Id = "APP-001",
                CompanyId = "CO-001",
                Name = "7-Zip",
                Vendor = "7-Zip",
                Category = "Utilities",
                HostingModel = "Endpoint",
                UserScope = "SingleUser",
                OwnerDepartmentId = "DEP-001",
                Criticality = "Low"
            },
            new ApplicationRecord
            {
                Id = "APP-002",
                CompanyId = "CO-001",
                Name = "Microsoft 365 Apps",
                Vendor = "Microsoft",
                Category = "Productivity",
                HostingModel = "SaaS",
                UserScope = "Enterprise",
                OwnerDepartmentId = "DEP-001",
                Criticality = "High",
                SsoEnabled = true,
                MfaRequired = true
            }
        ]);

        generator.GenerateEcosystem(world, new GenerationContext
        {
            Scenario = new ScenarioDefinition
            {
                Name = "Commodity Filter Test"
            }
        }, catalogs);

        Assert.DoesNotContain(world.ExternalOrganizations, organization =>
            string.Equals(organization.Name, "7-Zip", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(world.ExternalOrganizations, organization =>
            string.Equals(organization.Name, "Microsoft", StringComparison.OrdinalIgnoreCase)
            && organization.RelationshipType == "Vendor"
            && !string.IsNullOrWhiteSpace(organization.RelationshipBasis)
            && !string.IsNullOrWhiteSpace(organization.RelationshipScope)
            && !string.IsNullOrWhiteSpace(organization.RelationshipDefinition));
    }
}
