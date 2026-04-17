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

        var result = service.Audit(world);

        Assert.Contains(result.Warnings, warning => warning.Contains("postal/geocode", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Warnings, warning => warning.Contains("country values", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Warnings, warning => warning.Contains("duplicate person user principal", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Warnings, warning => warning.Contains("duplicate directory account user principal", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Warnings, warning => warning.Contains("duplicate generated passwords", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Warnings, warning => warning.Contains("guest or B2B accounts", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Warnings, warning => warning.Contains("external workforce people", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Warnings, warning => warning.Contains("applications reference vendors", StringComparison.OrdinalIgnoreCase));
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
    }
}
