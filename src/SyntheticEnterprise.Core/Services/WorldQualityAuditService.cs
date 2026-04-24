namespace SyntheticEnterprise.Core.Services;

using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Core.Abstractions;

public sealed class WorldQualityAuditService : IWorldQualityAuditService
{
    public WorldQualityAuditResult Audit(SyntheticEnterpriseWorld world)
    {
        var warnings = new List<string>();
        var metrics = BuildMetrics(world);
        var samples = BuildSamples(world);

        AppendMetricWarning(warnings, metrics, "companies_missing_identity_metadata", "companies are missing legal, domain, website, tagline, headquarters, or phone identity metadata.");
        AppendMetricWarning(warnings, metrics, "offices_missing_geocode", "office records are missing postal/geocode details.");
        AppendMetricWarning(warnings, metrics, "offices_missing_contact_metadata", "office records are missing business phone numbers or headquarters markers.");
        AppendMetricWarning(warnings, metrics, "people_office_country_mismatch", "people have country values that do not match their assigned office.");
        AppendMetricWarning(warnings, metrics, "duplicate_person_display_names", "duplicate person display names were generated.");
        AppendMetricWarning(warnings, metrics, "max_person_display_name_repeat_over_limit", "a person display name is repeating more often than the realism limit allows.");
        AppendMetricWarning(warnings, metrics, "duplicate_person_upns", "duplicate person user principal names were generated.");
        AppendMetricWarning(warnings, metrics, "duplicate_account_upns", "duplicate directory account user principal names were generated.");
        AppendMetricWarning(warnings, metrics, "duplicate_generated_passwords", "duplicate generated passwords were detected.");
        AppendMetricWarning(warnings, metrics, "accounts_missing_temporal_identity_evidence", "accounts are missing temporal identity evidence such as last logon or created/modified timestamps.");
        AppendMetricWarning(warnings, metrics, "workstations_missing_identity_evidence", "workstations are missing both assigned-user and directory-account evidence.");
        AppendMetricWarning(warnings, metrics, "servers_missing_directory_account_evidence", "servers are missing directory-account evidence.");
        AppendMetricWarning(warnings, metrics, "external_people_missing_employer", "external workforce people are missing an employer organization reference.");
        AppendMetricWarning(warnings, metrics, "guest_accounts_missing_metadata", "guest or B2B accounts are missing invitation, tenant, sponsor, or invited-organization metadata.");
        AppendMetricWarning(warnings, metrics, "application_counterparty_links_missing_org", "application counterparty links reference missing external organizations.");
        AppendMetricWarning(warnings, metrics, "process_counterparty_links_missing_org", "business process counterparty links reference missing external organizations.");
        AppendMetricWarning(warnings, metrics, "duplicate_external_org_names", "duplicate external organization names were generated.");
        AppendMetricWarning(warnings, metrics, "external_orgs_missing_identity_metadata", "external organizations are missing legal, domain, website, contact, description, or tagline metadata.");
        AppendMetricWarning(warnings, metrics, "external_orgs_missing_relationship_qualifiers", "external organizations are missing relationship basis, scope, or definition metadata.");
        AppendMetricWarning(warnings, metrics, "numbered_business_unit_names", "business unit names use synthetic numeric suffixes.");
        AppendMetricWarning(warnings, metrics, "numbered_department_names", "department names use synthetic numeric suffixes.");
        AppendMetricWarning(warnings, metrics, "numbered_team_names", "team names use synthetic numeric suffixes.");
        AppendMetricWarning(warnings, metrics, "generic_share_names", "file shares still use generic or legacy naming patterns.");
        AppendMetricWarning(warnings, metrics, "generic_folder_names", "document folders still use generic or sequenced naming patterns.");
        AppendMetricWarning(warnings, metrics, "generic_channel_names", "collaboration channel names still use generic template labels.");
        AppendMetricWarning(warnings, metrics, "business_process_configuration_items", "business processes are surfacing as configuration items.");
        AppendMetricWarning(warnings, metrics, "undersized_policy_surface", "policy and policy-setting counts are below the minimum realism threshold for this scenario.");
        AppendMetricWarning(warnings, metrics, "policies_missing_guid", "policies are missing stable directory or platform identifiers.");
        AppendMetricWarning(warnings, metrics, "policy_settings_missing_path", "policy settings are missing realistic policy or registry paths.");
        AppendMetricWarning(warnings, metrics, "group_policies_without_linked_container", "group policy objects are missing linked container targets.");
        AppendMetricWarning(warnings, metrics, "intune_policies_without_scope_or_assignment", "Intune policies are missing identity-store scope or assignment targets.");
        AppendMetricWarning(warnings, metrics, "conditional_access_policies_without_scope_or_assignment", "conditional access policies are missing identity-store scope or assignment targets.");
        AppendMetricWarning(warnings, metrics, "office_region_country_mismatch", "office region labels do not match the office country for known international mappings.");
        AppendMetricWarning(warnings, metrics, "office_phone_country_mismatch", "office business phone formats do not align with the office country for known international mappings.");

        return new WorldQualityAuditResult
        {
            Warnings = warnings,
            Metrics = metrics,
            Samples = samples
        };
    }

