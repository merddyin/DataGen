namespace SyntheticEnterprise.Core.Generation.Applications;

using System.Security.Cryptography;
using System.Text;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Core.Abstractions;

public sealed class BasicCloudTenantGenerator : ICloudTenantGenerator
{
    private readonly IIdFactory _idFactory;

    public BasicCloudTenantGenerator(IIdFactory idFactory)
    {
        _idFactory = idFactory;
    }

    public void GenerateTenants(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs)
    {
        var tenantPatterns = ReadCloudTenantPatterns(catalogs);

        foreach (var company in world.Companies)
        {
            var applications = world.Applications
                .Where(application =>
                    application.CompanyId == company.Id
                    && application.HostingModel == "SaaS"
                    && IsTenantEligibleApplication(company, application))
                .ToList();
            if (applications.Count == 0)
            {
                continue;
            }

            var offices = world.Offices.Where(office => office.CompanyId == company.Id).ToList();
            var departments = world.Departments.Where(department => department.CompanyId == company.Id).ToList();
            var employeeCount = world.People.Count(person => person.CompanyId == company.Id);

            foreach (var providerGroup in applications
                         .GroupBy(
                             application => ResolveTenantFamily(application, tenantPatterns, company.Industry, employeeCount).ToKey(),
                             StringComparer.OrdinalIgnoreCase))
            {
                var representative = providerGroup.First();
                var tenantFamily = ResolveTenantFamily(representative, tenantPatterns, company.Industry, employeeCount);
                var adminDepartment = SelectAdminDepartment(departments, representative.Category);
                var tenant = new CloudTenant
                {
                    Id = _idFactory.Next("TEN"),
                    CompanyId = company.Id,
                    Provider = tenantFamily.Provider,
                    TenantType = tenantFamily.TenantType,
                    Name = BuildTenantName(company.Name, tenantFamily.Provider, tenantFamily.TenantType),
                    PrimaryDomain = BuildTenantDomain(company, tenantFamily.Provider, tenantFamily.DomainSuffix),
                    Region = ResolvePrimaryRegion(offices),
                    AuthenticationModel = string.IsNullOrWhiteSpace(tenantFamily.AuthenticationModel)
                        ? (providerGroup.Any(application => application.SsoEnabled) ? "Federated" : "Local")
                        : tenantFamily.AuthenticationModel,
                    Environment = "Production",
                    AdminDepartmentId = adminDepartment?.Id ?? string.Empty
                };

                world.CloudTenants.Add(tenant);
                var identityStore = AddIdentityStore(world, company, tenant);
                AddContainers(world, company, tenant, identityStore, offices);
                BackfillModernPolicyIdentityStoreScope(world, company, tenant, identityStore);
                CreateCloudPolicies(world, company, tenant, identityStore);

                foreach (var application in providerGroup)
                {
                    world.ApplicationTenantLinks.Add(new ApplicationTenantLink
                    {
                        Id = _idFactory.Next("APPTEN"),
                        CompanyId = company.Id,
                        ApplicationId = application.Id,
                        CloudTenantId = tenant.Id,
                        RelationshipType = InferRelationshipType(application, tenantPatterns, company.Industry, employeeCount),
                        IsPrimary = true
                    });
                }
            }
        }
    }

