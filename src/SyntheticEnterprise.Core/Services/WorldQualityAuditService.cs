namespace SyntheticEnterprise.Core.Services;

using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Core.Abstractions;

public sealed class WorldQualityAuditService : IWorldQualityAuditService
{
    public WorldQualityAuditResult Audit(SyntheticEnterpriseWorld world)
    {
        var warnings = new List<string>();

        AppendIfPositive(warnings, CountCompaniesMissingIdentityMetadata(world), "companies are missing legal, domain, website, tagline, headquarters, or phone identity metadata.");
        AppendIfPositive(warnings, CountOfficesMissingGeocode(world), "office records are missing postal/geocode details.");
        AppendIfPositive(warnings, CountOfficesMissingContactMetadata(world), "office records are missing business phone numbers or headquarters markers.");
        AppendIfPositive(warnings, CountPeopleWithOfficeCountryMismatch(world), "people have country values that do not match their assigned office.");
        AppendIfPositive(warnings, CountDuplicateValues(world.People.Select(person => person.UserPrincipalName)), "duplicate person user principal names were generated.");
        AppendIfPositive(warnings, CountDuplicateValues(world.Accounts.Select(account => account.UserPrincipalName)), "duplicate directory account user principal names were generated.");
        AppendIfPositive(warnings, CountDuplicateValues(world.Accounts.Select(account => account.GeneratedPassword)), "duplicate generated passwords were detected.");
        AppendIfPositive(warnings, CountExternalWorkforceWithoutEmployer(world), "external workforce people are missing an employer organization reference.");
        AppendIfPositive(warnings, CountGuestAccountsMissingMetadata(world), "guest or B2B accounts are missing invitation, tenant, sponsor, or invited-organization metadata.");
        AppendIfPositive(warnings, CountVendorApplicationsWithoutCounterparty(world), "applications reference vendors that were not materialized as external organizations.");
        AppendIfPositive(warnings, CountDuplicateValues(world.ExternalOrganizations.Select(organization => organization.Name)), "duplicate external organization names were generated.");
        AppendIfPositive(warnings, CountExternalOrganizationsMissingIdentityMetadata(world), "external organizations are missing legal, domain, website, contact, description, or tagline metadata.");

        return new WorldQualityAuditResult
        {
            Warnings = warnings
        };
    }

    private static int CountOfficesMissingGeocode(SyntheticEnterpriseWorld world)
        => world.Offices.Count(office =>
            string.IsNullOrWhiteSpace(office.PostalCode)
            || !office.Geocoded
            || string.IsNullOrWhiteSpace(office.Latitude)
            || string.IsNullOrWhiteSpace(office.Longitude));

    private static int CountCompaniesMissingIdentityMetadata(SyntheticEnterpriseWorld world)
    {
        var officeIds = new HashSet<string>(world.Offices.Select(office => office.Id), StringComparer.OrdinalIgnoreCase);
        return world.Companies.Count(company =>
            string.IsNullOrWhiteSpace(company.LegalName)
            || string.IsNullOrWhiteSpace(company.PrimaryCountry)
            || string.IsNullOrWhiteSpace(company.PrimaryDomain)
            || string.IsNullOrWhiteSpace(company.Website)
            || string.IsNullOrWhiteSpace(company.Tagline)
            || string.IsNullOrWhiteSpace(company.PrimaryPhoneNumber)
            || string.IsNullOrWhiteSpace(company.HeadquartersOfficeId)
            || !officeIds.Contains(company.HeadquartersOfficeId));
    }