    private static IReadOnlyDictionary<string, int> BuildMetrics(SyntheticEnterpriseWorld world)
    {
        var metrics = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["companies_missing_identity_metadata"] = CountCompaniesMissingIdentityMetadata(world),
            ["offices_missing_geocode"] = CountOfficesMissingGeocode(world),
            ["offices_missing_contact_metadata"] = CountOfficesMissingContactMetadata(world),
            ["people_office_country_mismatch"] = CountPeopleWithOfficeCountryMismatch(world),
            ["duplicate_person_display_names"] = CountDuplicateValues(world.People.Select(person => person.DisplayName)),
            ["max_person_display_name_repeat"] = CountMaxRepeat(world.People.Select(person => person.DisplayName)),
            ["max_person_display_name_repeat_over_limit"] = Math.Max(0, CountMaxRepeat(world.People.Select(person => person.DisplayName)) - 3),
            ["duplicate_person_upns"] = CountDuplicateValues(world.People.Select(person => person.UserPrincipalName)),
            ["duplicate_account_upns"] = CountDuplicateValues(world.Accounts.Select(account => account.UserPrincipalName)),
            ["duplicate_generated_passwords"] = CountDuplicateValues(world.Accounts.Select(account => account.GeneratedPassword)),
            ["accounts_missing_temporal_identity_evidence"] = CountAccountsMissingTemporalEvidence(world),
            ["workstations_missing_identity_evidence"] = CountWorkstationsMissingIdentityEvidence(world),
            ["servers_missing_directory_account_evidence"] = CountServersMissingDirectoryAccountEvidence(world),
            ["external_people_missing_employer"] = CountExternalWorkforceWithoutEmployer(world),
            ["guest_accounts_missing_metadata"] = CountGuestAccountsMissingMetadata(world),
            ["application_counterparty_links_missing_org"] = CountLinksMissingOrganization(
                world.ApplicationCounterpartyLinks.Select(link => link.ExternalOrganizationId),
                world.ExternalOrganizations),
            ["process_counterparty_links_missing_org"] = CountLinksMissingOrganization(
                world.BusinessProcessCounterpartyLinks.Select(link => link.ExternalOrganizationId),
                world.ExternalOrganizations),
            ["duplicate_external_org_names"] = CountDuplicateValues(world.ExternalOrganizations.Select(organization => organization.Name)),
            ["external_orgs_missing_identity_metadata"] = CountExternalOrganizationsMissingIdentityMetadata(world),
            ["external_orgs_missing_relationship_qualifiers"] = CountExternalOrganizationsMissingRelationshipQualifiers(world),
            ["numbered_business_unit_names"] = CountNumberedNames(world.BusinessUnits.Select(unit => unit.Name)),
            ["numbered_department_names"] = CountNumberedNames(world.Departments.Select(department => department.Name)),
            ["numbered_team_names"] = CountNumberedNames(world.Teams.Select(team => team.Name)),
            ["generic_share_names"] = CountGenericShareNames(world),
            ["generic_folder_names"] = CountGenericFolderNames(world),
            ["generic_channel_names"] = CountGenericChannelNames(world),
            ["business_process_configuration_items"] = world.ConfigurationItems.Count(item =>
                string.Equals(item.CiType, "BusinessProcessService", StringComparison.OrdinalIgnoreCase)
                || world.BusinessProcesses.Any(process => string.Equals(process.Name, item.DisplayName, StringComparison.OrdinalIgnoreCase))),
            ["undersized_policy_surface"] = HasUndersizedPolicySurface(world) ? 1 : 0,
            ["policies_missing_guid"] = world.Policies.Count(policy => string.IsNullOrWhiteSpace(policy.PolicyGuid)),
            ["policy_settings_missing_path"] = world.PolicySettings.Count(setting => string.IsNullOrWhiteSpace(setting.PolicyPath)),
            ["group_policies_without_linked_container"] = CountPoliciesMissingTarget(world, "GroupPolicyObject", "Container", "Linked"),
            ["intune_policies_without_scope_or_assignment"] = CountModernPoliciesMissingScopeOrAssignment(world, "Intune"),
            ["conditional_access_policies_without_scope_or_assignment"] = CountModernPoliciesMissingScopeOrAssignment(world, "ConditionalAccessPolicy"),
            ["office_region_country_mismatch"] = CountOfficeRegionCountryMismatches(world),
            ["office_phone_country_mismatch"] = CountOfficePhoneCountryMismatches(world),
            ["company_count"] = world.Companies.Count,
            ["person_count"] = world.People.Count,
            ["policy_count"] = world.Policies.Count,
            ["policy_setting_count"] = world.PolicySettings.Count,
            ["group_count"] = world.Groups.Count,
            ["file_share_count"] = world.FileShares.Count,
            ["collaboration_site_count"] = world.CollaborationSites.Count,
            ["configuration_item_count"] = world.ConfigurationItems.Count,
            ["plugin_generated_record_count"] = world.PluginRecords.Count,
            ["plugin_generated_capability_count"] = world.PluginRecords
                .Select(record => record.PluginCapability)
                .Where(capability => !string.IsNullOrWhiteSpace(capability))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count()
        };

