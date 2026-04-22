using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Core.Services;

namespace SyntheticEnterprise.Core.Tests;

public sealed class WorldQualityAuditServiceTests
{
    [Fact]
    public void Audit_Flags_Guest_Metadata_And_Duplicate_Principals()
    {
        var service = new WorldQualityAuditService();
        var world = new SyntheticEnterpriseWorld();
        world.Offices.Add(new Office
        {
            Id = "OFF-1",
            CompanyId = "COMP-1",
            Name = "HQ",
            Country = "United States",
            PostalCode = "",
            Geocoded = false
        });
        world.People.AddRange(
        [
            new Person
            {
                Id = "P-1",
                CompanyId = "COMP-1",
                FirstName = "Alex",
                LastName = "Carter",
                DisplayName = "Alex Carter",
                Country = "Canada",
                OfficeId = "OFF-1",
                UserPrincipalName = "alex@example.test"
            },
            new Person
            {
                Id = "P-2",
                CompanyId = "COMP-1",
                FirstName = "Jamie",
                LastName = "Singh",
                DisplayName = "Jamie Singh",
                Country = "United States",
                OfficeId = "OFF-1",
                UserPrincipalName = "alex@example.test",
                EmploymentType = "Contractor",
                PersonType = "External",
                SponsorPersonId = "P-1"
            }
        ]);
        world.Accounts.AddRange(
        [
            new DirectoryAccount
            {
                Id = "A-1",
                CompanyId = "COMP-1",
                PersonId = "P-1",
                UserPrincipalName = "alex@example.test",
                GeneratedPassword = "SharedPassword!2A"
            },
            new DirectoryAccount
            {
                Id = "A-2",
                CompanyId = "COMP-1",
                PersonId = "P-2",
                UserPrincipalName = "alex@example.test",
                GeneratedPassword = "SharedPassword!2A",
                UserType = "Guest",
                IdentityProvider = "EntraB2B",
                ExternalAccessCategory = "Guest"
            }
        ]);
        world.Applications.Add(new ApplicationRecord
        {
            Id = "APP-1",
            CompanyId = "COMP-1",
            Name = "CRM",
            Vendor = "Unknown Vendor"
        });
        world.ApplicationCounterpartyLinks.Add(new ApplicationCounterpartyLink
        {
            Id = "APPEXT-1",
            CompanyId = "COMP-1",
            ApplicationId = "APP-1",
            ExternalOrganizationId = "EXT-MISSING",
            RelationshipType = "CustomerIntegration",
            IntegrationType = "Portal",
            Criticality = "Medium"
        });

        var result = service.Audit(world);

        Assert.Contains(result.Warnings, warning => warning.Contains("postal/geocode", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Warnings, warning => warning.Contains("country values", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Warnings, warning => warning.Contains("duplicate person user principal", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Warnings, warning => warning.Contains("duplicate directory account user principal", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Warnings, warning => warning.Contains("duplicate generated passwords", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Warnings, warning => warning.Contains("guest or B2B accounts", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Warnings, warning => warning.Contains("external workforce people", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Warnings, warning => warning.Contains("application counterparty links reference missing external organizations", StringComparison.OrdinalIgnoreCase));
        Assert.True(result.Metrics["offices_missing_geocode"] > 0);
        Assert.True(result.Metrics["duplicate_person_upns"] > 0);
        Assert.True(result.Metrics["duplicate_account_upns"] > 0);
        Assert.Contains("CRM", result.Samples["applications"]);
    }

    [Fact]
    public void Audit_Flags_Missing_Company_And_Office_Identity_Metadata()
    {
        var service = new WorldQualityAuditService();
        var world = new SyntheticEnterpriseWorld();
        world.Companies.Add(new Company
        {
            Id = "COMP-1",
            Name = "Northwind",
            Industry = "Manufacturing"
        });
        world.Offices.AddRange(
        [
            new Office
            {
                Id = "OFF-1",
                CompanyId = "COMP-1",
                Name = "HQ",
                Country = "United States",
                City = "Chicago",
                PostalCode = "60601",
                Geocoded = true,
                Latitude = "41.8781",
                Longitude = "-87.6298"
            },
            new Office
            {
                Id = "OFF-2",
                CompanyId = "COMP-1",
                Name = "Branch",
                Country = "United States",
                City = "Milwaukee",
                PostalCode = "53202",
                Geocoded = true,
                Latitude = "43.0389",
                Longitude = "-87.9065",
                IsHeadquarters = true,
                BusinessPhone = "+1 414-555-0100"
            },
            new Office
            {
                Id = "OFF-3",
                CompanyId = "COMP-1",
                Name = "Annex",
                Country = "United States",
                City = "Madison",
                PostalCode = "53703",
                Geocoded = true,
                Latitude = "43.0731",
                Longitude = "-89.4012",
                IsHeadquarters = true,
                BusinessPhone = "+1 608-555-0100"
            }
        ]);

        var result = service.Audit(world);

        Assert.Contains(result.Warnings, warning => warning.Contains("companies are missing legal, domain, website, tagline, headquarters, or phone identity metadata", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Warnings, warning => warning.Contains("office records are missing business phone numbers or headquarters markers", StringComparison.OrdinalIgnoreCase));
        Assert.True(result.Metrics["companies_missing_identity_metadata"] > 0);
        Assert.True(result.Metrics["offices_missing_contact_metadata"] > 0);
    }

    [Fact]
    public void Audit_Flags_Generic_Naming_And_Undersized_Policy_Surface()
    {
        var service = new WorldQualityAuditService();
        var world = new SyntheticEnterpriseWorld();
        world.BusinessUnits.Add(new BusinessUnit { Id = "BU-1", CompanyId = "COMP-1", Name = "Operations 3" });
        world.Departments.Add(new Department { Id = "DEPT-1", CompanyId = "COMP-1", Name = "Endpoint Engineering 6" });
        world.Teams.Add(new Team { Id = "TEAM-1", CompanyId = "COMP-1", Name = "Commercial Planning 15" });
        world.FileShares.Add(new FileShareRepository { Id = "FS-1", CompanyId = "COMP-1", ShareName = "marketing-share-01" });
        world.DocumentFolders.Add(new DocumentFolder { Id = "FOLDER-1", CompanyId = "COMP-1", Name = "Archive-02" });
        world.CollaborationChannels.Add(new CollaborationChannel { Id = "CHAN-1", CompanyId = "COMP-1", Name = "Operations" });
        world.ConfigurationItems.Add(new ConfigurationItem { Id = "CI-1", CompanyId = "COMP-1", CiType = "BusinessProcessService", DisplayName = "Order to Cash" });
        world.ExternalOrganizations.Add(new ExternalOrganization
        {
            Id = "EXT-1",
            CompanyId = "COMP-1",
            Name = "Northwind Consulting",
            LegalName = "Northwind Consulting LLC",
            Description = "Temporary consulting labor provider.",
            Tagline = "Consulting delivery with measurable outcomes.",
            RelationshipType = "Vendor",
            Industry = "Professional Services",
            Country = "United States",
            PrimaryDomain = "northwindconsulting.com",
            Website = "https://www.northwindconsulting.com",
            ContactEmail = "alliances@northwindconsulting.com",
            TaxIdentifier = "12-3456789",
            Segment = "ConsultingPartner",
            RevenueBand = "Enterprise",
            OwnerDepartmentId = "DEPT-1",
            Criticality = "High"
        });
        world.People.AddRange(Enumerable.Range(1, 300).Select(index => new Person
        {
            Id = $"P-{index}",
            CompanyId = "COMP-1",
            FirstName = "Alex",
            LastName = $"User{index}",
            DisplayName = $"Alex User{index}",
            EmploymentType = "Employee"
        }));
        world.Policies.AddRange(Enumerable.Range(1, 10).Select(index => new PolicyRecord
        {
            Id = $"POL-{index}",
            CompanyId = "COMP-1",
            Name = $"Policy {index}"
        }));
        world.PolicySettings.AddRange(Enumerable.Range(1, 40).Select(index => new PolicySettingRecord
        {
            Id = $"SET-{index}",
            CompanyId = "COMP-1",
            PolicyId = "POL-1",
            SettingName = $"Setting {index}"
        }));

        var result = service.Audit(world);

        Assert.Contains(result.Warnings, warning => warning.Contains("business unit names use synthetic numeric suffixes", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Warnings, warning => warning.Contains("department names use synthetic numeric suffixes", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Warnings, warning => warning.Contains("team names use synthetic numeric suffixes", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Warnings, warning => warning.Contains("file shares still use generic or legacy naming patterns", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Warnings, warning => warning.Contains("document folders still use generic or sequenced naming patterns", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Warnings, warning => warning.Contains("collaboration channel names still use generic template labels", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Warnings, warning => warning.Contains("business processes are surfacing as configuration items", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Warnings, warning => warning.Contains("policy and policy-setting counts are below the minimum realism threshold", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Warnings, warning => warning.Contains("external organizations are missing relationship basis, scope, or definition metadata", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Operations 3", result.Samples["business_units"].Single());
        Assert.Equal("Endpoint Engineering 6", result.Samples["departments"].Single());
        Assert.Equal("Commercial Planning 15", result.Samples["teams"].Single());
        Assert.True(result.Metrics["undersized_policy_surface"] > 0);
    }

    [Fact]
    public void Audit_Flags_International_Region_And_Phone_Mismatches()
    {
        var service = new WorldQualityAuditService();
        var world = new SyntheticEnterpriseWorld();
        world.Offices.AddRange(
        [
            new Office
            {
                Id = "OFF-UK-1",
                CompanyId = "COMP-1",
                Name = "London Office",
                Country = "United Kingdom",
                Region = "North America",
                BusinessPhone = "+1 312 555 0100"
            },
            new Office
            {
                Id = "OFF-MX-1",
                CompanyId = "COMP-1",
                Name = "Monterrey Office",
                Country = "Mexico",
                Region = "North America",
                BusinessPhone = "+52 81 5555 0100"
            }
        ]);
        world.PluginRecords.Add(new PluginGeneratedRecord
        {
            Id = "PLUG-1",
            PluginCapability = "firstparty.itsm",
            RecordType = "ServiceTicket"
        });

        var result = service.Audit(world);

        Assert.Contains(result.Warnings, warning => warning.Contains("office region labels do not match", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Warnings, warning => warning.Contains("office business phone formats do not align", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, result.Metrics["office_region_country_mismatch"]);
        Assert.Equal(1, result.Metrics["office_phone_country_mismatch"]);
        Assert.Equal(1, result.Metrics["plugin_generated_record_count"]);
        Assert.Equal(1, result.Metrics["plugin_generated_capability_count"]);
        Assert.Contains(result.Samples["plugin_capabilities"], capability => string.Equals(capability, "firstparty.itsm", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Samples["offices"], office => office.Contains("London Office", StringComparison.OrdinalIgnoreCase));
    }
}
