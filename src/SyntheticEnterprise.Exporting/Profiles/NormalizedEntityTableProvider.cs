using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Exporting.Contracts;
using SyntheticEnterprise.Exporting.Services;

namespace SyntheticEnterprise.Exporting.Profiles;

public sealed class NormalizedEntityTableProvider : IEntityTableProvider, IExportRequestAware
{
    private CredentialExportMode _credentialExportMode = CredentialExportMode.Masked;

    public void ApplyRequest(ExportRequest request)
    {
        _credentialExportMode = request.CredentialExportMode;
    }

    public IReadOnlyList<object> GetDescriptors()
    {
        return new object[]
        {
            new EntityTableDescriptor<EnvironmentContainer>
            {
                LogicalName = "containers",
                RelativePathStem = "entities/containers",
                Columns =
                [
                    "id",
                    "company_id",
                    "name",
                    "container_type",
                    "platform",
                    "parent_container_id",
                    "container_path",
                    "purpose",
                    "environment",
                    "blocks_policy_inheritance",
                    "identity_store_id",
                    "cloud_tenant_id",
                    "source_entity_type",
                    "source_entity_id"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.Containers,
                RowProjector = container => new Dictionary<string, object?>
                {
                    ["id"] = container.Id,
                    ["company_id"] = container.CompanyId,
                    ["name"] = container.Name,
                    ["container_type"] = container.ContainerType,
                    ["platform"] = container.Platform,
                    ["parent_container_id"] = container.ParentContainerId,
                    ["container_path"] = container.ContainerPath,
                    ["purpose"] = container.Purpose,
                    ["environment"] = container.Environment,
                    ["blocks_policy_inheritance"] = container.BlocksPolicyInheritance,
                    ["identity_store_id"] = container.IdentityStoreId,
                    ["cloud_tenant_id"] = container.CloudTenantId,
                    ["source_entity_type"] = container.SourceEntityType,
                    ["source_entity_id"] = container.SourceEntityId
                },
                SortKeySelector = container => container.Id
            },
            new EntityTableDescriptor<Company>
            {
                LogicalName = "companies",
                RelativePathStem = "entities/companies",
                Columns =
                [
                    "id",
                    "name",
                    "legal_name",
                    "industry",
                    "primary_country",
                    "primary_domain",
                    "website",
                    "tagline",
                    "headquarters_office_id",
                    "primary_phone_number"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.Companies,
                RowProjector = company => new Dictionary<string, object?>
                {
                    ["id"] = company.Id,
                    ["name"] = company.Name,
                    ["legal_name"] = company.LegalName,
                    ["industry"] = company.Industry,
                    ["primary_country"] = company.PrimaryCountry,
                    ["primary_domain"] = company.PrimaryDomain,
                    ["website"] = company.Website,
                    ["tagline"] = company.Tagline,
                    ["headquarters_office_id"] = company.HeadquartersOfficeId,
                    ["primary_phone_number"] = company.PrimaryPhoneNumber
                },
                SortKeySelector = company => company.Id
            },
            new EntityTableDescriptor<Office>
            {
                LogicalName = "offices",
                RelativePathStem = "entities/offices",
                Columns =
                [
                    "id",
                    "company_id",
                    "name",
                    "region",
                    "country",
                    "state_or_province",
                    "city",
                    "postal_code",
                    "time_zone",
                    "address_mode",
                    "street_address",
                    "building_number",
                    "street_name",
                    "floor_or_suite",
                    "business_phone",
                    "is_headquarters",
                    "latitude",
                    "longitude",
                    "geocoded"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.Offices,
                RowProjector = office => new Dictionary<string, object?>
                {
                    ["id"] = office.Id,
                    ["company_id"] = office.CompanyId,
                    ["name"] = office.Name,
                    ["region"] = office.Region,
                    ["country"] = office.Country,
                    ["state_or_province"] = office.StateOrProvince,
                    ["city"] = office.City,
                    ["postal_code"] = office.PostalCode,
                    ["time_zone"] = office.TimeZone,
                    ["address_mode"] = office.AddressMode,
                    ["street_address"] = FormatStreetAddress(office),
                    ["building_number"] = office.BuildingNumber,
                    ["street_name"] = office.StreetName,
                    ["floor_or_suite"] = office.FloorOrSuite,
                    ["business_phone"] = office.BusinessPhone,
                    ["is_headquarters"] = office.IsHeadquarters,
                    ["latitude"] = office.Latitude,
                    ["longitude"] = office.Longitude,
                    ["geocoded"] = office.Geocoded
                },
                SortKeySelector = office => office.Id
            },
            new EntityTableDescriptor<DirectoryOrganizationalUnit>
            {
                LogicalName = "organizational_units",
                RelativePathStem = "entities/organizational_units",
                Columns =
                [
                    "id",
                    "company_id",
                    "name",
                    "distinguished_name",
                    "parent_ou_id",
                    "purpose"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.OrganizationalUnits,
                RowProjector = ou => new Dictionary<string, object?>
                {
                    ["id"] = ou.Id,
                    ["company_id"] = ou.CompanyId,
                    ["name"] = ou.Name,
                    ["distinguished_name"] = ou.DistinguishedName,
                    ["parent_ou_id"] = ou.ParentOuId,
                    ["purpose"] = ou.Purpose
                },
                SortKeySelector = ou => ou.Id
            },
            new EntityTableDescriptor<IdentityStore>
            {
                LogicalName = "identity_stores",
                RelativePathStem = "entities/identity_stores",
                Columns =
                [
                    "id",
                    "company_id",
                    "name",
                    "store_type",
                    "provider",
                    "primary_domain",
                    "naming_context",
                    "directory_mode",
                    "authentication_model",
                    "environment",
                    "is_primary",
                    "cloud_tenant_id"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.IdentityStores,
                RowProjector = store => new Dictionary<string, object?>
                {
                    ["id"] = store.Id,
                    ["company_id"] = store.CompanyId,
                    ["name"] = store.Name,
                    ["store_type"] = store.StoreType,
                    ["provider"] = store.Provider,
                    ["primary_domain"] = store.PrimaryDomain,
                    ["naming_context"] = store.NamingContext,
                    ["directory_mode"] = store.DirectoryMode,
                    ["authentication_model"] = store.AuthenticationModel,
                    ["environment"] = store.Environment,
                    ["is_primary"] = store.IsPrimary,
                    ["cloud_tenant_id"] = store.CloudTenantId
                },
                SortKeySelector = store => store.Id
            },
            new EntityTableDescriptor<PolicyRecord>
            {
                LogicalName = "policies",
                RelativePathStem = "entities/policies",
                Columns =
                [
                    "id",
                    "company_id",
                    "name",
                    "policy_type",
                    "platform",
                    "category",
                    "environment",
                    "status",
                    "description",
                    "identity_store_id",
                    "cloud_tenant_id",
                    "source_entity_type",
                    "source_entity_id"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.Policies,
                RowProjector = policy => new Dictionary<string, object?>
                {
                    ["id"] = policy.Id,
                    ["company_id"] = policy.CompanyId,
                    ["name"] = policy.Name,
                    ["policy_type"] = policy.PolicyType,
                    ["platform"] = policy.Platform,
                    ["category"] = policy.Category,
                    ["environment"] = policy.Environment,
                    ["status"] = policy.Status,
                    ["description"] = policy.Description,
                    ["identity_store_id"] = policy.IdentityStoreId,
                    ["cloud_tenant_id"] = policy.CloudTenantId,
                    ["source_entity_type"] = policy.SourceEntityType,
                    ["source_entity_id"] = policy.SourceEntityId
                },
                SortKeySelector = policy => policy.Id
            },
            new EntityTableDescriptor<PolicySettingRecord>
            {
                LogicalName = "policy_settings",
                RelativePathStem = "entities/policy_settings",
                Columns =
                [
                    "id",
                    "company_id",
                    "policy_id",
                    "setting_name",
                    "setting_category",
                    "value_type",
                    "configured_value",
                    "is_legacy",
                    "is_conflicting",
                    "source_reference"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.PolicySettings,
                RowProjector = setting => new Dictionary<string, object?>
                {
                    ["id"] = setting.Id,
                    ["company_id"] = setting.CompanyId,
                    ["policy_id"] = setting.PolicyId,
                    ["setting_name"] = setting.SettingName,
                    ["setting_category"] = setting.SettingCategory,
                    ["value_type"] = setting.ValueType,
                    ["configured_value"] = setting.ConfiguredValue,
                    ["is_legacy"] = setting.IsLegacy,
                    ["is_conflicting"] = setting.IsConflicting,
                    ["source_reference"] = setting.SourceReference
                },
                SortKeySelector = setting => setting.Id
            },
            new EntityTableDescriptor<AccessControlEvidenceRecord>
            {
                LogicalName = "access_control_evidence",
                RelativePathStem = "entities/access_control_evidence",
                Columns =
                [
                    "id",
                    "company_id",
                    "principal_object_id",
                    "principal_type",
                    "target_type",
                    "target_id",
                    "right_name",
                    "access_type",
                    "is_inherited",
                    "is_default_entry",
                    "source_system",
                    "inheritance_source_id",
                    "notes"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.AccessControlEvidence,
                RowProjector = evidence => new Dictionary<string, object?>
                {
                    ["id"] = evidence.Id,
                    ["company_id"] = evidence.CompanyId,
                    ["principal_object_id"] = evidence.PrincipalObjectId,
                    ["principal_type"] = evidence.PrincipalType,
                    ["target_type"] = evidence.TargetType,
                    ["target_id"] = evidence.TargetId,
                    ["right_name"] = evidence.RightName,
                    ["access_type"] = evidence.AccessType,
                    ["is_inherited"] = evidence.IsInherited,
                    ["is_default_entry"] = evidence.IsDefaultEntry,
                    ["source_system"] = evidence.SourceSystem,
                    ["inheritance_source_id"] = evidence.InheritanceSourceId,
                    ["notes"] = evidence.Notes
                },
                SortKeySelector = evidence => evidence.Id
            },
            new EntityTableDescriptor<ConfigurationItem>
            {
                LogicalName = "configuration_items",
                RelativePathStem = "entities/configuration_items",
                Columns =
                [
                    "id",
                    "company_id",
                    "ci_key",
                    "name",
                    "display_name",
                    "ci_type",
                    "ci_class",
                    "source_entity_type",
                    "source_entity_id",
                    "manufacturer",
                    "vendor",
                    "model",
                    "version",
                    "serial_number",
                    "asset_tag",
                    "environment",
                    "operational_status",
                    "lifecycle_status",
                    "location_type",
                    "location_id",
                    "business_owner_person_id",
                    "technical_owner_person_id",
                    "support_team_id",
                    "owning_department_id",
                    "owning_lob_id",
                    "service_tier",
                    "service_classification",
                    "business_criticality",
                    "data_sensitivity",
                    "maintenance_window_day",
                    "maintenance_window_start_local",
                    "maintenance_window_duration_minutes",
                    "maintenance_window_time_zone",
                    "maintenance_window_frequency",
                    "install_date",
                    "retirement_date",
                    "last_reviewed_at",
                    "notes"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.ConfigurationItems,
                RowProjector = item => new Dictionary<string, object?>
                {
                    ["id"] = item.Id,
                    ["company_id"] = item.CompanyId,
                    ["ci_key"] = item.CiKey,
                    ["name"] = item.Name,
                    ["display_name"] = item.DisplayName,
                    ["ci_type"] = item.CiType,
                    ["ci_class"] = item.CiClass,
                    ["source_entity_type"] = item.SourceEntityType,
                    ["source_entity_id"] = item.SourceEntityId,
                    ["manufacturer"] = item.Manufacturer,
                    ["vendor"] = item.Vendor,
                    ["model"] = item.Model,
                    ["version"] = item.Version,
                    ["serial_number"] = item.SerialNumber,
                    ["asset_tag"] = item.AssetTag,
                    ["environment"] = item.Environment,
                    ["operational_status"] = item.OperationalStatus,
                    ["lifecycle_status"] = item.LifecycleStatus,
                    ["location_type"] = item.LocationType,
                    ["location_id"] = item.LocationId,
                    ["business_owner_person_id"] = item.BusinessOwnerPersonId,
                    ["technical_owner_person_id"] = item.TechnicalOwnerPersonId,
                    ["support_team_id"] = item.SupportTeamId,
                    ["owning_department_id"] = item.OwningDepartmentId,
                    ["owning_lob_id"] = item.OwningLobId,
                    ["service_tier"] = item.ServiceTier,
                    ["service_classification"] = item.ServiceClassification,
                    ["business_criticality"] = item.BusinessCriticality,
                    ["data_sensitivity"] = item.DataSensitivity,
                    ["maintenance_window_day"] = item.MaintenanceWindow?.DayOfWeek,
                    ["maintenance_window_start_local"] = item.MaintenanceWindow?.StartTimeLocal,
                    ["maintenance_window_duration_minutes"] = item.MaintenanceWindow?.DurationMinutes,
                    ["maintenance_window_time_zone"] = item.MaintenanceWindow?.TimeZone,
                    ["maintenance_window_frequency"] = item.MaintenanceWindow?.Frequency,
                    ["install_date"] = item.InstallDate,
                    ["retirement_date"] = item.RetirementDate,
                    ["last_reviewed_at"] = item.LastReviewedAt,
                    ["notes"] = item.Notes
                },
                SortKeySelector = item => item.Id
            },
            new EntityTableDescriptor<CmdbSourceRecord>
            {
                LogicalName = "cmdb_source_records",
                RelativePathStem = "entities/cmdb_source_records",
                Columns =
                [
                    "id",
                    "company_id",
                    "source_system",
                    "source_record_id",
                    "ci_type",
                    "ci_class",
                    "name",
                    "display_name",
                    "observed_manufacturer",
                    "observed_vendor",
                    "observed_model",
                    "observed_version",
                    "observed_serial_number",
                    "observed_asset_tag",
                    "observed_location",
                    "observed_environment",
                    "observed_operational_status",
                    "observed_lifecycle_status",
                    "observed_business_owner",
                    "observed_technical_owner",
                    "observed_support_group",
                    "observed_owning_lob",
                    "observed_service_tier",
                    "observed_service_classification",
                    "observed_business_criticality",
                    "observed_maintenance_window",
                    "match_status",
                    "confidence",
                    "last_seen",
                    "last_imported"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.CmdbSourceRecords,
                RowProjector = item => new Dictionary<string, object?>
                {
                    ["id"] = item.Id,
                    ["company_id"] = item.CompanyId,
                    ["source_system"] = item.SourceSystem,
                    ["source_record_id"] = item.SourceRecordId,
                    ["ci_type"] = item.CiType,
                    ["ci_class"] = item.CiClass,
                    ["name"] = item.Name,
                    ["display_name"] = item.DisplayName,
                    ["observed_manufacturer"] = item.ObservedManufacturer,
                    ["observed_vendor"] = item.ObservedVendor,
                    ["observed_model"] = item.ObservedModel,
                    ["observed_version"] = item.ObservedVersion,
                    ["observed_serial_number"] = item.ObservedSerialNumber,
                    ["observed_asset_tag"] = item.ObservedAssetTag,
                    ["observed_location"] = item.ObservedLocation,
                    ["observed_environment"] = item.ObservedEnvironment,
                    ["observed_operational_status"] = item.ObservedOperationalStatus,
                    ["observed_lifecycle_status"] = item.ObservedLifecycleStatus,
                    ["observed_business_owner"] = item.ObservedBusinessOwner,
                    ["observed_technical_owner"] = item.ObservedTechnicalOwner,
                    ["observed_support_group"] = item.ObservedSupportGroup,
                    ["observed_owning_lob"] = item.ObservedOwningLob,
                    ["observed_service_tier"] = item.ObservedServiceTier,
                    ["observed_service_classification"] = item.ObservedServiceClassification,
                    ["observed_business_criticality"] = item.ObservedBusinessCriticality,
                    ["observed_maintenance_window"] = item.ObservedMaintenanceWindow,
                    ["match_status"] = item.MatchStatus,
                    ["confidence"] = item.Confidence,
                    ["last_seen"] = item.LastSeen,
                    ["last_imported"] = item.LastImported
                },
                SortKeySelector = item => item.Id
            },
            new EntityTableDescriptor<Person>
            {
                LogicalName = "people",
                RelativePathStem = "entities/people",
                Columns =
                [
                    "id",
                    "company_id",
                    "team_id",
                    "department_id",
                    "first_name",
                    "last_name",
                    "display_name",
                    "title",
                    "manager_person_id",
                    "employee_id",
                    "country",
                    "office_id",
                    "user_principal_name",
                    "employment_type",
                    "person_type",
                    "employer_organization_id",
                    "sponsor_person_id"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.People,
                RowProjector = person => new Dictionary<string, object?>
                {
                    ["id"] = person.Id,
                    ["company_id"] = person.CompanyId,
                    ["team_id"] = person.TeamId,
                    ["department_id"] = person.DepartmentId,
                    ["first_name"] = person.FirstName,
                    ["last_name"] = person.LastName,
                    ["display_name"] = person.DisplayName,
                    ["title"] = person.Title,
                    ["manager_person_id"] = person.ManagerPersonId,
                    ["employee_id"] = person.EmployeeId,
                    ["country"] = person.Country,
                    ["office_id"] = person.OfficeId,
                    ["user_principal_name"] = person.UserPrincipalName,
                    ["employment_type"] = person.EmploymentType,
                    ["person_type"] = person.PersonType,
                    ["employer_organization_id"] = person.EmployerOrganizationId,
                    ["sponsor_person_id"] = person.SponsorPersonId
                },
                SortKeySelector = person => person.Id
            },
            new EntityTableDescriptor<DirectoryAccount>
            {
                LogicalName = "accounts",
                RelativePathStem = "entities/accounts",
                Columns =
                [
                    "id",
                    "company_id",
                    "person_id",
                    "account_type",
                    "sam_account_name",
                    "user_principal_name",
                    "mail",
                    "distinguished_name",
                    "ou_id",
                    "enabled",
                    "privileged",
                    "mfa_enabled",
                    "employee_id",
                    "manager_account_id",
                    "password_profile",
                    "administrative_tier",
                    "generated_password",
                    "generated_password_length",
                    "generated_password_present",
                    "password_last_set",
                    "password_expires",
                    "password_never_expires",
                    "must_change_password_at_next_logon",
                    "user_type",
                    "identity_provider",
                    "invited_organization_id",
                    "invited_by_account_id",
                    "home_tenant_domain",
                    "resource_tenant_domain",
                    "invitation_status",
                    "invitation_sent_at",
                    "invitation_redeemed_at",
                    "access_expires_at",
                    "guest_lifecycle_state",
                    "cross_tenant_access_policy",
                    "external_access_category",
                    "entitlement_package_name",
                    "entitlement_assignment_state",
                    "last_access_review_at",
                    "access_review_status",
                    "previous_invited_by_account_id",
                    "sponsor_last_changed_at"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.Accounts,
                RowProjector = account => new Dictionary<string, object?>
                {
                    ["id"] = account.Id,
                    ["company_id"] = account.CompanyId,
                    ["person_id"] = account.PersonId,
                    ["account_type"] = account.AccountType,
                    ["sam_account_name"] = account.SamAccountName,
                    ["user_principal_name"] = account.UserPrincipalName,
                    ["mail"] = account.Mail,
                    ["distinguished_name"] = account.DistinguishedName,
                    ["ou_id"] = account.OuId,
                    ["enabled"] = account.Enabled,
                    ["privileged"] = account.Privileged,
                    ["mfa_enabled"] = account.MfaEnabled,
                    ["employee_id"] = account.EmployeeId,
                    ["manager_account_id"] = account.ManagerAccountId,
                    ["password_profile"] = account.PasswordProfile,
                    ["administrative_tier"] = account.AdministrativeTier,
                    ["generated_password"] = ProjectPassword(account.GeneratedPassword, _credentialExportMode),
                    ["generated_password_length"] = account.GeneratedPassword?.Length,
                    ["generated_password_present"] = !string.IsNullOrWhiteSpace(account.GeneratedPassword),
                    ["password_last_set"] = account.PasswordLastSet,
                    ["password_expires"] = account.PasswordExpires,
                    ["password_never_expires"] = account.PasswordNeverExpires,
                    ["must_change_password_at_next_logon"] = account.MustChangePasswordAtNextLogon,
                    ["user_type"] = account.UserType,
                    ["identity_provider"] = account.IdentityProvider,
                    ["invited_organization_id"] = account.InvitedOrganizationId,
                    ["invited_by_account_id"] = account.InvitedByAccountId,
                    ["home_tenant_domain"] = account.HomeTenantDomain,
                    ["resource_tenant_domain"] = account.ResourceTenantDomain,
                    ["invitation_status"] = account.InvitationStatus,
                    ["invitation_sent_at"] = account.InvitationSentAt,
                    ["invitation_redeemed_at"] = account.InvitationRedeemedAt,
                    ["access_expires_at"] = account.AccessExpiresAt,
                    ["guest_lifecycle_state"] = account.GuestLifecycleState,
                    ["cross_tenant_access_policy"] = account.CrossTenantAccessPolicy,
                    ["external_access_category"] = account.ExternalAccessCategory,
                    ["entitlement_package_name"] = account.EntitlementPackageName,
                    ["entitlement_assignment_state"] = account.EntitlementAssignmentState,
                    ["last_access_review_at"] = account.LastAccessReviewAt,
                    ["access_review_status"] = account.AccessReviewStatus,
                    ["previous_invited_by_account_id"] = account.PreviousInvitedByAccountId,
                    ["sponsor_last_changed_at"] = account.SponsorLastChangedAt
                },
                SortKeySelector = account => account.Id
            },
            new EntityTableDescriptor<DirectoryGroup>
            {
                LogicalName = "groups",
                RelativePathStem = "entities/groups",
                Columns =
                [
                    "id",
                    "company_id",
                    "name",
                    "group_type",
                    "scope",
                    "mail_enabled",
                    "distinguished_name",
                    "ou_id",
                    "purpose",
                    "administrative_tier"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.Groups,
                RowProjector = group => new Dictionary<string, object?>
                {
                    ["id"] = group.Id,
                    ["company_id"] = group.CompanyId,
                    ["name"] = group.Name,
                    ["group_type"] = group.GroupType,
                    ["scope"] = group.Scope,
                    ["mail_enabled"] = group.MailEnabled,
                    ["distinguished_name"] = group.DistinguishedName,
                    ["ou_id"] = group.OuId,
                    ["purpose"] = group.Purpose,
                    ["administrative_tier"] = group.AdministrativeTier
                },
                SortKeySelector = group => group.Id
            },
            new EntityTableDescriptor<ManagedDevice>
            {
                LogicalName = "devices",
                RelativePathStem = "entities/devices",
                Columns =
                [
                    "id",
                    "company_id",
                    "device_type",
                    "hostname",
                    "asset_tag",
                    "serial_number",
                    "manufacturer",
                    "model",
                    "operating_system",
                    "operating_system_version",
                    "assigned_person_id",
                    "assigned_office_id",
                    "directory_account_id",
                    "ou_id",
                    "distinguished_name",
                    "domain_joined",
                    "compliance_state",
                    "last_seen"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.Devices,
                RowProjector = device => new Dictionary<string, object?>
                {
                    ["id"] = device.Id,
                    ["company_id"] = device.CompanyId,
                    ["device_type"] = device.DeviceType,
                    ["hostname"] = device.Hostname,
                    ["asset_tag"] = device.AssetTag,
                    ["serial_number"] = device.SerialNumber,
                    ["manufacturer"] = device.Manufacturer,
                    ["model"] = device.Model,
                    ["operating_system"] = device.OperatingSystem,
                    ["operating_system_version"] = device.OperatingSystemVersion,
                    ["assigned_person_id"] = device.AssignedPersonId,
                    ["assigned_office_id"] = device.AssignedOfficeId,
                    ["directory_account_id"] = device.DirectoryAccountId,
                    ["ou_id"] = device.OuId,
                    ["distinguished_name"] = device.DistinguishedName,
                    ["domain_joined"] = device.DomainJoined,
                    ["compliance_state"] = device.ComplianceState,
                    ["last_seen"] = device.LastSeen
                },
                SortKeySelector = device => device.Id
            },
            new EntityTableDescriptor<ServerAsset>
            {
                LogicalName = "servers",
                RelativePathStem = "entities/servers",
                Columns =
                [
                    "id",
                    "company_id",
                    "hostname",
                    "server_role",
                    "environment",
                    "operating_system",
                    "operating_system_version",
                    "office_id",
                    "ou_id",
                    "distinguished_name",
                    "domain_joined",
                    "owner_team_id",
                    "criticality"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.Servers,
                RowProjector = server => new Dictionary<string, object?>
                {
                    ["id"] = server.Id,
                    ["company_id"] = server.CompanyId,
                    ["hostname"] = server.Hostname,
                    ["server_role"] = server.ServerRole,
                    ["environment"] = server.Environment,
                    ["operating_system"] = server.OperatingSystem,
                    ["operating_system_version"] = server.OperatingSystemVersion,
                    ["office_id"] = server.OfficeId,
                    ["ou_id"] = server.OuId,
                    ["distinguished_name"] = server.DistinguishedName,
                    ["domain_joined"] = server.DomainJoined,
                    ["owner_team_id"] = server.OwnerTeamId,
                    ["criticality"] = server.Criticality
                },
                SortKeySelector = server => server.Id
            },
            new EntityTableDescriptor<NetworkAsset>
            {
                LogicalName = "network_assets",
                RelativePathStem = "entities/network_assets",
                Columns =
                [
                    "id",
                    "company_id",
                    "hostname",
                    "asset_type",
                    "office_id",
                    "vendor",
                    "model"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.NetworkAssets,
                RowProjector = asset => new Dictionary<string, object?>
                {
                    ["id"] = asset.Id,
                    ["company_id"] = asset.CompanyId,
                    ["hostname"] = asset.Hostname,
                    ["asset_type"] = asset.AssetType,
                    ["office_id"] = asset.OfficeId,
                    ["vendor"] = asset.Vendor,
                    ["model"] = asset.Model
                },
                SortKeySelector = asset => asset.Id
            },
            new EntityTableDescriptor<SoftwarePackage>
            {
                LogicalName = "software_packages",
                RelativePathStem = "entities/software_packages",
                Columns =
                [
                    "id",
                    "name",
                    "category",
                    "vendor",
                    "version"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.SoftwarePackages,
                RowProjector = package => new Dictionary<string, object?>
                {
                    ["id"] = package.Id,
                    ["name"] = package.Name,
                    ["category"] = package.Category,
                    ["vendor"] = package.Vendor,
                    ["version"] = package.Version
                },
                SortKeySelector = package => package.Id
            },
            new EntityTableDescriptor<EndpointAdministrativeAssignment>
            {
                LogicalName = "endpoint_administrative_assignments",
                RelativePathStem = "entities/endpoint_administrative_assignments",
                Columns =
                [
                    "id",
                    "company_id",
                    "endpoint_type",
                    "endpoint_id",
                    "principal_object_id",
                    "principal_type",
                    "access_role",
                    "administrative_tier",
                    "assignment_scope",
                    "management_plane"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.EndpointAdministrativeAssignments,
                RowProjector = assignment => new Dictionary<string, object?>
                {
                    ["id"] = assignment.Id,
                    ["company_id"] = assignment.CompanyId,
                    ["endpoint_type"] = assignment.EndpointType,
                    ["endpoint_id"] = assignment.EndpointId,
                    ["principal_object_id"] = assignment.PrincipalObjectId,
                    ["principal_type"] = assignment.PrincipalType,
                    ["access_role"] = assignment.AccessRole,
                    ["administrative_tier"] = assignment.AdministrativeTier,
                    ["assignment_scope"] = assignment.AssignmentScope,
                    ["management_plane"] = assignment.ManagementPlane
                },
                SortKeySelector = assignment => assignment.Id
            },
            new EntityTableDescriptor<EndpointPolicyBaseline>
            {
                LogicalName = "endpoint_policy_baselines",
                RelativePathStem = "entities/endpoint_policy_baselines",
                Columns =
                [
                    "id",
                    "company_id",
                    "endpoint_type",
                    "endpoint_id",
                    "policy_name",
                    "policy_category",
                    "assigned_from",
                    "enforcement_mode",
                    "desired_state",
                    "current_state",
                    "administrative_tier"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.EndpointPolicyBaselines,
                RowProjector = baseline => new Dictionary<string, object?>
                {
                    ["id"] = baseline.Id,
                    ["company_id"] = baseline.CompanyId,
                    ["endpoint_type"] = baseline.EndpointType,
                    ["endpoint_id"] = baseline.EndpointId,
                    ["policy_name"] = baseline.PolicyName,
                    ["policy_category"] = baseline.PolicyCategory,
                    ["assigned_from"] = baseline.AssignedFrom,
                    ["enforcement_mode"] = baseline.EnforcementMode,
                    ["desired_state"] = baseline.DesiredState,
                    ["current_state"] = baseline.CurrentState,
                    ["administrative_tier"] = baseline.AdministrativeTier
                },
                SortKeySelector = baseline => baseline.Id
            },
            new EntityTableDescriptor<EndpointLocalGroupMember>
            {
                LogicalName = "endpoint_local_group_members",
                RelativePathStem = "entities/endpoint_local_group_members",
                Columns =
                [
                    "id",
                    "company_id",
                    "endpoint_type",
                    "endpoint_id",
                    "local_group_name",
                    "principal_object_id",
                    "principal_type",
                    "principal_name",
                    "membership_source",
                    "administrative_tier"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.EndpointLocalGroupMembers,
                RowProjector = member => new Dictionary<string, object?>
                {
                    ["id"] = member.Id,
                    ["company_id"] = member.CompanyId,
                    ["endpoint_type"] = member.EndpointType,
                    ["endpoint_id"] = member.EndpointId,
                    ["local_group_name"] = member.LocalGroupName,
                    ["principal_object_id"] = member.PrincipalObjectId,
                    ["principal_type"] = member.PrincipalType,
                    ["principal_name"] = member.PrincipalName,
                    ["membership_source"] = member.MembershipSource,
                    ["administrative_tier"] = member.AdministrativeTier
                },
                SortKeySelector = member => member.Id
            },
            new EntityTableDescriptor<ApplicationRecord>
            {
                LogicalName = "applications",
                RelativePathStem = "entities/applications",
                Columns =
                [
                    "id",
                    "company_id",
                    "name",
                    "category",
                    "vendor",
                    "business_capability",
                    "hosting_model",
                    "environment",
                    "criticality",
                    "data_sensitivity",
                    "user_scope",
                    "owner_department_id",
                    "url",
                    "sso_enabled",
                    "mfa_required"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.Applications,
                RowProjector = application => new Dictionary<string, object?>
                {
                    ["id"] = application.Id,
                    ["company_id"] = application.CompanyId,
                    ["name"] = application.Name,
                    ["category"] = application.Category,
                    ["vendor"] = application.Vendor,
                    ["business_capability"] = application.BusinessCapability,
                    ["hosting_model"] = application.HostingModel,
                    ["environment"] = application.Environment,
                    ["criticality"] = application.Criticality,
                    ["data_sensitivity"] = application.DataSensitivity,
                    ["user_scope"] = application.UserScope,
                    ["owner_department_id"] = application.OwnerDepartmentId,
                    ["url"] = application.Url,
                    ["sso_enabled"] = application.SsoEnabled,
                    ["mfa_required"] = application.MfaRequired
                },
                SortKeySelector = application => application.Id
            },
            new EntityTableDescriptor<ApplicationService>
            {
                LogicalName = "application_services",
                RelativePathStem = "entities/application_services",
                Columns =
                [
                    "id",
                    "company_id",
                    "application_id",
                    "name",
                    "service_type",
                    "runtime",
                    "deployment_model",
                    "environment",
                    "owner_team_id",
                    "criticality"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.ApplicationServices,
                RowProjector = service => new Dictionary<string, object?>
                {
                    ["id"] = service.Id,
                    ["company_id"] = service.CompanyId,
                    ["application_id"] = service.ApplicationId,
                    ["name"] = service.Name,
                    ["service_type"] = service.ServiceType,
                    ["runtime"] = service.Runtime,
                    ["deployment_model"] = service.DeploymentModel,
                    ["environment"] = service.Environment,
                    ["owner_team_id"] = service.OwnerTeamId,
                    ["criticality"] = service.Criticality
                },
                SortKeySelector = service => service.Id
            },
            new EntityTableDescriptor<CloudTenant>
            {
                LogicalName = "cloud_tenants",
                RelativePathStem = "entities/cloud_tenants",
                Columns =
                [
                    "id",
                    "company_id",
                    "provider",
                    "tenant_type",
                    "name",
                    "primary_domain",
                    "region",
                    "authentication_model",
                    "environment",
                    "admin_department_id"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.CloudTenants,
                RowProjector = tenant => new Dictionary<string, object?>
                {
                    ["id"] = tenant.Id,
                    ["company_id"] = tenant.CompanyId,
                    ["provider"] = tenant.Provider,
                    ["tenant_type"] = tenant.TenantType,
                    ["name"] = tenant.Name,
                    ["primary_domain"] = tenant.PrimaryDomain,
                    ["region"] = tenant.Region,
                    ["authentication_model"] = tenant.AuthenticationModel,
                    ["environment"] = tenant.Environment,
                    ["admin_department_id"] = tenant.AdminDepartmentId
                },
                SortKeySelector = tenant => tenant.Id
            },
            new EntityTableDescriptor<BusinessProcess>
            {
                LogicalName = "business_processes",
                RelativePathStem = "entities/business_processes",
                Columns =
                [
                    "id",
                    "company_id",
                    "name",
                    "domain",
                    "business_capability",
                    "owner_department_id",
                    "operating_model",
                    "process_scope",
                    "criticality",
                    "customer_facing"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.BusinessProcesses,
                RowProjector = process => new Dictionary<string, object?>
                {
                    ["id"] = process.Id,
                    ["company_id"] = process.CompanyId,
                    ["name"] = process.Name,
                    ["domain"] = process.Domain,
                    ["business_capability"] = process.BusinessCapability,
                    ["owner_department_id"] = process.OwnerDepartmentId,
                    ["operating_model"] = process.OperatingModel,
                    ["process_scope"] = process.ProcessScope,
                    ["criticality"] = process.Criticality,
                    ["customer_facing"] = process.CustomerFacing
                },
                SortKeySelector = process => process.Id
            },
            new EntityTableDescriptor<ExternalOrganization>
            {
                LogicalName = "external_organizations",
                RelativePathStem = "entities/external_organizations",
                Columns =
                [
                    "id",
                    "company_id",
                    "name",
                    "legal_name",
                    "description",
                    "tagline",
                    "relationship_type",
                    "industry",
                    "country",
                    "primary_domain",
                    "website",
                    "contact_email",
                    "tax_identifier",
                    "segment",
                    "revenue_band",
                    "owner_department_id",
                    "criticality"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.ExternalOrganizations,
                RowProjector = organization => new Dictionary<string, object?>
                {
                    ["id"] = organization.Id,
                    ["company_id"] = organization.CompanyId,
                    ["name"] = organization.Name,
                    ["legal_name"] = organization.LegalName,
                    ["description"] = organization.Description,
                    ["tagline"] = organization.Tagline,
                    ["relationship_type"] = organization.RelationshipType,
                    ["industry"] = organization.Industry,
                    ["country"] = organization.Country,
                    ["primary_domain"] = organization.PrimaryDomain,
                    ["website"] = organization.Website,
                    ["contact_email"] = organization.ContactEmail,
                    ["tax_identifier"] = organization.TaxIdentifier,
                    ["segment"] = organization.Segment,
                    ["revenue_band"] = organization.RevenueBand,
                    ["owner_department_id"] = organization.OwnerDepartmentId,
                    ["criticality"] = organization.Criticality
                },
                SortKeySelector = organization => organization.Id
            },
            new EntityTableDescriptor<CrossTenantAccessPolicyRecord>
            {
                LogicalName = "cross_tenant_access_policies",
                RelativePathStem = "entities/cross_tenant_access_policies",
                Columns =
                [
                    "id",
                    "company_id",
                    "external_organization_id",
                    "resource_tenant_domain",
                    "home_tenant_domain",
                    "relationship_type",
                    "policy_name",
                    "access_direction",
                    "trust_level",
                    "default_access",
                    "conditional_access_profile",
                    "allowed_resource_scope",
                    "b2b_collaboration_enabled",
                    "inbound_trust_mfa",
                    "inbound_trust_compliant_device",
                    "allow_invitations",
                    "entitlement_management_enabled"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.CrossTenantAccessPolicies,
                RowProjector = policy => new Dictionary<string, object?>
                {
                    ["id"] = policy.Id,
                    ["company_id"] = policy.CompanyId,
                    ["external_organization_id"] = policy.ExternalOrganizationId,
                    ["resource_tenant_domain"] = policy.ResourceTenantDomain,
                    ["home_tenant_domain"] = policy.HomeTenantDomain,
                    ["relationship_type"] = policy.RelationshipType,
                    ["policy_name"] = policy.PolicyName,
                    ["access_direction"] = policy.AccessDirection,
                    ["trust_level"] = policy.TrustLevel,
                    ["default_access"] = policy.DefaultAccess,
                    ["conditional_access_profile"] = policy.ConditionalAccessProfile,
                    ["allowed_resource_scope"] = policy.AllowedResourceScope,
                    ["b2b_collaboration_enabled"] = policy.B2BCollaborationEnabled,
                    ["inbound_trust_mfa"] = policy.InboundTrustMfa,
                    ["inbound_trust_compliant_device"] = policy.InboundTrustCompliantDevice,
                    ["allow_invitations"] = policy.AllowInvitations,
                    ["entitlement_management_enabled"] = policy.EntitlementManagementEnabled
                },
                SortKeySelector = policy => policy.Id
            },
            new EntityTableDescriptor<CrossTenantAccessEvent>
            {
                LogicalName = "cross_tenant_access_events",
                RelativePathStem = "entities/cross_tenant_access_events",
                Columns =
                [
                    "id",
                    "company_id",
                    "account_id",
                    "external_organization_id",
                    "event_type",
                    "event_status",
                    "event_category",
                    "actor_account_id",
                    "policy_id",
                    "resource_reference",
                    "entitlement_package_name",
                    "review_decision",
                    "source_system",
                    "event_at"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.CrossTenantAccessEvents,
                RowProjector = accessEvent => new Dictionary<string, object?>
                {
                    ["id"] = accessEvent.Id,
                    ["company_id"] = accessEvent.CompanyId,
                    ["account_id"] = accessEvent.AccountId,
                    ["external_organization_id"] = accessEvent.ExternalOrganizationId,
                    ["event_type"] = accessEvent.EventType,
                    ["event_status"] = accessEvent.EventStatus,
                    ["event_category"] = accessEvent.EventCategory,
                    ["actor_account_id"] = accessEvent.ActorAccountId,
                    ["policy_id"] = accessEvent.PolicyId,
                    ["resource_reference"] = accessEvent.ResourceReference,
                    ["entitlement_package_name"] = accessEvent.EntitlementPackageName,
                    ["review_decision"] = accessEvent.ReviewDecision,
                    ["source_system"] = accessEvent.SourceSystem,
                    ["event_at"] = accessEvent.EventAt
                },
                SortKeySelector = accessEvent => accessEvent.Id
            },
            new EntityTableDescriptor<ObservedEntitySnapshot>
            {
                LogicalName = "observed_entity_snapshots",
                RelativePathStem = "entities/observed_entity_snapshots",
                Columns =
                [
                    "id",
                    "company_id",
                    "source_system",
                    "entity_type",
                    "entity_id",
                    "display_name",
                    "observed_state",
                    "ground_truth_state",
                    "drift_type",
                    "environment",
                    "owner_reference",
                    "recorded_at"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.ObservedEntitySnapshots,
                RowProjector = snapshot => new Dictionary<string, object?>
                {
                    ["id"] = snapshot.Id,
                    ["company_id"] = snapshot.CompanyId,
                    ["source_system"] = snapshot.SourceSystem,
                    ["entity_type"] = snapshot.EntityType,
                    ["entity_id"] = snapshot.EntityId,
                    ["display_name"] = snapshot.DisplayName,
                    ["observed_state"] = snapshot.ObservedState,
                    ["ground_truth_state"] = snapshot.GroundTruthState,
                    ["drift_type"] = snapshot.DriftType,
                    ["environment"] = snapshot.Environment,
                    ["owner_reference"] = snapshot.OwnerReference,
                    ["recorded_at"] = snapshot.RecordedAt
                },
                SortKeySelector = snapshot => snapshot.Id
            },
            new EntityTableDescriptor<DatabaseRepository>
            {
                LogicalName = "databases",
                RelativePathStem = "entities/databases",
                Columns =
                [
                    "id",
                    "company_id",
                    "name",
                    "engine",
                    "environment",
                    "size_gb",
                    "owner_department_id",
                    "associated_application_id",
                    "host_server_id",
                    "sensitivity"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.Databases,
                RowProjector = database => new Dictionary<string, object?>
                {
                    ["id"] = database.Id,
                    ["company_id"] = database.CompanyId,
                    ["name"] = database.Name,
                    ["engine"] = database.Engine,
                    ["environment"] = database.Environment,
                    ["size_gb"] = database.SizeGb,
                    ["owner_department_id"] = database.OwnerDepartmentId,
                    ["associated_application_id"] = database.AssociatedApplicationId,
                    ["host_server_id"] = database.HostServerId,
                    ["sensitivity"] = database.Sensitivity
                },
                SortKeySelector = database => database.Id
            },
            new EntityTableDescriptor<FileShareRepository>
            {
                LogicalName = "file_shares",
                RelativePathStem = "entities/file_shares",
                Columns =
                [
                    "id",
                    "company_id",
                    "share_name",
                    "unc_path",
                    "owner_department_id",
                    "owner_person_id",
                    "host_server_id",
                    "share_purpose",
                    "file_count",
                    "folder_count",
                    "total_size_gb",
                    "access_model",
                    "sensitivity"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.FileShares,
                RowProjector = share => new Dictionary<string, object?>
                {
                    ["id"] = share.Id,
                    ["company_id"] = share.CompanyId,
                    ["share_name"] = share.ShareName,
                    ["unc_path"] = share.UncPath,
                    ["owner_department_id"] = share.OwnerDepartmentId,
                    ["owner_person_id"] = share.OwnerPersonId,
                    ["host_server_id"] = share.HostServerId,
                    ["share_purpose"] = share.SharePurpose,
                    ["file_count"] = share.FileCount,
                    ["folder_count"] = share.FolderCount,
                    ["total_size_gb"] = share.TotalSizeGb,
                    ["access_model"] = share.AccessModel,
                    ["sensitivity"] = share.Sensitivity
                },
                SortKeySelector = share => share.Id
            },
            new EntityTableDescriptor<CollaborationSite>
            {
                LogicalName = "collaboration_sites",
                RelativePathStem = "entities/collaboration_sites",
                Columns =
                [
                    "id",
                    "company_id",
                    "platform",
                    "name",
                    "url",
                    "owner_person_id",
                    "owner_department_id",
                    "member_count",
                    "file_count",
                    "total_size_gb",
                    "privacy_type",
                    "workspace_type"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.CollaborationSites,
                RowProjector = site => new Dictionary<string, object?>
                {
                    ["id"] = site.Id,
                    ["company_id"] = site.CompanyId,
                    ["platform"] = site.Platform,
                    ["name"] = site.Name,
                    ["url"] = site.Url,
                    ["owner_person_id"] = site.OwnerPersonId,
                    ["owner_department_id"] = site.OwnerDepartmentId,
                    ["member_count"] = site.MemberCount,
                    ["file_count"] = site.FileCount,
                    ["total_size_gb"] = site.TotalSizeGb,
                    ["privacy_type"] = site.PrivacyType,
                    ["workspace_type"] = site.WorkspaceType
                },
                SortKeySelector = site => site.Id
            },
            new EntityTableDescriptor<CollaborationChannel>
            {
                LogicalName = "collaboration_channels",
                RelativePathStem = "entities/collaboration_channels",
                Columns =
                [
                    "id",
                    "company_id",
                    "collaboration_site_id",
                    "name",
                    "channel_type",
                    "member_count",
                    "message_count",
                    "file_count"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.CollaborationChannels,
                RowProjector = channel => new Dictionary<string, object?>
                {
                    ["id"] = channel.Id,
                    ["company_id"] = channel.CompanyId,
                    ["collaboration_site_id"] = channel.CollaborationSiteId,
                    ["name"] = channel.Name,
                    ["channel_type"] = channel.ChannelType,
                    ["member_count"] = channel.MemberCount,
                    ["message_count"] = channel.MessageCount,
                    ["file_count"] = channel.FileCount
                },
                SortKeySelector = channel => channel.Id
            },
            new EntityTableDescriptor<CollaborationChannelTab>
            {
                LogicalName = "collaboration_channel_tabs",
                RelativePathStem = "entities/collaboration_channel_tabs",
                Columns =
                [
                    "id",
                    "company_id",
                    "collaboration_channel_id",
                    "name",
                    "tab_type",
                    "target_type",
                    "target_id",
                    "target_reference",
                    "vendor",
                    "is_pinned"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.CollaborationChannelTabs,
                RowProjector = tab => new Dictionary<string, object?>
                {
                    ["id"] = tab.Id,
                    ["company_id"] = tab.CompanyId,
                    ["collaboration_channel_id"] = tab.CollaborationChannelId,
                    ["name"] = tab.Name,
                    ["tab_type"] = tab.TabType,
                    ["target_type"] = tab.TargetType,
                    ["target_id"] = tab.TargetId,
                    ["target_reference"] = tab.TargetReference,
                    ["vendor"] = tab.Vendor,
                    ["is_pinned"] = tab.IsPinned
                },
                SortKeySelector = tab => tab.Id
            },
            new EntityTableDescriptor<DocumentLibrary>
            {
                LogicalName = "document_libraries",
                RelativePathStem = "entities/document_libraries",
                Columns =
                [
                    "id",
                    "company_id",
                    "collaboration_site_id",
                    "name",
                    "template_type",
                    "item_count",
                    "total_size_gb",
                    "sensitivity"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.DocumentLibraries,
                RowProjector = library => new Dictionary<string, object?>
                {
                    ["id"] = library.Id,
                    ["company_id"] = library.CompanyId,
                    ["collaboration_site_id"] = library.CollaborationSiteId,
                    ["name"] = library.Name,
                    ["template_type"] = library.TemplateType,
                    ["item_count"] = library.ItemCount,
                    ["total_size_gb"] = library.TotalSizeGb,
                    ["sensitivity"] = library.Sensitivity
                },
                SortKeySelector = library => library.Id
            },
            new EntityTableDescriptor<SitePage>
            {
                LogicalName = "site_pages",
                RelativePathStem = "entities/site_pages",
                Columns =
                [
                    "id",
                    "company_id",
                    "collaboration_site_id",
                    "title",
                    "page_type",
                    "author_person_id",
                    "associated_library_id",
                    "view_count",
                    "last_modified",
                    "promoted_state"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.SitePages,
                RowProjector = page => new Dictionary<string, object?>
                {
                    ["id"] = page.Id,
                    ["company_id"] = page.CompanyId,
                    ["collaboration_site_id"] = page.CollaborationSiteId,
                    ["title"] = page.Title,
                    ["page_type"] = page.PageType,
                    ["author_person_id"] = page.AuthorPersonId,
                    ["associated_library_id"] = page.AssociatedLibraryId,
                    ["view_count"] = page.ViewCount,
                    ["last_modified"] = page.LastModified,
                    ["promoted_state"] = page.PromotedState
                },
                SortKeySelector = page => page.Id
            },
            new EntityTableDescriptor<DocumentFolder>
            {
                LogicalName = "document_folders",
                RelativePathStem = "entities/document_folders",
                Columns =
                [
                    "id",
                    "company_id",
                    "document_library_id",
                    "parent_folder_id",
                    "name",
                    "folder_type",
                    "depth",
                    "item_count",
                    "total_size_gb",
                    "sensitivity"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.DocumentFolders,
                RowProjector = folder => new Dictionary<string, object?>
                {
                    ["id"] = folder.Id,
                    ["company_id"] = folder.CompanyId,
                    ["document_library_id"] = folder.DocumentLibraryId,
                    ["parent_folder_id"] = folder.ParentFolderId,
                    ["name"] = folder.Name,
                    ["folder_type"] = folder.FolderType,
                    ["depth"] = folder.Depth,
                    ["item_count"] = folder.ItemCount,
                    ["total_size_gb"] = folder.TotalSizeGb,
                    ["sensitivity"] = folder.Sensitivity
                },
                SortKeySelector = folder => folder.Id
            },
            new EntityTableDescriptor<PluginGeneratedRecord>
            {
                LogicalName = "plugin_generated_records",
                RelativePathStem = "entities/plugin_generated_records",
                Columns =
                [
                    "id",
                    "plugin_capability",
                    "record_type",
                    "associated_entity_type",
                    "associated_entity_id",
                    "properties_json",
                    "json_payload"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.PluginRecords
                    .Where(record => !IsRelationshipRecord(record))
                    .ToList(),
                RowProjector = record => new Dictionary<string, object?>
                {
                    ["id"] = record.Id,
                    ["plugin_capability"] = record.PluginCapability,
                    ["record_type"] = record.RecordType,
                    ["associated_entity_type"] = record.AssociatedEntityType,
                    ["associated_entity_id"] = record.AssociatedEntityId,
                    ["properties_json"] = SerializeProperties(record.Properties),
                    ["json_payload"] = record.JsonPayload
                },
                SortKeySelector = record => record.Id
            }
        };
    }

    private static GenerationResult GetGenerationResult(object input)
    {
        var candidate = Unwrap(input);
        return candidate as GenerationResult
               ?? throw new InvalidOperationException("Normalized entity export expects a GenerationResult input.");
    }

    private static object? Unwrap(object? input)
    {
        if (input is null)
        {
            return null;
        }

        if (input.GetType().FullName == "System.Management.Automation.PSObject")
        {
            return input.GetType().GetProperty("BaseObject")?.GetValue(input);
        }

        return input;
    }

    private static object? ProjectPassword(string? value, CredentialExportMode mode)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return mode switch
        {
            CredentialExportMode.IncludeGenerated => value,
            CredentialExportMode.Omit => null,
            _ => $"[MASKED:{value.Length}]"
        };
    }

    private static string FormatStreetAddress(Office office)
        => string.Join(
            " ",
            new[] { office.BuildingNumber, office.StreetName }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim()));

    private static bool IsRelationshipRecord(PluginGeneratedRecord record)
        => record.Properties.ContainsKey("relationship_type")
           && record.Properties.ContainsKey("source_entity_id")
           && record.Properties.ContainsKey("target_entity_id");

    private static string SerializeProperties(IReadOnlyDictionary<string, string?> properties)
        => JsonSerializer.Serialize(
            properties.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase));
}