        return metrics;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildSamples(SyntheticEnterpriseWorld world)
    {
        return new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["business_units"] = Sample(world.BusinessUnits.Select(unit => unit.Name)),
            ["departments"] = Sample(world.Departments.Select(department => department.Name)),
            ["teams"] = Sample(world.Teams.Select(team => team.Name)),
            ["file_shares"] = Sample(world.FileShares.Select(share => share.ShareName)),
            ["collaboration_sites"] = Sample(world.CollaborationSites.Select(site => site.Name)),
            ["document_libraries"] = Sample(world.DocumentLibraries.Select(library => library.Name)),
            ["document_folders"] = Sample(world.DocumentFolders.Select(folder => folder.Name)),
            ["groups"] = Sample(world.Groups.Select(group => group.Name)),
            ["policies"] = Sample(world.Policies.Select(policy => policy.Name)),
            ["policy_settings"] = Sample(world.PolicySettings.Select(setting => $"{setting.SettingName} [{setting.PolicyPath}]")),
            ["configuration_items"] = Sample(world.ConfigurationItems.Select(item => item.DisplayName)),
            ["cmdb_sources"] = Sample(world.CmdbSourceRecords.Select(record => record.DisplayName)),
            ["applications"] = Sample(world.Applications.Select(application => application.Name)),
            ["people"] = Sample(world.People.Select(person => person.DisplayName)),
            ["accounts"] = Sample(world.Accounts.Select(account => $"{account.SamAccountName} [{account.AccountType} | {account.IdentityProvider}]")),
            ["servers"] = Sample(world.Servers.Select(server => server.Hostname)),
            ["offices"] = Sample(world.Offices.Select(office => $"{office.Name} [{office.Country} | {office.Region} | {office.BusinessPhone}]")),
            ["identity_stores"] = Sample(world.IdentityStores.Select(store => $"{store.Name} [{store.DirectoryMode} | {store.PrimaryDomain}]")),
            ["plugin_capabilities"] = Sample(world.PluginRecords.Select(record => record.PluginCapability).Distinct(StringComparer.OrdinalIgnoreCase))
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

    private static int CountAccountsMissingTemporalEvidence(SyntheticEnterpriseWorld world)
        => world.Accounts.Count(account =>
            account.WhenCreated is null
            || account.WhenModified is null
            || (ShouldExpectLastLogon(account) && account.LastLogon is null));

    private static int CountWorkstationsMissingIdentityEvidence(SyntheticEnterpriseWorld world)
        => world.Devices.Count(device =>
            (string.Equals(device.DeviceType, "Workstation", StringComparison.OrdinalIgnoreCase)
             || string.Equals(device.DeviceType, "PrivilegedAccessWorkstation", StringComparison.OrdinalIgnoreCase))
            && string.IsNullOrWhiteSpace(device.AssignedPersonId)
            && string.IsNullOrWhiteSpace(device.DirectoryAccountId)
            && string.IsNullOrWhiteSpace(device.OnPremDirectoryAccountId)
            && string.IsNullOrWhiteSpace(device.CloudDirectoryAccountId));

    private static int CountServersMissingDirectoryAccountEvidence(SyntheticEnterpriseWorld world)
        => world.Servers.Count(server =>
            string.IsNullOrWhiteSpace(server.DirectoryAccountId)
            && string.IsNullOrWhiteSpace(server.OnPremDirectoryAccountId)
            && string.IsNullOrWhiteSpace(server.CloudDirectoryAccountId));

    private static bool ShouldExpectLastLogon(DirectoryAccount account)
    {
        if (!account.Enabled)
        {
            return false;
        }

        return !string.Equals(account.UserType, "Guest", StringComparison.OrdinalIgnoreCase)
               || string.Equals(account.InvitationStatus, "Accepted", StringComparison.OrdinalIgnoreCase);
    }

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

    private static int CountLinksMissingOrganization(IEnumerable<string> organizationIds, SyntheticEnterpriseWorld world)
        => CountLinksMissingOrganization(organizationIds, world.ExternalOrganizations);

    private static int CountLinksMissingOrganization(IEnumerable<string> organizationIds, IReadOnlyList<ExternalOrganization> organizations)
    {
        var knownIds = new HashSet<string>(organizations.Select(organization => organization.Id), StringComparer.OrdinalIgnoreCase);
        return organizationIds.Count(organizationId =>
            !string.IsNullOrWhiteSpace(organizationId)
            && !knownIds.Contains(organizationId));
    }

    private static int CountExternalOrganizationsMissingIdentityMetadata(SyntheticEnterpriseWorld world)
        => world.ExternalOrganizations.Count(organization =>
            string.IsNullOrWhiteSpace(organization.LegalName)
            || string.IsNullOrWhiteSpace(organization.PrimaryDomain)
            || string.IsNullOrWhiteSpace(organization.Website)
            || string.IsNullOrWhiteSpace(organization.ContactEmail)
            || string.IsNullOrWhiteSpace(organization.Description)
            || string.IsNullOrWhiteSpace(organization.Tagline));

    private static int CountExternalOrganizationsMissingRelationshipQualifiers(SyntheticEnterpriseWorld world)
        => world.ExternalOrganizations.Count(organization =>
            string.IsNullOrWhiteSpace(organization.RelationshipBasis)
            || string.IsNullOrWhiteSpace(organization.RelationshipScope)
            || string.IsNullOrWhiteSpace(organization.RelationshipDefinition));

    private static int CountNumberedNames(IEnumerable<string?> names)
        => names.Count(name =>
            !string.IsNullOrWhiteSpace(name)
            && System.Text.RegularExpressions.Regex.IsMatch(name!, @"\b\d+\b$", System.Text.RegularExpressions.RegexOptions.CultureInvariant));

    private static int CountGenericShareNames(SyntheticEnterpriseWorld world)
        => world.FileShares.Count(share =>
            !string.IsNullOrWhiteSpace(share.ShareName)
            && (share.ShareName.Contains("-share-", StringComparison.OrdinalIgnoreCase)
                || share.ShareName.StartsWith("home-", StringComparison.OrdinalIgnoreCase)
                || share.ShareName.StartsWith("profile-", StringComparison.OrdinalIgnoreCase)
                || System.Text.RegularExpressions.Regex.IsMatch(share.ShareName, @"^[a-z0-9]+share\d*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant)));

    private static int CountGenericFolderNames(SyntheticEnterpriseWorld world)
        => world.DocumentFolders.Count(folder =>
            !string.IsNullOrWhiteSpace(folder.Name)
            && System.Text.RegularExpressions.Regex.IsMatch(folder.Name, @"(^.+-\d{2,}$)|^(Shared-\d+|Archive-\d+|Working-\d+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant));

    private static int CountGenericChannelNames(SyntheticEnterpriseWorld world)
        => world.CollaborationChannels.Count(channel =>
            string.Equals(channel.Name, "Operations", StringComparison.OrdinalIgnoreCase)
            || string.Equals(channel.Name, "Projects", StringComparison.OrdinalIgnoreCase)
            || System.Text.RegularExpressions.Regex.IsMatch(channel.Name ?? string.Empty, @"\b\d+\b$", System.Text.RegularExpressions.RegexOptions.CultureInvariant));

    private static int CountOfficeRegionCountryMismatches(SyntheticEnterpriseWorld world)
        => world.Offices.Count(office =>
        {
            var expectedRegion = ResolveExpectedRegionForCountry(office.Country);
            return expectedRegion is not null
                   && !string.IsNullOrWhiteSpace(office.Region)
                   && !string.Equals(expectedRegion, office.Region, StringComparison.OrdinalIgnoreCase);
        });

    private static int CountOfficePhoneCountryMismatches(SyntheticEnterpriseWorld world)
        => world.Offices.Count(office =>
        {
            var expectedPrefix = ResolveExpectedPhonePrefixForCountry(office.Country);
            return expectedPrefix is not null
                   && !string.IsNullOrWhiteSpace(office.BusinessPhone)
                   && !office.BusinessPhone.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase);
        });

    private static bool HasUndersizedPolicySurface(SyntheticEnterpriseWorld world)
    {
        var employeeCount = world.People.Count(person => string.Equals(person.EmploymentType, "Employee", StringComparison.OrdinalIgnoreCase));
        var expectedMinimumSettings = employeeCount switch
        {
            >= 10000 => 1500,
            >= 5000 => 1100,
            >= 1000 => 800,
            >= 250 => 500,
            _ => 250
        };

        var expectedMinimumPolicies = employeeCount switch
        {
            >= 10000 => 100,
            >= 5000 => 80,
            >= 1000 => 60,
            >= 250 => 40,
            _ => 20
        };

        return world.PolicySettings.Count < expectedMinimumSettings
               || world.Policies.Count < expectedMinimumPolicies;
    }

    private static int CountPoliciesMissingTarget(
        SyntheticEnterpriseWorld world,
        string policyType,
        string targetType,
        string assignmentMode)
    {
        return world.Policies.Count(policy =>
            string.Equals(policy.PolicyType, policyType, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(policy.Status, "Disabled", StringComparison.OrdinalIgnoreCase)
            && !world.PolicyTargetLinks.Any(link =>
                string.Equals(link.PolicyId, policy.Id, StringComparison.OrdinalIgnoreCase)
                && string.Equals(link.TargetType, targetType, StringComparison.OrdinalIgnoreCase)
                && string.Equals(link.AssignmentMode, assignmentMode, StringComparison.OrdinalIgnoreCase)
                && link.LinkEnabled));
    }

    private static int CountModernPoliciesMissingScopeOrAssignment(SyntheticEnterpriseWorld world, string policyTypeOrPlatform)
    {
        return world.Policies.Count(policy =>
        {
            var matches = string.Equals(policy.PolicyType, policyTypeOrPlatform, StringComparison.OrdinalIgnoreCase)
                          || string.Equals(policy.Platform, policyTypeOrPlatform, StringComparison.OrdinalIgnoreCase);
            if (!matches)
            {
                return false;
            }

            var hasScope = world.PolicyTargetLinks.Any(link =>
                string.Equals(link.PolicyId, policy.Id, StringComparison.OrdinalIgnoreCase)
                && string.Equals(link.TargetType, "IdentityStore", StringComparison.OrdinalIgnoreCase)
                && string.Equals(link.AssignmentMode, "Scope", StringComparison.OrdinalIgnoreCase));

            var hasAssignment = world.PolicyTargetLinks.Any(link =>
                string.Equals(link.PolicyId, policy.Id, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(link.TargetType, "IdentityStore", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(link.AssignmentMode, "DelegatedAdministration", StringComparison.OrdinalIgnoreCase));

            return !hasScope || !hasAssignment;
        });
    }

    private static int CountDuplicateValues(IEnumerable<string?> values)
        => values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value!, StringComparer.OrdinalIgnoreCase)
            .Count(group => group.Count() > 1);

    private static int CountMaxRepeat(IEnumerable<string?> values)
        => values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value!, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Count())
            .DefaultIfEmpty(0)
            .Max();

    private static IReadOnlyList<string> Sample(IEnumerable<string?> values, int take = 6)
        => values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .ToArray();

    private static string? ResolveExpectedRegionForCountry(string? country)
        => country?.Trim() switch
        {
            "United States" => "North America",
            "Canada" => "North America",
            "Mexico" => "North America",
            "United Kingdom" => "Europe",
            "England" => "Europe",
            "Scotland" => "Europe",
            "Wales" => "Europe",
            "Northern Ireland" => "Europe",
            "Australia" => "Oceania",
            "New Zealand" => "Oceania",
            _ => null
        };

    private static string? ResolveExpectedPhonePrefixForCountry(string? country)
        => country?.Trim() switch
        {
            "United States" => "+1",
            "Canada" => "+1",
            "Mexico" => "+52",
            "United Kingdom" => "+44",
            "England" => "+44",
            "Scotland" => "+44",
            "Wales" => "+44",
            "Northern Ireland" => "+44",
            "Australia" => "+61",
            "New Zealand" => "+64",
            _ => null
        };

    private static void AppendMetricWarning(ICollection<string> warnings, IReadOnlyDictionary<string, int> metrics, string metricKey, string message)
    {
        if (metrics.TryGetValue(metricKey, out var count) && count > 0)
        {
            warnings.Add($"World quality audit: {count} {message}");
        }
    }
}