    private static int CountOfficesMissingContactMetadata(SyntheticEnterpriseWorld world)
    {
        var headquartersByCompany = world.Offices
            .Where(office => office.IsHeadquarters)
            .GroupBy(office => office.CompanyId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        return world.Offices.Count(office =>
            string.IsNullOrWhiteSpace(office.BusinessPhone)
            || !headquartersByCompany.TryGetValue(office.CompanyId, out var headquartersCount)
            || headquartersCount != 1);
    }

    private static int CountPeopleWithOfficeCountryMismatch(SyntheticEnterpriseWorld world)
    {
        var offices = world.Offices.ToDictionary(office => office.Id, StringComparer.OrdinalIgnoreCase);
        return world.People.Count(person =>
            !string.IsNullOrWhiteSpace(person.OfficeId)
            && offices.TryGetValue(person.OfficeId!, out var office)
            && !string.IsNullOrWhiteSpace(person.Country)
            && !string.IsNullOrWhiteSpace(office.Country)
            && !string.Equals(person.Country, office.Country, StringComparison.OrdinalIgnoreCase));
    }

    private static int CountExternalWorkforceWithoutEmployer(SyntheticEnterpriseWorld world)
        => world.People.Count(person =>
            !string.Equals(person.EmploymentType, "Employee", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(person.PersonType, "Guest", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(person.EmployerOrganizationId));

    private static int CountGuestAccountsMissingMetadata(SyntheticEnterpriseWorld world)
    {
        var people = world.People.ToDictionary(person => person.Id, StringComparer.OrdinalIgnoreCase);
        var externalOrganizations = new HashSet<string>(world.ExternalOrganizations.Select(organization => organization.Id), StringComparer.OrdinalIgnoreCase);

        return world.Accounts.Count(account =>
        {
            var guestLike = string.Equals(account.UserType, "Guest", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(account.IdentityProvider, "EntraB2B", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(account.ExternalAccessCategory, "Guest", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(account.ExternalAccessCategory, "ManagedServiceProvider", StringComparison.OrdinalIgnoreCase);
            if (!guestLike)
            {
                return false;
            }

            var missingInvitation = string.IsNullOrWhiteSpace(account.InvitedOrganizationId)
                                    || !externalOrganizations.Contains(account.InvitedOrganizationId);
            var missingTenant = string.IsNullOrWhiteSpace(account.HomeTenantDomain)
                                || string.IsNullOrWhiteSpace(account.ResourceTenantDomain);
            var missingState = string.IsNullOrWhiteSpace(account.InvitationStatus);
            var missingLifecycle = string.IsNullOrWhiteSpace(account.GuestLifecycleState)
                                   || string.IsNullOrWhiteSpace(account.CrossTenantAccessPolicy)
                                   || account.InvitationSentAt is null
                                   || (string.Equals(account.InvitationStatus, "Accepted", StringComparison.OrdinalIgnoreCase)
                                       && account.InvitationRedeemedAt is null);
            var missingGovernance = string.IsNullOrWhiteSpace(account.EntitlementPackageName)
                                    || string.IsNullOrWhiteSpace(account.EntitlementAssignmentState)
                                    || string.IsNullOrWhiteSpace(account.AccessReviewStatus)
                                    || (string.Equals(account.InvitationStatus, "Accepted", StringComparison.OrdinalIgnoreCase)
                                        && account.LastAccessReviewAt is null)
                                    || (account.SponsorLastChangedAt is not null
                                        && string.IsNullOrWhiteSpace(account.PreviousInvitedByAccountId));
            var missingSponsor = string.IsNullOrWhiteSpace(account.PersonId)
                                 || !people.TryGetValue(account.PersonId!, out var person)
                                 || string.IsNullOrWhiteSpace(person.SponsorPersonId)
                                 || string.IsNullOrWhiteSpace(account.InvitedByAccountId);

            return missingInvitation || missingTenant || missingState || missingLifecycle || missingGovernance || missingSponsor;
        });
    }

    private static int CountVendorApplicationsWithoutCounterparty(SyntheticEnterpriseWorld world)
    {
        var vendorNames = new HashSet<string>(
            world.ExternalOrganizations
                .Where(organization => string.Equals(organization.RelationshipType, "Vendor", StringComparison.OrdinalIgnoreCase))
                .Select(organization => organization.Name),
            StringComparer.OrdinalIgnoreCase);

        return world.Applications.Count(application =>
            !string.IsNullOrWhiteSpace(application.Vendor)
            && !vendorNames.Contains(application.Vendor));
    }

    private static int CountExternalOrganizationsMissingIdentityMetadata(SyntheticEnterpriseWorld world)
        => world.ExternalOrganizations.Count(organization =>
            string.IsNullOrWhiteSpace(organization.LegalName)
            || string.IsNullOrWhiteSpace(organization.PrimaryDomain)
            || string.IsNullOrWhiteSpace(organization.Website)
            || string.IsNullOrWhiteSpace(organization.ContactEmail)
            || string.IsNullOrWhiteSpace(organization.Description)
            || string.IsNullOrWhiteSpace(organization.Tagline));

    private static int CountDuplicateValues(IEnumerable<string?> values)
        => values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value!, StringComparer.OrdinalIgnoreCase)
            .Count(group => group.Count() > 1);

    private static void AppendIfPositive(ICollection<string> warnings, int count, string message)
    {
        if (count > 0)
        {
            warnings.Add($"World quality audit: {count} {message}");
        }
    }
}
