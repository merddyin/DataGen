using Microsoft.Extensions.DependencyInjection;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
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
}