    private void CreateCloudPolicies(SyntheticEnterpriseWorld world, Company company, CloudTenant tenant, IdentityStore? identityStore)
    {
        if (!string.Equals(tenant.Provider, "Microsoft", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var tenantContainer = world.Containers.FirstOrDefault(container =>
            container.CompanyId == company.Id
            && string.Equals(container.ContainerType, "CloudTenant", StringComparison.OrdinalIgnoreCase)
            && string.Equals(container.CloudTenantId, tenant.Id, StringComparison.OrdinalIgnoreCase));
        var administrativeUnits = world.Containers
            .Where(container => container.CompanyId == company.Id
                                && string.Equals(container.ContainerType, "AdministrativeUnit", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(container.CloudTenantId, tenant.Id, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var azureSubscriptions = world.Containers
            .Where(container => container.CompanyId == company.Id
                                && string.Equals(container.ContainerType, "AzureSubscription", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(container.CloudTenantId, tenant.Id, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var azureResourceGroups = world.Containers
            .Where(container => container.CompanyId == company.Id
                                && string.Equals(container.ContainerType, "AzureResourceGroup", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(container.CloudTenantId, tenant.Id, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var allEmployeesGroup = FindGroup(world, company.Id, "GG All Employees", "M365 All Employees", "GG Microsoft 365 Users");
        var guestCollaborationGroup = world.Groups.FirstOrDefault(group =>
            group.CompanyId == company.Id
            && (string.Equals(group.Name, "M365-GuestCollaboration", StringComparison.OrdinalIgnoreCase)
                || string.Equals(group.Name, "M365 Guest Collaboration", StringComparison.OrdinalIgnoreCase)));
        var privilegedAccessGroup = FindGroup(world, company.Id, "UG Privileged Access", "GG Privileged Access");

        var compliancePolicy = EnsurePolicy(
            world,
            company.Id,
            "Windows Device Compliance Baseline",
            "IntuneCompliancePolicy",
            "Intune",
            "EndpointCompliance",
            "Tenant-wide Windows device compliance policy.",
            identityStore?.Id,
            tenant.Id);
        AddPolicySetting(world, company.Id, compliancePolicy.Id, "MinimumOsVersion", "DeviceCompliance", "String", "10.0.22621");
        AddPolicySetting(world, company.Id, compliancePolicy.Id, "RequireBitLocker", "DeviceCompliance", "Boolean", "true");
        AddPolicySetting(world, company.Id, compliancePolicy.Id, "ScreenLockTimeoutMinutes", "DeviceCompliance", "Integer", "10", isConflicting: true, sourceReference: "Overlaps with workstation GPO timeout");
        AddPolicyTarget(world, company.Id, compliancePolicy.Id, "IdentityStore", identityStore?.Id, "Scope", false, 1);
        AddPolicyTarget(world, company.Id, compliancePolicy.Id, "Group", allEmployeesGroup?.Id, "Include", false, 1);
        AddAccessControlEvidence(world, company.Id, allEmployeesGroup?.Id, "Group", "Policy", compliancePolicy.Id, "AssignPolicy", "Allow", false, "Intune");

        var configurationProfile = EnsurePolicy(
            world,
            company.Id,
            "Windows Endpoint Configuration Profile",
            "IntuneConfigurationProfile",
            "Intune",
            "EndpointConfiguration",
            "Tenant-wide workstation configuration baseline.",
            identityStore?.Id,
            tenant.Id);
        AddPolicySetting(world, company.Id, configurationProfile.Id, "DefenderRealtimeMonitoring", "EndpointSecurity", "Boolean", "true");
        AddPolicySetting(world, company.Id, configurationProfile.Id, "UsbStorageAccess", "DeviceControl", "String", "BlockWrite");
        AddPolicySetting(world, company.Id, configurationProfile.Id, "ScreenLockTimeoutMinutes", "EndpointConfiguration", "Integer", "10", isConflicting: true, sourceReference: "Overlaps with workstation GPO timeout");
        AddPolicyTarget(world, company.Id, configurationProfile.Id, "IdentityStore", identityStore?.Id, "Scope", false, 1);
        foreach (var administrativeUnit in administrativeUnits)
        {
            AddPolicyTarget(world, company.Id, configurationProfile.Id, "Container", administrativeUnit.Id, "AdministrativeUnitScope", false, 2, true);
        }
        AddPolicyTarget(world, company.Id, configurationProfile.Id, "Group", allEmployeesGroup?.Id, "Include", false, 1);
        AddAccessControlEvidence(world, company.Id, privilegedAccessGroup?.Id, "Group", "Container", tenantContainer?.Id, "DeviceConfigurationAdministration", "Allow", false, "Intune");
        AddAccessControlEvidence(world, company.Id, privilegedAccessGroup?.Id, "Group", "Container", tenantContainer?.Id, "ApplicationAdministration", "Allow", false, "EntraID");
        foreach (var administrativeUnit in administrativeUnits)
        {
            AddAccessControlEvidence(world, company.Id, privilegedAccessGroup?.Id, "Group", "Container", administrativeUnit.Id, "AdministrativeUnitManagement", "Allow", false, "EntraID");
            AddAccessControlEvidence(world, company.Id, privilegedAccessGroup?.Id, "Group", "Container", administrativeUnit.Id, "HelpdeskAdministration", "Allow", false, "EntraID");
        }
        foreach (var subscription in azureSubscriptions)
        {
            AddAccessControlEvidence(world, company.Id, privilegedAccessGroup?.Id, "Group", "Container", subscription.Id, "RoleAssignmentWrite", "Allow", false, "AzureRBAC");
        }
        foreach (var resourceGroup in azureResourceGroups)
        {
            AddAccessControlEvidence(world, company.Id, privilegedAccessGroup?.Id, "Group", "Container", resourceGroup.Id, "ResourceGroupOwner", "Allow", false, "AzureRBAC");
            AddAccessControlEvidence(world, company.Id, allEmployeesGroup?.Id, "Group", "Container", resourceGroup.Id, "ResourceGroupRead", "Allow", true, "AzureRBAC", azureSubscriptions.FirstOrDefault()?.Id, "Inherited broad reader assignment from subscription scope");
        }

        var primarySubscription = azureSubscriptions.FirstOrDefault();
        if (primarySubscription is not null)
        {
            var landingZonePolicy = EnsurePolicy(
                world,
                company.Id,
                "Azure Landing Zone Guardrails",
                "AzurePolicyDefinition",
                "Azure",
                "CloudGovernance",
                "Subscription-level Azure Policy assignment for deployment guardrails.",
                identityStore?.Id,
                tenant.Id);
            AddPolicySetting(world, company.Id, landingZonePolicy.Id, "DenyPublicIp", "AzurePolicyRule", "Boolean", "true");
            AddPolicySetting(world, company.Id, landingZonePolicy.Id, "AllowedLocations", "AzurePolicyRule", "String", "centralus,eastus2");
            AddPolicySetting(world, company.Id, landingZonePolicy.Id, "LegacyMonitoringAgentAllowed", "AzurePolicyRule", "Boolean", "true", isLegacy: true, sourceReference: "Temporary exception retained for migration-era monitoring");
            AddPolicyTarget(world, company.Id, landingZonePolicy.Id, "Container", primarySubscription.Id, "Scope", true, 1, true);
            AddAccessControlEvidence(world, company.Id, privilegedAccessGroup?.Id, "Group", "Policy", landingZonePolicy.Id, "PolicyAssignmentWrite", "Allow", false, "AzurePolicy");

            foreach (var resourceGroup in azureResourceGroups.Take(1))
            {
                AddPolicyTarget(world, company.Id, landingZonePolicy.Id, "Container", resourceGroup.Id, "Exemption", false, 1, true, "ExemptionCategory", "LegacyMonitoringAgent");
                AddAccessControlEvidence(world, company.Id, privilegedAccessGroup?.Id, "Group", "Container", resourceGroup.Id, "PolicyExemptionWrite", "Allow", false, "AzurePolicy", notes: "Scoped exemption for migration-era monitoring dependency");
            }
        }

        foreach (var resourceGroup in azureResourceGroups)
        {
            var taggingPolicy = EnsurePolicy(
                world,
                company.Id,
                $"Tagging Compliance - {resourceGroup.Name}",
                "AzurePolicyInitiative",
                "Azure",
                "CloudGovernance",
                "Resource-group Azure Policy initiative for mandatory tagging and diagnostics.",
                identityStore?.Id,
                tenant.Id);
            AddPolicySetting(world, company.Id, taggingPolicy.Id, "RequireCostCenterTag", "AzurePolicyRule", "Boolean", "true");
            AddPolicySetting(world, company.Id, taggingPolicy.Id, "RequireDataClassificationTag", "AzurePolicyRule", "Boolean", "true");
            AddPolicySetting(world, company.Id, taggingPolicy.Id, "DeployDiagnosticSettings", "AzurePolicyRule", "Boolean", "true", isConflicting: true, sourceReference: "Can overlap with manual platform diagnostics onboarding");
            AddPolicyTarget(world, company.Id, taggingPolicy.Id, "Container", resourceGroup.Id, "Scope", false, 1, true);
        }

        var conditionalAccess = EnsurePolicy(
            world,
            company.Id,
            "Require MFA For Administrative Portals",
            "ConditionalAccessPolicy",
            "EntraID",
            "IdentityProtection",
            "Administrative portal sign-in controls for privileged access.",
            identityStore?.Id,
            tenant.Id);
        AddPolicySetting(world, company.Id, conditionalAccess.Id, "RequireMfa", "ConditionalAccess", "Boolean", "true");
        AddPolicySetting(world, company.Id, conditionalAccess.Id, "RequireCompliantDevice", "ConditionalAccess", "Boolean", "true");
        AddPolicySetting(world, company.Id, conditionalAccess.Id, "LegacyAuthAllowed", "ConditionalAccess", "Boolean", "false", isLegacy: true);
        AddPolicyTarget(world, company.Id, conditionalAccess.Id, "IdentityStore", identityStore?.Id, "Scope", false, 1);
        AddPolicyTarget(world, company.Id, conditionalAccess.Id, "Group", privilegedAccessGroup?.Id, "Include", false, 1);
        AddAccessControlEvidence(world, company.Id, privilegedAccessGroup?.Id, "Group", "Policy", conditionalAccess.Id, "ConditionalAccessAdministration", "Allow", false, "EntraID");
        AddAccessControlEvidence(world, company.Id, privilegedAccessGroup?.Id, "Group", "IdentityStore", identityStore?.Id, "PrivilegedRoleAdministration", "Allow", false, "EntraID");
        AddAccessControlEvidence(world, company.Id, allEmployeesGroup?.Id, "Group", "IdentityStore", identityStore?.Id, "DirectoryReaders", "Allow", false, "EntraID", notes: "Broad read visibility through legacy directory readers assignment");

        var guestAccess = EnsurePolicy(
            world,
            company.Id,
            "Guest Collaboration Access Policy",
            "ConditionalAccessPolicy",
            "EntraID",
            "ExternalCollaboration",
            "Guest collaboration access controls for Microsoft 365 workloads.",
            identityStore?.Id,
            tenant.Id);
        AddPolicySetting(world, company.Id, guestAccess.Id, "RequireTermsOfUse", "ConditionalAccess", "Boolean", "true");
        AddPolicySetting(world, company.Id, guestAccess.Id, "SessionControls", "ConditionalAccess", "String", "Limited");
        AddPolicySetting(world, company.Id, guestAccess.Id, "SharePointExternalSharingMode", "Collaboration", "String", "ExistingGuestsOnly", isLegacy: true);
        AddPolicyTarget(world, company.Id, guestAccess.Id, "IdentityStore", identityStore?.Id, "Scope", false, 1);
        AddPolicyTarget(world, company.Id, guestAccess.Id, "Group", guestCollaborationGroup?.Id, "Include", false, 1);
        AddAccessControlEvidence(world, company.Id, guestCollaborationGroup?.Id, "Group", "Policy", guestAccess.Id, "ApplyPolicy", "Allow", false, "EntraID");
    }

    private void BackfillModernPolicyIdentityStoreScope(
        SyntheticEnterpriseWorld world,
        Company company,
        CloudTenant tenant,
        IdentityStore? identityStore)
    {
        if (identityStore is null)
        {
            return;
        }

        var matchingPolicies = world.Policies
            .Where(policy =>
                string.Equals(policy.CompanyId, company.Id, StringComparison.OrdinalIgnoreCase)
                && (string.Equals(policy.Platform, "Intune", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(policy.PolicyType, "ConditionalAccessPolicy", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var policy in matchingPolicies)
        {
            if (string.IsNullOrWhiteSpace(policy.IdentityStoreId) || string.IsNullOrWhiteSpace(policy.CloudTenantId))
            {
                var index = world.Policies.FindIndex(existing => string.Equals(existing.Id, policy.Id, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    world.Policies[index] = policy with
                    {
                        IdentityStoreId = string.IsNullOrWhiteSpace(policy.IdentityStoreId) ? identityStore.Id : policy.IdentityStoreId,
                        CloudTenantId = string.IsNullOrWhiteSpace(policy.CloudTenantId) ? tenant.Id : policy.CloudTenantId
                    };
                }
            }

            var hasIdentityStoreScope = world.PolicyTargetLinks.Any(link =>
                string.Equals(link.PolicyId, policy.Id, StringComparison.OrdinalIgnoreCase)
                && string.Equals(link.TargetType, "IdentityStore", StringComparison.OrdinalIgnoreCase)
                && string.Equals(link.AssignmentMode, "Scope", StringComparison.OrdinalIgnoreCase));

            if (!hasIdentityStoreScope)
            {
                AddPolicyTarget(world, company.Id, policy.Id, "IdentityStore", identityStore.Id, "Scope", false, 1);
            }
        }
    }

    private IdentityStore? AddIdentityStore(SyntheticEnterpriseWorld world, Company company, CloudTenant tenant)
    {
        var storeType = ResolveIdentityStoreType(tenant.Provider);
        if (string.IsNullOrWhiteSpace(storeType))
        {
            return null;
        }

        var existing = world.IdentityStores.FirstOrDefault(store =>
            string.Equals(store.CompanyId, company.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(store.StoreType, storeType, StringComparison.OrdinalIgnoreCase)
            && string.Equals(store.PrimaryDomain, tenant.PrimaryDomain, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        var identityStore = new IdentityStore
        {
            Id = _idFactory.Next("IDS"),
            CompanyId = company.Id,
            Name = ResolveIdentityStoreName(tenant.PrimaryDomain, tenant.Provider, storeType),
            StoreType = storeType,
            Provider = tenant.Provider,
            PrimaryDomain = tenant.PrimaryDomain,
            DirectoryMode = "CloudDirectory",
            AuthenticationModel = string.IsNullOrWhiteSpace(tenant.AuthenticationModel) ? "CloudManaged" : tenant.AuthenticationModel,
            Environment = tenant.Environment,
            IsPrimary = true,
            CloudTenantId = tenant.Id
        };

        world.IdentityStores.Add(identityStore);
        return identityStore;
    }

    private void AddContainers(
        SyntheticEnterpriseWorld world,
        Company company,
        CloudTenant tenant,
        IdentityStore? identityStore,
        IReadOnlyList<Office> offices)
    {
        if (!world.Containers.Any(container =>
                string.Equals(container.CompanyId, company.Id, StringComparison.OrdinalIgnoreCase)
                && string.Equals(container.ContainerType, "CloudTenant", StringComparison.OrdinalIgnoreCase)
                && string.Equals(container.CloudTenantId, tenant.Id, StringComparison.OrdinalIgnoreCase)))
        {
            world.Containers.Add(new EnvironmentContainer
            {
                Id = _idFactory.Next("CNT"),
                CompanyId = company.Id,
                Name = tenant.Name,
                ContainerType = "CloudTenant",
                Platform = tenant.Provider,
                ParentContainerId = null,
                ContainerPath = tenant.PrimaryDomain,
                Purpose = $"{tenant.Provider} tenant root",
                Environment = tenant.Environment,
                IdentityStoreId = identityStore?.Id,
                CloudTenantId = tenant.Id,
                SourceEntityType = nameof(CloudTenant),
                SourceEntityId = tenant.Id
            });
        }

        if (!string.Equals(tenant.Provider, "Microsoft", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var tenantContainerId = world.Containers
            .Where(container => string.Equals(container.CloudTenantId, tenant.Id, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(container.ContainerType, "CloudTenant", StringComparison.OrdinalIgnoreCase))
            .Select(container => container.Id)
            .First();

        foreach (var office in offices
                     .Where(office => office.CompanyId == company.Id)
                     .Take(3))
        {
            if (world.Containers.Any(container =>
                    string.Equals(container.ContainerType, "AdministrativeUnit", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(container.CloudTenantId, tenant.Id, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(container.SourceEntityId, office.Id, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            world.Containers.Add(new EnvironmentContainer
            {
                Id = _idFactory.Next("CNT"),
                CompanyId = company.Id,
                Name = $"{office.Name} Administrative Unit",
                ContainerType = "AdministrativeUnit",
                Platform = "EntraID",
                ParentContainerId = tenantContainerId,
                ContainerPath = $"{tenant.PrimaryDomain}/administrativeUnits/{Slug(office.Name)}",
                Purpose = "Entra administrative unit",
                Environment = tenant.Environment,
                IdentityStoreId = identityStore?.Id,
                CloudTenantId = tenant.Id,
                SourceEntityType = nameof(Office),
                SourceEntityId = office.Id
            });
        }

        if (!world.Containers.Any(container =>
                string.Equals(container.ContainerType, "AzureSubscription", StringComparison.OrdinalIgnoreCase)
                && string.Equals(container.CloudTenantId, tenant.Id, StringComparison.OrdinalIgnoreCase)))
        {
            var subscriptionId = _idFactory.Next("CNT");
            world.Containers.Add(new EnvironmentContainer
            {
                Id = subscriptionId,
                CompanyId = company.Id,
                Name = $"{company.Name} Production Subscription",
                ContainerType = "AzureSubscription",
                Platform = "Azure",
                ParentContainerId = tenantContainerId,
                ContainerPath = $"{tenant.PrimaryDomain}/subscriptions/{Slug(company.Name)}-prod",
                Purpose = "Primary Azure production subscription",
                Environment = tenant.Environment,
                IdentityStoreId = identityStore?.Id,
                CloudTenantId = tenant.Id,
                SourceEntityType = nameof(CloudTenant),
                SourceEntityId = tenant.Id
            });

            foreach (var office in offices.Where(office => office.CompanyId == company.Id).Take(2))
            {
                world.Containers.Add(new EnvironmentContainer
                {
                    Id = _idFactory.Next("CNT"),
                    CompanyId = company.Id,
                    Name = $"{office.Name} Resource Group",
                    ContainerType = "AzureResourceGroup",
                    Platform = "Azure",
                    ParentContainerId = subscriptionId,
                    ContainerPath = $"{tenant.PrimaryDomain}/subscriptions/{Slug(company.Name)}-prod/resourceGroups/{Slug(office.Name)}-rg",
                    Purpose = "Azure workload resource group",
                    Environment = tenant.Environment,
                    IdentityStoreId = identityStore?.Id,
                    CloudTenantId = tenant.Id,
                    SourceEntityType = nameof(Office),
                    SourceEntityId = office.Id
                });
            }
        }
    }

    private static TenantFamily ResolveTenantFamily(
        ApplicationRecord application,
        IReadOnlyList<CloudTenantPattern> patterns,
        string companyIndustry,
        int employeeCount)
    {
        var pattern = SelectCloudTenantPattern(application, patterns, companyIndustry, employeeCount);
        if (pattern is not null)
        {
            return new TenantFamily(
                FirstNonEmpty(pattern.Provider, application.Vendor, "SaaS"),
                FirstNonEmpty(pattern.TenantType, "SaaS"),
                FirstNonEmpty(pattern.DomainSuffix, "apps.test"),
                pattern.AuthenticationModel);
        }

        var vendor = application.Vendor;
        var capability = application.BusinessCapability;
        var name = application.Name;

        if (vendor.Contains("Microsoft", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Teams", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Power BI", StringComparison.OrdinalIgnoreCase)
            || name.Contains("OneDrive", StringComparison.OrdinalIgnoreCase))
        {
            return new TenantFamily("Microsoft", "ProductivitySuite", "tenant.onmicrosoft.com", string.Empty);
        }

        if (vendor.Contains("Salesforce", StringComparison.OrdinalIgnoreCase)
            || capability.Contains("Customer Relationship", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Tableau", StringComparison.OrdinalIgnoreCase))
        {
            return new TenantFamily("Salesforce", "BusinessApps", "my.salesforce.com", string.Empty);
        }

        if (vendor.Contains("Atlassian", StringComparison.OrdinalIgnoreCase))
        {
            return new TenantFamily("Atlassian", "KnowledgeAndDelivery", "atlassian.net", string.Empty);
        }

        if (vendor.Contains("ServiceNow", StringComparison.OrdinalIgnoreCase))
        {
            return new TenantFamily("ServiceNow", "ITServiceManagement", "service-now.com", string.Empty);
        }

        if (vendor.Contains("Workday", StringComparison.OrdinalIgnoreCase))
        {
            return new TenantFamily("Workday", "HumanCapitalManagement", "workday.com", string.Empty);
        }

        if (vendor.Contains("Okta", StringComparison.OrdinalIgnoreCase))
        {
            return new TenantFamily("Okta", "Identity", "okta.com", string.Empty);
        }

        if (vendor.Contains("Adobe", StringComparison.OrdinalIgnoreCase))
        {
            return new TenantFamily("Adobe", "DigitalExperience", "adobeaemcloud.com", string.Empty);
        }

        if (vendor.Contains("Zoom", StringComparison.OrdinalIgnoreCase))
        {
            return new TenantFamily("Zoom", "Collaboration", "zoom.us", string.Empty);
        }

        if (vendor.Contains("Slack", StringComparison.OrdinalIgnoreCase))
        {
            return new TenantFamily("Slack", "Collaboration", "slack.com", string.Empty);
        }

        if (vendor.Contains("SAP", StringComparison.OrdinalIgnoreCase))
        {
            return new TenantFamily("SAP", "BusinessApps", "saps4hana.cloud", string.Empty);
        }

        return new TenantFamily(string.IsNullOrWhiteSpace(vendor) ? "SaaS" : vendor, "SaaS", "apps.test", string.Empty);
    }

    private PolicyRecord EnsurePolicy(
        SyntheticEnterpriseWorld world,
        string companyId,
        string name,
        string policyType,
        string platform,
        string category,
        string description,
        string? identityStoreId,
        string? cloudTenantId)
    {
        var existing = world.Policies.FirstOrDefault(policy =>
            policy.CompanyId == companyId
            && string.Equals(policy.Name, name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(policy.PolicyType, policyType, StringComparison.OrdinalIgnoreCase)
            && string.Equals(policy.Platform, platform, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        var policy = new PolicyRecord
        {
            Id = _idFactory.Next("POL"),
            CompanyId = companyId,
            PolicyGuid = CreateStableGuid(companyId, name, policyType, platform, category),
            Name = name,
            PolicyType = policyType,
            Platform = platform,
            Category = category,
            Environment = "Production",
            Status = "Enabled",
            Description = description,
            IdentityStoreId = identityStoreId,
            CloudTenantId = cloudTenantId
        };
        world.Policies.Add(policy);
        return policy;
    }

    private void AddPolicySetting(
        SyntheticEnterpriseWorld world,
        string companyId,
        string policyId,
        string settingName,
        string settingCategory,
        string valueType,
        string configuredValue,
        bool isLegacy = false,
        bool isConflicting = false,
        string? sourceReference = null)
    {
        if (world.PolicySettings.Any(setting =>
                setting.CompanyId == companyId
                && string.Equals(setting.PolicyId, policyId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(setting.SettingName, settingName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var policy = world.Policies.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, policyId, StringComparison.OrdinalIgnoreCase));
        var metadata = ResolvePolicySettingMetadata(policy, settingName, settingCategory);

        world.PolicySettings.Add(new PolicySettingRecord
        {
            Id = _idFactory.Next("PST"),
            CompanyId = companyId,
            PolicyId = policyId,
            SettingName = settingName,
            SettingCategory = settingCategory,
            PolicyPath = metadata.PolicyPath,
            RegistryPath = metadata.RegistryPath,
            ValueType = valueType,
            ConfiguredValue = configuredValue,
            IsLegacy = isLegacy,
            IsConflicting = isConflicting,
            SourceReference = sourceReference
        });
    }

    private static (string PolicyPath, string? RegistryPath) ResolvePolicySettingMetadata(
        PolicyRecord? policy,
        string settingName,
        string settingCategory)
    {
        if (policy is null)
        {
            return ($@"HKLM\Software\Policies\SyntheticEnterprise\CloudPolicies\Unknown\{settingCategory}\{settingName}", null);
        }

        if (string.Equals(policy.Platform, "Intune", StringComparison.OrdinalIgnoreCase)
            || string.Equals(policy.PolicyType, "IntuneConfigurationProfile", StringComparison.OrdinalIgnoreCase)
            || string.Equals(policy.PolicyType, "IntuneCompliancePolicy", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(settingCategory, "Compliance", StringComparison.OrdinalIgnoreCase)
                || string.Equals(policy.PolicyType, "IntuneCompliancePolicy", StringComparison.OrdinalIgnoreCase))
            {
                return ($"Devices\\Windows\\Compliance policies\\{policy.Name}", $"./Device/Vendor/MSFT/Policy/Config/Compliance/{settingName}");
            }

            return ($"Devices\\Windows\\Configuration profiles\\{policy.Name}", $"./Device/Vendor/MSFT/Policy/Config/{settingCategory}/{settingName}");
        }

        if (string.Equals(policy.Platform, "Azure", StringComparison.OrdinalIgnoreCase))
        {
            return ($"Azure Policy\\Assignments\\{policy.Name}", $"/providers/Microsoft.Authorization/policyAssignments/{settingName}");
        }

        return ($@"HKLM\Software\Policies\SyntheticEnterprise\CloudPolicies\{policy.Name.Replace(' ', '_')}\{settingCategory}\{settingName}", null);
    }

    private static string CreateStableGuid(params string[] components)
    {
        var seed = string.Join("|", components.Where(component => !string.IsNullOrWhiteSpace(component)));
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(seed));
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes[..16]).ToString();
    }

    private void AddPolicyTarget(
        SyntheticEnterpriseWorld world,
        string companyId,
        string policyId,
        string targetType,
        string? targetId,
        string assignmentMode,
        bool isEnforced,
        int linkOrder,
        bool linkEnabled = true,
        string? filterType = null,
        string? filterValue = null)
    {
        if (string.IsNullOrWhiteSpace(targetId))
        {
            return;
        }

        if (world.PolicyTargetLinks.Any(link =>
                link.CompanyId == companyId
                && string.Equals(link.PolicyId, policyId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(link.TargetType, targetType, StringComparison.OrdinalIgnoreCase)
                && string.Equals(link.TargetId, targetId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(link.AssignmentMode, assignmentMode, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        world.PolicyTargetLinks.Add(new PolicyTargetLink
        {
            Id = _idFactory.Next("PTL"),
            CompanyId = companyId,
            PolicyId = policyId,
            TargetType = targetType,
            TargetId = targetId,
            AssignmentMode = assignmentMode,
            LinkEnabled = linkEnabled,
            IsEnforced = isEnforced,
            LinkOrder = linkOrder,
            FilterType = filterType,
            FilterValue = filterValue
        });
    }

    private void AddAccessControlEvidence(
        SyntheticEnterpriseWorld world,
        string companyId,
        string? principalObjectId,
        string principalType,
        string targetType,
        string? targetId,
        string rightName,
        string accessType,
        bool isInherited,
        string sourceSystem,
        string? inheritanceSourceId = null,
        string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(principalObjectId) || string.IsNullOrWhiteSpace(targetId))
        {
            return;
        }

        if (world.AccessControlEvidence.Any(evidence =>
                evidence.CompanyId == companyId
                && string.Equals(evidence.PrincipalObjectId, principalObjectId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(evidence.TargetType, targetType, StringComparison.OrdinalIgnoreCase)
                && string.Equals(evidence.TargetId, targetId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(evidence.RightName, rightName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(evidence.AccessType, accessType, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        world.AccessControlEvidence.Add(new AccessControlEvidenceRecord
        {
            Id = _idFactory.Next("ACE"),
            CompanyId = companyId,
            PrincipalObjectId = principalObjectId,
            PrincipalType = principalType,
            TargetType = targetType,
            TargetId = targetId,
            RightName = rightName,
            AccessType = accessType,
            IsInherited = isInherited,
            IsDefaultEntry = false,
            SourceSystem = sourceSystem,
            InheritanceSourceId = inheritanceSourceId,
            Notes = notes
        });
    }

    private static Department? SelectAdminDepartment(IReadOnlyList<Department> departments, string category)
    {
        static bool Matches(Department department, params string[] names)
            => names.Any(name => department.Name.Contains(name, StringComparison.OrdinalIgnoreCase));

        return category switch
        {
            "HR" => departments.FirstOrDefault(department => Matches(department, "Human Resources", "Information Technology")),
            "Sales" or "Marketing" => departments.FirstOrDefault(department => Matches(department, "Sales", "Marketing", "Information Technology")),
            "Security" => departments.FirstOrDefault(department => Matches(department, "Security", "Information Technology")),
            _ => departments.FirstOrDefault(department => Matches(department, "Information Technology", "Operations", "Engineering"))
        };
    }

    private static DirectoryGroup? FindGroup(SyntheticEnterpriseWorld world, string companyId, params string[] names)
        => world.Groups.FirstOrDefault(group =>
            group.CompanyId == companyId
            && names.Any(name => string.Equals(group.Name, name, StringComparison.OrdinalIgnoreCase)));

    private static string BuildTenantName(string companyName, string provider, string tenantType)
        => $"{companyName} {provider} {tenantType}".Trim();

    private static bool IsTenantEligibleApplication(Company company, ApplicationRecord application)
    {
        if (!string.Equals(application.HostingModel, "SaaS", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (LooksLikeEndpointSoftware(application))
        {
            return false;
        }

        if (string.Equals(application.Vendor, company.Name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static bool LooksLikeEndpointSoftware(ApplicationRecord application)
    {
        var name = application.Name ?? string.Empty;
        var category = application.Category ?? string.Empty;

        if (category is "Browser" or "Utility" or "Backup")
        {
            return true;
        }

        return name.Contains("Agent", StringComparison.OrdinalIgnoreCase)
               || name.Contains("Plugin", StringComparison.OrdinalIgnoreCase)
               || name.Contains("Sync Client", StringComparison.OrdinalIgnoreCase)
               || name.Contains("VDI", StringComparison.OrdinalIgnoreCase)
               || name.Contains("Browser", StringComparison.OrdinalIgnoreCase)
               || name.Contains("Backup", StringComparison.OrdinalIgnoreCase)
               || name.Contains("Notepad++", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveIdentityStoreType(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return string.Empty;
        }

        return provider.Trim() switch
        {
            var value when value.Equals("Microsoft", StringComparison.OrdinalIgnoreCase) => "EntraTenant",
            var value when value.Equals("Okta", StringComparison.OrdinalIgnoreCase) => "Okta",
            var value when value.Equals("Auth0", StringComparison.OrdinalIgnoreCase) => "Auth0",
            var value when value.Equals("Google", StringComparison.OrdinalIgnoreCase) || value.Equals("Google Workspace", StringComparison.OrdinalIgnoreCase) => "GoogleWorkspace",
            var value when value.Equals("OneLogin", StringComparison.OrdinalIgnoreCase) => "OneLogin",
            var value when value.Equals("Ping", StringComparison.OrdinalIgnoreCase) || value.Equals("Ping Identity", StringComparison.OrdinalIgnoreCase) => "PingIdentity",
            var value when value.Equals("JumpCloud", StringComparison.OrdinalIgnoreCase) => "JumpCloud",
            _ => string.Empty
        };
    }

    private static string ResolveIdentityStoreName(string primaryDomain, string? provider, string storeType)
        => storeType switch
        {
            "EntraTenant" or "GoogleWorkspace" or "Okta" or "Auth0" or "OneLogin" or "PingIdentity" or "JumpCloud"
                => primaryDomain,
            _ => string.IsNullOrWhiteSpace(primaryDomain) ? provider?.Trim() ?? storeType : primaryDomain
        };

    private static string BuildTenantDomain(Company company, string provider, string domainSuffix)
    {
        var companySlug = !string.IsNullOrWhiteSpace(company.PrimaryDomain)
            ? company.PrimaryDomain.Split('.', StringSplitOptions.RemoveEmptyEntries)[0]
            : Slug(company.Name);
        var suffix = domainSuffix.TrimStart('.');

        if (string.Equals(provider, "Microsoft", StringComparison.OrdinalIgnoreCase)
            && string.Equals(suffix, "tenant.onmicrosoft.com", StringComparison.OrdinalIgnoreCase))
        {
            return $"{companySlug}.onmicrosoft.com";
        }

        if (string.Equals(provider, "Okta", StringComparison.OrdinalIgnoreCase)
            && string.Equals(suffix, "okta.com", StringComparison.OrdinalIgnoreCase))
        {
            return $"{companySlug}.okta.com";
        }

        if (string.Equals(provider, "Auth0", StringComparison.OrdinalIgnoreCase)
            && string.Equals(suffix, "auth0.com", StringComparison.OrdinalIgnoreCase))
        {
            return $"{companySlug}.auth0.com";
        }

        var providerSlug = Slug(provider);

        return suffix.Contains('/')
            ? $"{companySlug}-{providerSlug}.{suffix.Replace("/", ".")}"
            : $"{companySlug}-{providerSlug}.{suffix}";
    }

    private static string ResolvePrimaryRegion(IReadOnlyList<Office> offices)
        => offices.GroupBy(office => office.Region, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)
            .FirstOrDefault(region => !string.IsNullOrWhiteSpace(region))
           ?? offices.Select(office => office.Country).FirstOrDefault(country => !string.IsNullOrWhiteSpace(country))
           ?? "Global";

    private static string InferRelationshipType(
        ApplicationRecord application,
        IReadOnlyList<CloudTenantPattern> patterns,
        string companyIndustry,
        int employeeCount)
    {
        var pattern = SelectCloudTenantPattern(application, patterns, companyIndustry, employeeCount);
        if (!string.IsNullOrWhiteSpace(pattern?.RelationshipType))
        {
            return pattern.RelationshipType;
        }

        return application.Category switch
        {
            "Security" => "IdentityControlPlane",
            "Collaboration" or "Productivity" => "PrimaryWorkspace",
            "Sales" or "Marketing" => "BusinessWorkspace",
            _ => "PrimaryTenant"
        };
    }

    private static CloudTenantPattern? SelectCloudTenantPattern(
        ApplicationRecord application,
        IReadOnlyList<CloudTenantPattern> patterns,
        string companyIndustry,
        int employeeCount)
    {
        if (patterns.Count == 0)
        {
            return null;
        }

        return patterns
            .Where(pattern => MatchesCloudTenantPattern(pattern, application, companyIndustry, employeeCount))
            .OrderByDescending(GetPatternSpecificity)
            .FirstOrDefault();
    }

    private static bool MatchesCloudTenantPattern(
        CloudTenantPattern pattern,
        ApplicationRecord application,
        string companyIndustry,
        int employeeCount)
    {
        if (pattern.MinimumEmployees > employeeCount)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchVendor)
            && !application.Vendor.Contains(pattern.MatchVendor, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchNameContains)
            && !application.Name.Contains(pattern.MatchNameContains, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (pattern.MatchCategories.Count > 0
            && !pattern.MatchCategories.Contains(application.Category, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (pattern.IndustryTags.Count == 0 || pattern.IndustryTags.Contains("All", StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        var industryTokens = BuildIndustryTokens(companyIndustry);
        return pattern.IndustryTags.Any(tag => industryTokens.Contains(tag));
    }

    private static int GetPatternSpecificity(CloudTenantPattern pattern)
    {
        var score = pattern.MinimumEmployees > 1 ? 1 : 0;

        if (!string.IsNullOrWhiteSpace(pattern.MatchVendor))
        {
            score += 8;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchNameContains))
        {
            score += 6;
        }

        if (pattern.MatchCategories.Count > 0)
        {
            score += 4;
        }

        if (pattern.IndustryTags.Count > 0 && !pattern.IndustryTags.Contains("All", StringComparer.OrdinalIgnoreCase))
        {
            score += 2;
        }

        return score;
    }

    private static List<CloudTenantPattern> ReadCloudTenantPatterns(CatalogSet catalogs)
    {
        if (!catalogs.CsvCatalogs.TryGetValue("cloud_tenant_patterns", out var rows))
        {
            return new List<CloudTenantPattern>();
        }

        return rows.Select(row => new CloudTenantPattern(
                Read(row, "MatchVendor"),
                Read(row, "MatchNameContains"),
                SplitPipe(Read(row, "MatchCategory")),
                SplitPipe(Read(row, "IndustryTags")),
                int.TryParse(Read(row, "MinimumEmployees"), out var minimumEmployees) ? minimumEmployees : 0,
                Read(row, "Provider"),
                Read(row, "TenantType"),
                Read(row, "DomainSuffix"),
                Read(row, "AuthenticationModel"),
                Read(row, "RelationshipType")))
            .ToList();
    }

    private static HashSet<string> BuildIndustryTokens(string companyIndustry)
    {
        var tokens = SplitPipe(companyIndustry);
        if (!string.IsNullOrWhiteSpace(companyIndustry))
        {
            tokens.Add(companyIndustry.Trim());

            foreach (var token in companyIndustry.Split(new[] { '/', ',', '&', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                tokens.Add(token.Trim());
            }
        }

        return tokens;
    }

    private static HashSet<string> SplitPipe(string value)
        => value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string Read(IReadOnlyDictionary<string, string?> row, string key)
        => row.TryGetValue(key, out var value) ? value?.Trim() ?? string.Empty : string.Empty;

    private static string FirstNonEmpty(params string[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static string Slug(string value)
        => new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private sealed record TenantFamily(string Provider, string TenantType, string DomainSuffix, string AuthenticationModel)
    {
        public string ToKey() => $"{Provider}|{TenantType}|{DomainSuffix}|{AuthenticationModel}";
    }

    private sealed record CloudTenantPattern(
        string MatchVendor,
        string MatchNameContains,
        HashSet<string> MatchCategories,
        HashSet<string> IndustryTags,
        int MinimumEmployees,
        string Provider,
        string TenantType,
        string DomainSuffix,
        string AuthenticationModel,
        string RelationshipType);
}
