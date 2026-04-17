namespace SyntheticEnterprise.Core.Generation.Identity;

using System.Security.Cryptography;
using System.Text;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Core.Abstractions;

public sealed class BasicIdentityGenerator : IIdentityGenerator
{
    private static readonly char[] LowercaseChars = "abcdefghijkmnopqrstuvwxyz".ToCharArray();
    private static readonly char[] UppercaseChars = "ABCDEFGHJKLMNPQRSTUVWXYZ".ToCharArray();
    private static readonly char[] DigitChars = "23456789".ToCharArray();
    private static readonly char[] SymbolChars = "!@#$%^&*()-_=+[]{}".ToCharArray();
    private static readonly char[] AllPasswordChars = LowercaseChars
        .Concat(UppercaseChars)
        .Concat(DigitChars)
        .Concat(SymbolChars)
        .ToArray();

    private readonly IIdFactory _idFactory;
    private readonly IRandomSource _randomSource;
    private readonly IClock _clock;

    public BasicIdentityGenerator(IIdFactory idFactory, IRandomSource randomSource, IClock clock)
    {
        _idFactory = idFactory;
        _randomSource = randomSource;
        _clock = clock;
    }

    public void GenerateIdentity(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs)
    {
        foreach (var company in world.Companies)
        {
            var companyDefinition = context.Scenario.Companies.FirstOrDefault(c =>
                string.Equals(c.Name, company.Name, StringComparison.OrdinalIgnoreCase));

            if (companyDefinition is null)
            {
                continue;
            }

            var companyPeople = world.People.Where(p => p.CompanyId == company.Id).ToList();
            var companyDepartments = world.Departments.Where(d => d.CompanyId == company.Id).ToList();
            var companyTeams = world.Teams.Where(team => team.CompanyId == company.Id).ToList();
            var rootDomain = BuildRootDomain(company);
            var issuedPasswords = new HashSet<string>(StringComparer.Ordinal);
            var includeAdministrativeTiers = context.Scenario.Identity.IncludeAdministrativeTiers;

            var identityStores = CreateIdentityStores(company, rootDomain, context.Scenario.Identity);
            world.IdentityStores.AddRange(identityStores);

            var ous = CreateOus(company, companyDepartments, rootDomain, includeAdministrativeTiers);
            world.OrganizationalUnits.AddRange(ous);
            world.Containers.AddRange(CreateDirectoryContainers(company, identityStores, ous));

            var peopleAccounts = CreateUserAccounts(company, companyPeople, companyDepartments, ous, issuedPasswords);
            world.Accounts.AddRange(peopleAccounts);

            SetManagerRelationships(world, company, companyPeople, peopleAccounts);

            var serviceAccounts = CreateServiceAccounts(company, companyDefinition, ous, rootDomain, issuedPasswords, includeAdministrativeTiers);
            world.Accounts.AddRange(serviceAccounts);

            var sharedAccounts = CreateSharedMailboxes(company, companyDefinition, ous, rootDomain, issuedPasswords);
            world.Accounts.AddRange(sharedAccounts);

            if (companyDefinition.IncludePrivilegedAccounts)
            {
                var privileged = CreatePrivilegedAccounts(company, companyPeople, ous, rootDomain, issuedPasswords, includeAdministrativeTiers);
                world.Accounts.AddRange(privileged);
            }

            var groups = CreateGroups(company, companyDepartments, ous, includeAdministrativeTiers);
            world.Groups.AddRange(groups);

            var memberships = CreateMemberships(company, companyDepartments, companyPeople, groups, world.Accounts, includeAdministrativeTiers);
            world.GroupMemberships.AddRange(memberships);

            if (context.Scenario.Identity.IncludeExternalWorkforce || context.Scenario.Identity.IncludeB2BGuests)
            {
                var externalOrganizations = EnsureExternalIdentityOrganizations(world, company, companyDepartments, companyDefinition, rootDomain);
                var externalPeople = CreateExternalPeople(
                    company,
                    companyDefinition,
                    context.Scenario.Identity,
                    companyPeople,
                    companyDepartments,
                    companyTeams,
                    world.Offices.Where(office => office.CompanyId == company.Id).ToList(),
                    externalOrganizations,
                    rootDomain);
                if (externalPeople.Count > 0)
                {
                    world.People.AddRange(externalPeople);
                    companyPeople.AddRange(externalPeople);

                    var externalAccounts = CreateExternalAccounts(
                        company,
                        externalPeople,
                        externalOrganizations,
                        world.Accounts.Where(account => account.CompanyId == company.Id).ToList(),
                        ous,
                        issuedPasswords,
                        rootDomain);
                    world.Accounts.AddRange(externalAccounts);

                    var externalGroups = CreateExternalGroups(company, ous);
                    foreach (var group in externalGroups)
                    {
                        if (!world.Groups.Any(existing => existing.CompanyId == group.CompanyId
                                                          && string.Equals(existing.Name, group.Name, StringComparison.OrdinalIgnoreCase)))
                        {
                            world.Groups.Add(group);
                            groups = groups.Append(group).ToList();
                        }
                    }

                    var externalMemberships = CreateExternalMemberships(company, externalPeople, externalAccounts, groups, companyDepartments);
                    world.GroupMemberships.AddRange(externalMemberships);
                    CreateCrossTenantAccessArtifacts(world, company, externalOrganizations, externalAccounts);
                }
            }

            CreateDirectoryPolicies(world, company, includeAdministrativeTiers);
            CreateCrossTenantPolicyObjects(world, company);
        }
    }

    private List<IdentityStore> CreateIdentityStores(Company company, string rootDomain, IdentityProfile identityProfile)
    {
        if (!identityProfile.IncludeHybridDirectory)
        {
            return new List<IdentityStore>();
        }

        return
        [
            new IdentityStore
            {
                Id = _idFactory.Next("IDS"),
                CompanyId = company.Id,
                Name = $"{company.Name} Active Directory",
                StoreType = "ActiveDirectoryDomain",
                Provider = "Microsoft",
                PrimaryDomain = rootDomain,
                NamingContext = BuildNamingContext(rootDomain),
                DirectoryMode = identityProfile.IncludeM365StyleGroups ? "HybridDirectory" : "OnPremDirectory",
                AuthenticationModel = "Kerberos",
                Environment = "Production",
                IsPrimary = true
            }
        ];
    }

    private List<EnvironmentContainer> CreateDirectoryContainers(
        Company company,
        IReadOnlyList<IdentityStore> identityStores,
        IReadOnlyList<DirectoryOrganizationalUnit> ous)
    {
        var primaryDirectoryStore = identityStores.FirstOrDefault(store =>
            string.Equals(store.StoreType, "ActiveDirectoryDomain", StringComparison.OrdinalIgnoreCase));
        if (primaryDirectoryStore is null)
        {
            return new List<EnvironmentContainer>();
        }

        var results = new List<EnvironmentContainer>();
        var domainRootContainer = new EnvironmentContainer
        {
            Id = _idFactory.Next("CNT"),
            CompanyId = company.Id,
            Name = primaryDirectoryStore.PrimaryDomain,
            ContainerType = "DirectoryDomain",
            Platform = "ActiveDirectory",
            ParentContainerId = null,
            ContainerPath = primaryDirectoryStore.NamingContext ?? primaryDirectoryStore.PrimaryDomain,
            Purpose = "Directory naming context",
            Environment = primaryDirectoryStore.Environment,
            BlocksPolicyInheritance = false,
            IdentityStoreId = primaryDirectoryStore.Id,
            SourceEntityType = nameof(IdentityStore),
            SourceEntityId = primaryDirectoryStore.Id
        };
        results.Add(domainRootContainer);

        var containerIdsByOuId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ou in ous)
        {
            var container = new EnvironmentContainer
            {
                Id = _idFactory.Next("CNT"),
                CompanyId = company.Id,
                Name = ou.Name,
                ContainerType = "OrganizationalUnit",
                Platform = "ActiveDirectory",
                ParentContainerId = !string.IsNullOrWhiteSpace(ou.ParentOuId) && containerIdsByOuId.TryGetValue(ou.ParentOuId, out var parentContainerId)
                    ? parentContainerId
                    : domainRootContainer.Id,
                ContainerPath = ou.DistinguishedName,
                Purpose = ou.Purpose,
                Environment = primaryDirectoryStore.Environment,
                BlocksPolicyInheritance = string.Equals(ou.Name, "Admin Accounts", StringComparison.OrdinalIgnoreCase)
                                          || string.Equals(ou.Name, "Privileged Access Workstations", StringComparison.OrdinalIgnoreCase),
                IdentityStoreId = primaryDirectoryStore.Id,
                SourceEntityType = nameof(DirectoryOrganizationalUnit),
                SourceEntityId = ou.Id
            };

            results.Add(container);
            containerIdsByOuId[ou.Id] = container.Id;
        }

        return results;
    }

    private void CreateDirectoryPolicies(SyntheticEnterpriseWorld world, Company company, bool includeAdministrativeTiers)
    {
        var activeDirectoryStore = world.IdentityStores.FirstOrDefault(store =>
            store.CompanyId == company.Id
            && string.Equals(store.StoreType, "ActiveDirectoryDomain", StringComparison.OrdinalIgnoreCase));
        if (activeDirectoryStore is null)
        {
            return;
        }

        var domainContainer = FindContainer(world, company.Id, "DirectoryDomain", activeDirectoryStore.Id);
        var workstationContainer = FindContainer(world, company.Id, "OrganizationalUnit", activeDirectoryStore.Id, "Workstations");
        var serverContainer = FindContainer(world, company.Id, "OrganizationalUnit", activeDirectoryStore.Id, "Servers");
        var pawContainer = FindContainer(world, company.Id, "OrganizationalUnit", activeDirectoryStore.Id, "Privileged Access Workstations");
        var allEmployeesGroup = FindGroup(world.Groups, company.Id, "SG-AllEmployees");
        var guestGroup = FindGroup(world.Groups, company.Id, "SG-B2BGuests");
        var workstationAdmins = FindGroup(world.Groups, company.Id, "SG-Tier1-WorkstationAdmins");
        var serverAdmins = FindGroup(world.Groups, company.Id, "SG-Tier1-ServerAdmins");
        var pawUsers = FindGroup(world.Groups, company.Id, "SG-Tier0-PAW-Users");

        var defaultDomainPolicy = EnsurePolicy(
            world,
            company.Id,
            "Default Domain Policy",
            "GroupPolicyObject",
            "ActiveDirectory",
            "IdentityBaseline",
            "Baseline domain credential and authentication settings.",
            activeDirectoryStore.Id,
            null);
        AddPolicySetting(world, company.Id, defaultDomainPolicy.Id, "MinimumPasswordLength", "PasswordPolicy", "Integer", "14");
        AddPolicySetting(world, company.Id, defaultDomainPolicy.Id, "PasswordHistoryCount", "PasswordPolicy", "Integer", "24");
        AddPolicySetting(world, company.Id, defaultDomainPolicy.Id, "AccountLockoutThreshold", "PasswordPolicy", "Integer", "10");
        AddPolicySetting(world, company.Id, defaultDomainPolicy.Id, "LanManCompatibilityLevel", "LegacyAuthentication", "String", "NTLMv2Only", isLegacy: true, sourceReference: "Commonly retained legacy hardening knob");
        AddPolicyTarget(world, company.Id, defaultDomainPolicy.Id, "Container", domainContainer?.Id, "Linked", true, 1, true);
        AddPolicyTarget(world, company.Id, defaultDomainPolicy.Id, "IdentityStore", activeDirectoryStore.Id, "Scope", false, 1);

        var workstationPolicy = EnsurePolicy(
            world,
            company.Id,
            "Workstation Security Baseline",
            "GroupPolicyObject",
            "ActiveDirectory",
            "EndpointSecurity",
            "GPO-backed workstation hardening baseline.",
            activeDirectoryStore.Id,
            null);
        AddPolicySetting(world, company.Id, workstationPolicy.Id, "WindowsFirewallEnabled", "NetworkSecurity", "Boolean", "true");
        AddPolicySetting(world, company.Id, workstationPolicy.Id, "Smb1Enabled", "LegacyProtocols", "Boolean", "false");
        AddPolicySetting(world, company.Id, workstationPolicy.Id, "ScreenLockTimeoutMinutes", "UserExperience", "Integer", "15", sourceReference: "Overlaps with Intune device controls");
        AddPolicyTarget(world, company.Id, workstationPolicy.Id, "Container", workstationContainer?.Id, "Linked", false, 1, true);
        AddPolicyTarget(world, company.Id, workstationPolicy.Id, "Group", allEmployeesGroup?.Id, "SecurityFilterInclude", false, 1);
        AddPolicyTarget(world, company.Id, workstationPolicy.Id, "Group", guestGroup?.Id, "SecurityFilterExclude", false, 2);
        AddPolicyTarget(world, company.Id, workstationPolicy.Id, "Group", workstationAdmins?.Id, "DelegatedAdministration", false, 1, true, "Permission", "EditSettings");
        AddPolicyTarget(world, company.Id, workstationPolicy.Id, "Container", workstationContainer?.Id, "WmiFilter", false, 1, true, "WmiQuery", "SELECT * FROM Win32_OperatingSystem WHERE ProductType = 1");
        AddAccessControlEvidence(world, company.Id, allEmployeesGroup?.Id, "Group", "Container", workstationContainer?.Id, "ApplyGroupPolicy", "Allow", false, "ActiveDirectory");
        AddAccessControlEvidence(world, company.Id, guestGroup?.Id, "Group", "Container", workstationContainer?.Id, "ApplyGroupPolicy", "Deny", false, "ActiveDirectory", notes: "Guest and external identities explicitly excluded from workstation baseline");
        AddAccessControlEvidence(world, company.Id, workstationAdmins?.Id, "Group", "Policy", workstationPolicy.Id, "EditSettings", "Allow", false, "ActiveDirectory");
        AddAccessControlEvidence(world, company.Id, workstationAdmins?.Id, "Group", "Container", workstationContainer?.Id, "LinkGpo", "Allow", false, "ActiveDirectory");
        AddAccessControlEvidence(world, company.Id, workstationAdmins?.Id, "Group", "Container", workstationContainer?.Id, "CreateComputerObject", "Allow", false, "ActiveDirectory", notes: "Delegated workstation join and OU maintenance");
        AddAccessControlEvidence(world, company.Id, workstationAdmins?.Id, "Group", "Container", workstationContainer?.Id, "DeleteComputerObject", "Allow", false, "ActiveDirectory");

        var serverPolicy = EnsurePolicy(
            world,
            company.Id,
            "Server Security Baseline",
            "GroupPolicyObject",
            "ActiveDirectory",
            "EndpointSecurity",
            "GPO-backed server hardening baseline.",
            activeDirectoryStore.Id,
            null);
        AddPolicySetting(world, company.Id, serverPolicy.Id, "WindowsFirewallEnabled", "NetworkSecurity", "Boolean", "true");
        AddPolicySetting(world, company.Id, serverPolicy.Id, "RemotePowerShellEnabled", "RemoteManagement", "Boolean", "true");
        AddPolicySetting(world, company.Id, serverPolicy.Id, "SmbSigningRequired", "LegacyProtocols", "Boolean", "true");
        AddPolicyTarget(world, company.Id, serverPolicy.Id, "Container", serverContainer?.Id, "Linked", true, 1, true);
        AddPolicyTarget(world, company.Id, serverPolicy.Id, "Group", serverAdmins?.Id, "DelegatedAdministration", false, 1, true, "Permission", "EditSettings");
        AddAccessControlEvidence(world, company.Id, serverAdmins?.Id, "Group", "Policy", serverPolicy.Id, "EditSettings", "Allow", false, "ActiveDirectory");
        AddAccessControlEvidence(world, company.Id, serverAdmins?.Id, "Group", "Container", serverContainer?.Id, "LinkGpo", "Allow", false, "ActiveDirectory");
        AddAccessControlEvidence(world, company.Id, serverAdmins?.Id, "Group", "Container", serverContainer?.Id, "CreateChild", "Allow", false, "ActiveDirectory", notes: "Delegated server OU lifecycle management");
        AddAccessControlEvidence(world, company.Id, serverAdmins?.Id, "Group", "Container", serverContainer?.Id, "DeleteChild", "Allow", false, "ActiveDirectory");

        var legacyBrowserPolicy = EnsurePolicy(
            world,
            company.Id,
            "Legacy Browser Compatibility Staging",
            "GroupPolicyObject",
            "ActiveDirectory",
            "UserExperience",
            "Disabled legacy compatibility GPO retained for staged application support.",
            activeDirectoryStore.Id,
            null);
        AddPolicySetting(world, company.Id, legacyBrowserPolicy.Id, "InternetExplorerModeSiteList", "LegacyCompatibility", "String", "LegacyLineOfBusinessApps", isLegacy: true, sourceReference: "Typical transitional browser-compatibility holdover");
        AddPolicySetting(world, company.Id, legacyBrowserPolicy.Id, "TrustedSitesZoneAssignments", "LegacyCompatibility", "String", "LegacyVendorPortal", isLegacy: true);
        AddPolicyTarget(world, company.Id, legacyBrowserPolicy.Id, "Container", workstationContainer?.Id, "Linked", false, 2, false);
        AddPolicyTarget(world, company.Id, legacyBrowserPolicy.Id, "Group", allEmployeesGroup?.Id, "SecurityFilterInclude", false, 1);
        AddPolicyTarget(world, company.Id, legacyBrowserPolicy.Id, "Container", workstationContainer?.Id, "WmiFilter", false, 2, true, "WmiQuery", "SELECT * FROM Win32_OperatingSystem WHERE ProductType = 1 AND Version LIKE '10.%'");
        AddPolicyTarget(world, company.Id, legacyBrowserPolicy.Id, "Group", workstationAdmins?.Id, "DelegatedAdministration", false, 1, true, "Permission", "EditPermissions");
        AddAccessControlEvidence(world, company.Id, workstationAdmins?.Id, "Group", "Policy", legacyBrowserPolicy.Id, "EditPermissions", "Allow", false, "ActiveDirectory");
        AddAccessControlEvidence(world, company.Id, workstationAdmins?.Id, "Group", "Policy", legacyBrowserPolicy.Id, "ReadPolicy", "Allow", false, "ActiveDirectory");

        if (includeAdministrativeTiers && pawContainer is not null)
        {
            var pawPolicy = EnsurePolicy(
                world,
                company.Id,
                "Privileged Access Workstation Hardening",
                "GroupPolicyObject",
                "ActiveDirectory",
                "PrivilegedAccess",
                "Tiered workstation controls for privileged admin workstations.",
                activeDirectoryStore.Id,
                null);
            AddPolicySetting(world, company.Id, pawPolicy.Id, "InternetAccessAllowed", "NetworkSecurity", "Boolean", "false");
            AddPolicySetting(world, company.Id, pawPolicy.Id, "RemovableMediaAccess", "DeviceControl", "String", "ReadOnly", isLegacy: true, sourceReference: "Common staged compromise between usability and hardening");
            AddPolicySetting(world, company.Id, pawPolicy.Id, "PowerShellScriptExecution", "AdministrativeTooling", "String", "Restricted");
            AddPolicyTarget(world, company.Id, pawPolicy.Id, "Container", pawContainer.Id, "Linked", true, 1, true);
            AddPolicyTarget(world, company.Id, pawPolicy.Id, "Group", pawUsers?.Id, "SecurityFilterInclude", false, 1);
            var tier0Admins = FindGroup(world.Groups, company.Id, "SG-Tier0-IdentityAdmins");
            AddAccessControlEvidence(world, company.Id, pawUsers?.Id, "Group", "Container", pawContainer.Id, "ApplyGroupPolicy", "Allow", false, "ActiveDirectory");
            AddAccessControlEvidence(world, company.Id, tier0Admins?.Id, "Group", "Policy", pawPolicy.Id, "EditSettings", "Allow", false, "ActiveDirectory");
            AddAccessControlEvidence(world, company.Id, tier0Admins?.Id, "Group", "Container", pawContainer.Id, "BlockInheritance", "Allow", false, "ActiveDirectory", notes: "Privileged access OU with explicit inheritance block");
            AddAccessControlEvidence(world, company.Id, tier0Admins?.Id, "Group", "Container", pawContainer.Id, "ResetPassword", "Allow", false, "ActiveDirectory", notes: "Tier-0 delegated recovery on privileged workstation accounts");
        }
    }

    private void CreateCrossTenantPolicyObjects(SyntheticEnterpriseWorld world, Company company)
    {
        var entraStore = world.IdentityStores.FirstOrDefault(store =>
            store.CompanyId == company.Id
            && string.Equals(store.StoreType, "EntraTenant", StringComparison.OrdinalIgnoreCase));
        var guestGroup = FindGroup(world.Groups, company.Id, "SG-B2BGuests");
        var guestCollaborationGroup = FindGroup(world.Groups, company.Id, "M365-GuestCollaboration");

        foreach (var crossTenantPolicy in world.CrossTenantAccessPolicies.Where(policy => policy.CompanyId == company.Id))
        {
            var policy = EnsurePolicy(
                world,
                company.Id,
                crossTenantPolicy.PolicyName,
                "CrossTenantAccessPolicy",
                "EntraID",
                "ExternalCollaboration",
                $"Cross-tenant access policy for {crossTenantPolicy.RelationshipType} relationships.",
                entraStore?.Id,
                null,
                nameof(CrossTenantAccessPolicyRecord),
                crossTenantPolicy.Id);

            AddPolicySetting(world, company.Id, policy.Id, "DefaultAccess", "CrossTenantAccess", "String", crossTenantPolicy.DefaultAccess);
            AddPolicySetting(world, company.Id, policy.Id, "ConditionalAccessProfile", "CrossTenantAccess", "String", crossTenantPolicy.ConditionalAccessProfile);
            AddPolicySetting(world, company.Id, policy.Id, "AllowedResourceScope", "CrossTenantAccess", "String", crossTenantPolicy.AllowedResourceScope);
            AddPolicySetting(world, company.Id, policy.Id, "InboundTrustMfa", "CrossTenantAccess", "Boolean", crossTenantPolicy.InboundTrustMfa ? "true" : "false");
            AddPolicySetting(world, company.Id, policy.Id, "InboundTrustCompliantDevice", "CrossTenantAccess", "Boolean", crossTenantPolicy.InboundTrustCompliantDevice ? "true" : "false");
            AddPolicySetting(world, company.Id, policy.Id, "EntitlementManagementEnabled", "Governance", "Boolean", crossTenantPolicy.EntitlementManagementEnabled ? "true" : "false");

            AddPolicyTarget(world, company.Id, policy.Id, "IdentityStore", entraStore?.Id, "Scope", false, 1);
            AddPolicyTarget(world, company.Id, policy.Id, "ExternalOrganization", crossTenantPolicy.ExternalOrganizationId, "PartnerScope", false, 1);
            AddPolicyTarget(world, company.Id, policy.Id, "Group", guestGroup?.Id, "Include", false, 1);
            AddPolicyTarget(world, company.Id, policy.Id, "Group", guestCollaborationGroup?.Id, "Include", false, 2);
            AddAccessControlEvidence(world, company.Id, guestGroup?.Id, "Group", "Policy", policy.Id, "ApplyPolicy", "Allow", false, "EntraID");
            AddAccessControlEvidence(world, company.Id, guestCollaborationGroup?.Id, "Group", "Policy", policy.Id, "CollaborationAccess", "Allow", false, "EntraID");
        }
    }

    private void SetManagerRelationships(
        SyntheticEnterpriseWorld world,
        Company company,
        IReadOnlyList<Person> companyPeople,
        IReadOnlyList<DirectoryAccount> peopleAccounts)
    {
        var managerMap = peopleAccounts.ToDictionary(a => a.PersonId ?? string.Empty, a => a.Id, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < world.Accounts.Count; i++)
        {
            var account = world.Accounts[i];
            if (account.CompanyId != company.Id || string.IsNullOrWhiteSpace(account.PersonId))
            {
                continue;
            }

            var person = companyPeople.FirstOrDefault(p => p.Id == account.PersonId);
            if (person?.ManagerPersonId is not null && managerMap.TryGetValue(person.ManagerPersonId, out var managerAccountId))
            {
                world.Accounts[i] = account with { ManagerAccountId = managerAccountId };
            }
        }
    }

    private List<DirectoryOrganizationalUnit> CreateOus(
        Company company,
        IReadOnlyList<Department> departments,
        string rootDomain,
        bool includeAdministrativeTiers)
    {
        var domainParts = rootDomain.Split('.');
        var dc = string.Join(",", domainParts.Select(part => $"DC={part}"));

        var root = CreateOu(company, "Corp", null, $"OU=Corp,{dc}", "Root");
        var users = CreateOu(company, "Users", root.Id, $"OU=Users,{root.DistinguishedName}", "User Accounts");
        var service = CreateOu(company, "Service Accounts", root.Id, $"OU=Service Accounts,{root.DistinguishedName}", "Service Accounts");
        var shared = CreateOu(company, "Shared Mailboxes", root.Id, $"OU=Shared Mailboxes,{root.DistinguishedName}", "Shared Mailboxes");
        var groups = CreateOu(company, "Groups", root.Id, $"OU=Groups,{root.DistinguishedName}", "Groups");
        var externalUsers = CreateOu(company, "External Users", root.Id, $"OU=External Users,{root.DistinguishedName}", "External Identities");
        var contractors = CreateOu(company, "Contractors", externalUsers.Id, $"OU=Contractors,{externalUsers.DistinguishedName}", "Contractor Accounts");
        var managedServices = CreateOu(company, "Managed Services", externalUsers.Id, $"OU=Managed Services,{externalUsers.DistinguishedName}", "Managed Service Provider Accounts");
        var guests = CreateOu(company, "Guests", externalUsers.Id, $"OU=Guests,{externalUsers.DistinguishedName}", "B2B Guest Accounts");
        var computers = CreateOu(company, "Computers", root.Id, $"OU=Computers,{root.DistinguishedName}", "Managed Computers");
        var workstations = CreateOu(company, "Workstations", computers.Id, $"OU=Workstations,{computers.DistinguishedName}", "Managed Workstations");
        var servers = CreateOu(company, "Servers", computers.Id, $"OU=Servers,{computers.DistinguishedName}", "Managed Servers");
        var productionServers = CreateOu(company, "Production", servers.Id, $"OU=Production,{servers.DistinguishedName}", "Production Servers");
        var stagingServers = CreateOu(company, "Staging", servers.Id, $"OU=Staging,{servers.DistinguishedName}", "Staging Servers");
        var developmentServers = CreateOu(company, "Development", servers.Id, $"OU=Development,{servers.DistinguishedName}", "Development Servers");

        var result = new List<DirectoryOrganizationalUnit>
        {
            root,
            users,
            service,
            shared,
            groups,
            externalUsers,
            contractors,
            managedServices,
            guests,
            computers,
            workstations,
            servers,
            productionServers,
            stagingServers,
            developmentServers
        };

        if (includeAdministrativeTiers)
        {
            var adminAccounts = CreateOu(company, "Admin Accounts", root.Id, $"OU=Admin Accounts,{root.DistinguishedName}", "Administrative Accounts");
            var pawOu = CreateOu(company, "Privileged Access Workstations", workstations.Id, $"OU=Privileged Access Workstations,{workstations.DistinguishedName}", "Privileged admin workstations");
            result.Add(adminAccounts);
            result.Add(pawOu);
            result.Add(CreateOu(company, "Tier 0", adminAccounts.Id, $"OU=Tier 0,{adminAccounts.DistinguishedName}", "Tier 0 Administrative Accounts"));
            result.Add(CreateOu(company, "Tier 1", adminAccounts.Id, $"OU=Tier 1,{adminAccounts.DistinguishedName}", "Tier 1 Administrative Accounts"));
            result.Add(CreateOu(company, "Tier 2", adminAccounts.Id, $"OU=Tier 2,{adminAccounts.DistinguishedName}", "Tier 2 Administrative Accounts"));
        }

        foreach (var department in departments)
        {
            result.Add(CreateOu(
                company,
                department.Name,
                users.Id,
                $"OU={EscapeDn(department.Name)},{users.DistinguishedName}",
                "Department Users"));
        }

        return result;
    }

    private List<DirectoryAccount> CreateUserAccounts(
        Company company,
        IReadOnlyList<Person> people,
        IReadOnlyList<Department> departments,
        IReadOnlyList<DirectoryOrganizationalUnit> ous,
        HashSet<string> issuedPasswords)
    {
        var usersOu = ous.First(o => o.Name == "Users");
        var departmentOus = ous
            .Where(o => o.ParentOuId == usersOu.Id)
            .GroupBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var departmentNamesById = departments.ToDictionary(d => d.Id, d => d.Name, StringComparer.OrdinalIgnoreCase);

        return people.Select(person =>
        {
            var sam = BuildSam(person.FirstName, person.LastName, person.EmployeeId);
            var targetOu = departmentNamesById.TryGetValue(person.DepartmentId, out var departmentName) &&
                           departmentOus.TryGetValue(departmentName, out var departmentOu)
                ? departmentOu
                : usersOu;
            var passwordLastSet = _clock.UtcNow.AddDays(-_randomSource.Next(1, 90));

            return new DirectoryAccount
            {
                Id = _idFactory.Next("ACT"),
                CompanyId = company.Id,
                PersonId = person.Id,
                AccountType = "User",
                SamAccountName = sam,
                UserPrincipalName = person.UserPrincipalName,
                Mail = person.UserPrincipalName,
                DistinguishedName = $"CN={EscapeDn(person.DisplayName)},{targetOu.DistinguishedName}",
                OuId = targetOu.Id,
                Enabled = true,
                Privileged = false,
                MfaEnabled = true,
                EmployeeId = person.EmployeeId,
                GeneratedPassword = CreateUniquePassword(issuedPasswords),
                PasswordProfile = "EmployeeStandard",
                AdministrativeTier = null,
                PasswordLastSet = passwordLastSet,
                PasswordExpires = passwordLastSet.AddDays(90),
                PasswordNeverExpires = false,
                MustChangePasswordAtNextLogon = ShouldRequirePasswordReset($"employee:{company.Id}:{person.Id}:{person.EmployeeId}", 0.02),
                UserType = "Member",
                IdentityProvider = "HybridDirectory",
                ExternalAccessCategory = "Employee"
            };
        }).ToList();
    }

    private List<DirectoryAccount> CreateServiceAccounts(
        Company company,
        ScenarioCompanyDefinition definition,
        IReadOnlyList<DirectoryOrganizationalUnit> ous,
        string rootDomain,
        HashSet<string> issuedPasswords,
        bool includeAdministrativeTiers)
    {
        var targetOu = includeAdministrativeTiers
            ? FindAdminTierOu(ous, "Tier 1") ?? ous.First(o => o.Name == "Service Accounts")
            : ous.First(o => o.Name == "Service Accounts");
        var results = new List<DirectoryAccount>();

        for (var i = 0; i < definition.ServiceAccountCount; i++)
        {
            var name = $"svc_{Slug(company.Name)}_{i + 1:00}";
            var passwordLastSet = _clock.UtcNow.AddDays(-_randomSource.Next(7, 180));
            results.Add(new DirectoryAccount
            {
                Id = _idFactory.Next("ACT"),
                CompanyId = company.Id,
                AccountType = "Service",
                SamAccountName = Truncate(name, 20),
                UserPrincipalName = $"{name}@{rootDomain}",
                Mail = null,
                DistinguishedName = $"CN={name},{targetOu.DistinguishedName}",
                OuId = targetOu.Id,
                Enabled = true,
                Privileged = _randomSource.NextDouble() < 0.25,
                MfaEnabled = false,
                GeneratedPassword = CreateUniquePassword(issuedPasswords, 20),
                PasswordProfile = "ServiceManaged",
                AdministrativeTier = includeAdministrativeTiers ? "Tier1" : null,
                PasswordLastSet = passwordLastSet,
                PasswordExpires = null,
                PasswordNeverExpires = true,
                MustChangePasswordAtNextLogon = false,
                UserType = "Member",
                IdentityProvider = "HybridDirectory",
                ExternalAccessCategory = "Service"
            });
        }

        return results;
    }

    private List<DirectoryAccount> CreateSharedMailboxes(
        Company company,
        ScenarioCompanyDefinition definition,
        IReadOnlyList<DirectoryOrganizationalUnit> ous,
        string rootDomain,
        HashSet<string> issuedPasswords)
    {
        var targetOu = ous.First(o => o.Name == "Shared Mailboxes");
        var mailboxPrefixes = new[] { "helpdesk", "payroll", "accounts-payable", "sales-ops", "recruiting", "facilities", "it-ops" };
        var results = new List<DirectoryAccount>();

        for (var i = 0; i < definition.SharedMailboxCount; i++)
        {
            var localPart = mailboxPrefixes[i % mailboxPrefixes.Length];
            var passwordLastSet = _clock.UtcNow.AddDays(-_randomSource.Next(14, 180));
            results.Add(new DirectoryAccount
            {
                Id = _idFactory.Next("ACT"),
                CompanyId = company.Id,
                AccountType = "Shared",
                SamAccountName = Truncate(localPart.Replace("-", ""), 20),
                UserPrincipalName = $"{localPart}@{rootDomain}",
                Mail = $"{localPart}@{rootDomain}",
                DistinguishedName = $"CN={EscapeDn(localPart)},{targetOu.DistinguishedName}",
                OuId = targetOu.Id,
                Enabled = true,
                Privileged = false,
                MfaEnabled = false,
                GeneratedPassword = CreateUniquePassword(issuedPasswords, 18),
                PasswordProfile = "SharedMailbox",
                AdministrativeTier = null,
                PasswordLastSet = passwordLastSet,
                PasswordExpires = null,
                PasswordNeverExpires = true,
                MustChangePasswordAtNextLogon = false,
                UserType = "Member",
                IdentityProvider = "HybridDirectory",
                ExternalAccessCategory = "SharedMailbox"
            });
        }

        return results;
    }

    private List<DirectoryAccount> CreatePrivilegedAccounts(
        Company company,
        IReadOnlyList<Person> people,
        IReadOnlyList<DirectoryOrganizationalUnit> ous,
        string rootDomain,
        HashSet<string> issuedPasswords,
        bool includeAdministrativeTiers)
    {
        var managers = people.Where(person =>
                person.Title.Contains("Chief", StringComparison.OrdinalIgnoreCase) ||
                person.Title.Contains("Vice President", StringComparison.OrdinalIgnoreCase) ||
                person.Title.Contains("Director", StringComparison.OrdinalIgnoreCase) ||
                person.Title.Contains("Manager", StringComparison.OrdinalIgnoreCase))
            .Take(Math.Max(2, people.Count / 20))
            .ToList();

        return managers.Select(person =>
        {
            var tier = includeAdministrativeTiers ? ResolveAdministrativeTier(person) : null;
            var targetOu = tier is null
                ? ous.First(o => o.Name == "Service Accounts")
                : FindAdminTierOu(ous, tier.Replace("Tier", "Tier ")) ?? ous.First(o => o.Name == "Service Accounts");
            var employeeSuffix = Slug(person.EmployeeId);
            var localPart = $"adm.{Slug(person.FirstName)}.{Slug(person.LastName)}.{employeeSuffix}";
            var passwordLastSet = _clock.UtcNow.AddDays(-_randomSource.Next(1, 45));

            return new DirectoryAccount
            {
                Id = _idFactory.Next("ACT"),
                CompanyId = company.Id,
                PersonId = person.Id,
                AccountType = "Privileged",
                SamAccountName = Truncate($"adm_{Slug(person.LastName)}_{employeeSuffix}", 20),
                UserPrincipalName = $"{localPart}@{rootDomain}",
                Mail = null,
                DistinguishedName = $"CN={EscapeDn(person.DisplayName)} Admin,{targetOu.DistinguishedName}",
                OuId = targetOu.Id,
                Enabled = true,
                Privileged = true,
                MfaEnabled = _randomSource.NextDouble() >= 0.15,
                EmployeeId = person.EmployeeId,
                GeneratedPassword = CreateUniquePassword(issuedPasswords, 20),
                PasswordProfile = "PrivilegedElevated",
                AdministrativeTier = tier,
                PasswordLastSet = passwordLastSet,
                PasswordExpires = passwordLastSet.AddDays(45),
                PasswordNeverExpires = false,
                MustChangePasswordAtNextLogon = ShouldRequirePasswordReset($"privileged:{company.Id}:{person.Id}:{person.EmployeeId}:{tier}", 0.05),
                UserType = "Member",
                IdentityProvider = "HybridDirectory",
                ExternalAccessCategory = "Privileged"
            };
        }).ToList();
    }

    private List<DirectoryGroup> CreateGroups(
        Company company,
        IReadOnlyList<Department> departments,
        IReadOnlyList<DirectoryOrganizationalUnit> ous,
        bool includeAdministrativeTiers)
    {
        var groupsOu = ous.First(o => o.Name == "Groups");
        var result = new List<DirectoryGroup>();

        foreach (var department in departments)
        {
            var slug = Slug(department.Name);
            result.Add(CreateGroup(company, $"SG-{slug}-Users", "Security", "Global", false, groupsOu, $"Baseline access for {department.Name}"));
            result.Add(CreateGroup(company, $"DL-{slug}", "Distribution", "Universal", true, groupsOu, $"Mail distribution for {department.Name}"));
        }

        result.Add(CreateGroup(company, "SG-AllEmployees", "Security", "Global", false, groupsOu, "All employee baseline access"));
        result.Add(CreateGroup(company, "M365-AllEmployees", "M365", "Universal", true, groupsOu, "Collaboration membership"));
        result.Add(CreateGroup(company, "SG-ExternalContractors", "Security", "Global", false, groupsOu, "External contractor baseline access"));
        result.Add(CreateGroup(company, "SG-MSP-Operators", "Security", "Global", false, groupsOu, "Managed service provider operator access"));
        result.Add(CreateGroup(company, "SG-B2BGuests", "Security", "Global", false, groupsOu, "B2B guest collaboration access"));
        result.Add(CreateGroup(company, "M365-GuestCollaboration", "M365", "Universal", true, groupsOu, "Guest collaboration membership"));

        if (includeAdministrativeTiers)
        {
            result.Add(CreateGroup(company, "SG-PrivilegedAccess", "Security", "Universal", false, groupsOu, "Umbrella privileged access group", "Tier0"));
            result.Add(CreateGroup(company, "SG-Tier0-IdentityAdmins", "Security", "Global", false, groupsOu, "Tier 0 identity administrators", "Tier0"));
            result.Add(CreateGroup(company, "SG-Tier0-PAW-Users", "Security", "Global", false, groupsOu, "Tier 0 privileged access workstation users", "Tier0"));
            result.Add(CreateGroup(company, "SG-Tier0-PAW-Devices", "Security", "Global", false, groupsOu, "Tier 0 privileged access workstation devices", "Tier0"));
            result.Add(CreateGroup(company, "SG-Tier1-ServerAdmins", "Security", "Global", false, groupsOu, "Tier 1 server administrators", "Tier1"));
            result.Add(CreateGroup(company, "SG-Tier1-WorkstationAdmins", "Security", "Global", false, groupsOu, "Tier 1 workstation administrators", "Tier1"));
            result.Add(CreateGroup(company, "SG-Tier1-PAW-Users", "Security", "Global", false, groupsOu, "Tier 1 privileged access workstation users", "Tier1"));
            result.Add(CreateGroup(company, "SG-Tier1-PAW-Devices", "Security", "Global", false, groupsOu, "Tier 1 privileged access workstation devices", "Tier1"));
            result.Add(CreateGroup(company, "SG-Tier1-ManagedWorkstations", "Security", "Global", false, groupsOu, "Tier 1 managed workstation computer objects", "Tier1"));
            result.Add(CreateGroup(company, "SG-Tier1-ManagedServers", "Security", "Global", false, groupsOu, "Tier 1 managed server computer objects", "Tier1"));
            result.Add(CreateGroup(company, "SG-Tier2-Helpdesk", "Security", "Global", false, groupsOu, "Tier 2 helpdesk operators", "Tier2"));
            result.Add(CreateGroup(company, "SG-Tier2-ApplicationSupport", "Security", "Global", false, groupsOu, "Tier 2 application support", "Tier2"));
        }

        return result;
    }

    private List<DirectoryGroupMembership> CreateMemberships(
        Company company,
        IReadOnlyList<Department> departments,
        IReadOnlyList<Person> people,
        IReadOnlyList<DirectoryGroup> groups,
        IReadOnlyList<DirectoryAccount> accounts,
        bool includeAdministrativeTiers)
    {
        var results = new List<DirectoryGroupMembership>();
        var userAccounts = accounts.Where(a => a.CompanyId == company.Id && (a.AccountType == "User" || a.AccountType == "Contractor")).ToList();
        var privilegedAccounts = accounts.Where(a => a.CompanyId == company.Id && a.AccountType == "Privileged").ToList();

        var allEmployeesGroup = FindGroup(groups, company.Id, "SG-AllEmployees");
        var m365Group = FindGroup(groups, company.Id, "M365-AllEmployees");

        foreach (var account in userAccounts)
        {
            if (m365Group is not null)
            {
                results.Add(CreateMembership(m365Group.Id, account.Id, "Account"));
            }
        }

        foreach (var department in departments)
        {
            var sg = FindGroup(groups, company.Id, $"SG-{Slug(department.Name)}-Users");
            var dl = FindGroup(groups, company.Id, $"DL-{Slug(department.Name)}");

            if (allEmployeesGroup is not null && sg is not null)
            {
                results.Add(CreateMembership(allEmployeesGroup.Id, sg.Id, "Group"));
            }

            var deptPeople = people.Where(p => p.CompanyId == company.Id && p.DepartmentId == department.Id).ToList();
            foreach (var person in deptPeople)
            {
                var account = userAccounts.FirstOrDefault(a => a.PersonId == person.Id);
                if (account is null)
                {
                    continue;
                }

                if (sg is not null)
                {
                    results.Add(CreateMembership(sg.Id, account.Id, "Account"));
                }

                if (dl is not null)
                {
                    results.Add(CreateMembership(dl.Id, account.Id, "Account"));
                }
            }
        }

        if (allEmployeesGroup is not null)
        {
            foreach (var account in userAccounts)
            {
                var person = people.FirstOrDefault(candidate => candidate.Id == account.PersonId);
                if (person is not null && !string.IsNullOrWhiteSpace(person.DepartmentId))
                {
                    continue;
                }

                results.Add(CreateMembership(allEmployeesGroup.Id, account.Id, "Account"));
            }
        }

        if (includeAdministrativeTiers)
        {
            var privilegedUmbrella = FindGroup(groups, company.Id, "SG-PrivilegedAccess");
            var tier0 = FindGroup(groups, company.Id, "SG-Tier0-IdentityAdmins");
            var tier0Paw = FindGroup(groups, company.Id, "SG-Tier0-PAW-Users");
            var tier1Server = FindGroup(groups, company.Id, "SG-Tier1-ServerAdmins");
            var tier1Workstation = FindGroup(groups, company.Id, "SG-Tier1-WorkstationAdmins");
            var tier1Paw = FindGroup(groups, company.Id, "SG-Tier1-PAW-Users");
            var tier2Helpdesk = FindGroup(groups, company.Id, "SG-Tier2-Helpdesk");
            var tier2AppSupport = FindGroup(groups, company.Id, "SG-Tier2-ApplicationSupport");

            foreach (var adminGroup in new[] { tier0, tier0Paw, tier1Server, tier1Workstation, tier1Paw, tier2Helpdesk, tier2AppSupport })
            {
                if (privilegedUmbrella is not null && adminGroup is not null)
                {
                    results.Add(CreateMembership(privilegedUmbrella.Id, adminGroup.Id, "Group"));
                }
            }

            foreach (var account in privilegedAccounts)
            {
                var person = people.FirstOrDefault(candidate => candidate.Id == account.PersonId);
                var targetGroups = ResolveAdminGroups(account, person, tier0, tier1Server, tier1Workstation, tier2Helpdesk, tier2AppSupport);
                foreach (var group in targetGroups.DistinctBy(group => group.Id))
                {
                    results.Add(CreateMembership(group.Id, account.Id, "Account"));
                }

                if (account.AdministrativeTier == "Tier0" && tier0Paw is not null)
                {
                    results.Add(CreateMembership(tier0Paw.Id, account.Id, "Account"));
                }
                else if (account.AdministrativeTier == "Tier1" && tier1Paw is not null)
                {
                    results.Add(CreateMembership(tier1Paw.Id, account.Id, "Account"));
                }
            }
        }

        return results;
    }

    private List<ExternalOrganization> EnsureExternalIdentityOrganizations(
        SyntheticEnterpriseWorld world,
        Company company,
        IReadOnlyList<Department> departments,
        ScenarioCompanyDefinition definition,
        string rootDomain)
    {
        var ownerDepartmentId = departments.FirstOrDefault(department =>
                                    department.Name.Contains("Information Technology", StringComparison.OrdinalIgnoreCase)
                                    || department.Name.Contains("Operations", StringComparison.OrdinalIgnoreCase)
                                    || department.Name.Contains("Engineering", StringComparison.OrdinalIgnoreCase))
                                ?.Id
                              ?? departments.FirstOrDefault()?.Id
                              ?? string.Empty;
        var country = definition.Countries.FirstOrDefault() ?? "United States";

        return new List<ExternalOrganization>
        {
            EnsureExternalOrganization(world, company, "NorthPeak Talent Partners", "StaffingPartner", country, ownerDepartmentId, "ExternalWorkforce", "Medium", "northpeaktalent.example"),
            EnsureExternalOrganization(world, company, "BlueRiver Managed Services", "ManagedServiceProvider", country, ownerDepartmentId, "ManagedServices", "High", "blueriverms.example"),
            EnsureExternalOrganization(world, company, $"{company.Name} Partner Collaboration", "Partner", country, ownerDepartmentId, "B2BPartner", "Medium", $"partners.{rootDomain}")
        };
    }

    private ExternalOrganization EnsureExternalOrganization(
        SyntheticEnterpriseWorld world,
        Company company,
        string name,
        string relationshipType,
        string country,
        string ownerDepartmentId,
        string segment,
        string criticality,
        string websiteHost)
    {
        var existing = world.ExternalOrganizations.FirstOrDefault(organization =>
            organization.CompanyId == company.Id
            && string.Equals(organization.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        var created = new ExternalOrganization
        {
            Id = _idFactory.Next("EXT"),
            CompanyId = company.Id,
            Name = name,
            LegalName = name,
            Description = $"{segment} external organization operating in {company.Industry}.",
            Tagline = relationshipType switch
            {
                "ManagedServiceProvider" => $"Supporting {company.Name} operations",
                "Partner" or "StaffingPartner" => $"Collaborating with {company.Name}",
                _ => $"Serving {company.Name} workforce and operations"
            },
            RelationshipType = relationshipType,
            Industry = company.Industry,
            Country = country,
            PrimaryDomain = websiteHost,
            Website = $"https://{websiteHost}",
            ContactEmail = relationshipType switch
            {
                "ManagedServiceProvider" => $"operations@{websiteHost}",
                "Partner" or "StaffingPartner" => $"alliances@{websiteHost}",
                _ => $"contact@{websiteHost}"
            },
            TaxIdentifier = BuildSyntheticExternalTaxIdentifier(country, name),
            Segment = segment,
            RevenueBand = relationshipType == "ManagedServiceProvider" ? "Enterprise" : "MidMarket",
            OwnerDepartmentId = ownerDepartmentId,
            Criticality = criticality
        };
        world.ExternalOrganizations.Add(created);
        return created;
    }

    private string BuildSyntheticExternalTaxIdentifier(string country, string organizationName)
    {
        var normalizedCountry = country.Trim();
        var ordinal = Math.Abs(StringComparer.OrdinalIgnoreCase.GetHashCode(organizationName)) % 9_000_000;

        return normalizedCountry switch
        {
            "United States" or "US" or "USA" => $"{_randomSource.Next(10, 99):00}-{ordinal + 1_000_000:0000000}",
            "Canada" or "CA" => $"{_randomSource.Next(100_000_000, 999_999_999)}RT{(ordinal % 9000) + 1000}",
            "United Kingdom" or "UK" or "GB" => $"GB{(ordinal % 900_000_000) + 100_000_000}",
            _ => $"{organizationName[..Math.Min(3, organizationName.Length)].ToUpperInvariant().PadRight(3, 'X')}-{(ordinal % 900_000) + 100_000}"
        };
    }

    private List<Person> CreateExternalPeople(
        Company company,
        ScenarioCompanyDefinition definition,
        IdentityProfile identityProfile,
        IReadOnlyList<Person> employees,
        IReadOnlyList<Department> departments,
        IReadOnlyList<Team> teams,
        IReadOnlyList<Office> offices,
        IReadOnlyList<ExternalOrganization> externalOrganizations,
        string rootDomain)
    {
        var results = new List<Person>();
        if (employees.Count == 0 || departments.Count == 0 || teams.Count == 0)
        {
            return results;
        }

        var contractorOrg = externalOrganizations.FirstOrDefault(org => org.RelationshipType == "StaffingPartner");
        var managedServicesOrg = externalOrganizations.FirstOrDefault(org => org.RelationshipType == "ManagedServiceProvider");
        var guestOrg = externalOrganizations.FirstOrDefault(org => org.RelationshipType == "Partner")
                       ?? externalOrganizations.FirstOrDefault(org => org.RelationshipType == "Vendor")
                       ?? externalOrganizations.FirstOrDefault();
        var firstNames = employees.Select(employee => employee.FirstName).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var lastNames = employees.Select(employee => employee.LastName).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var issuedPersonUpns = new HashSet<string>(
            employees.Select(employee => employee.UserPrincipalName).Where(value => !string.IsNullOrWhiteSpace(value)),
            StringComparer.OrdinalIgnoreCase);
        if (firstNames.Count == 0) firstNames.AddRange(new[] { "Alex", "Jordan", "Taylor", "Morgan" });
        if (lastNames.Count == 0) lastNames.AddRange(new[] { "Carter", "Patel", "Reed", "Nguyen" });

        if (identityProfile.IncludeExternalWorkforce && contractorOrg is not null)
        {
            var contractorCount = Math.Max(2, (int)Math.Round(definition.EmployeeCount * Math.Clamp(identityProfile.ContractorRatio, 0.0, 0.35)));
            results.AddRange(CreateExternalPeopleOfType(
                company,
                employees,
                departments,
                teams,
                offices,
                contractorOrg,
                contractorCount,
                "Contractor",
                "InternalContractor",
                rootDomain,
                firstNames,
                lastNames,
                new[] { "Analyst", "Specialist", "Engineer", "Project Manager", "Coordinator" },
                issuedPersonUpns));
        }

        if (identityProfile.IncludeExternalWorkforce && managedServicesOrg is not null)
        {
            var mspCount = Math.Max(1, (int)Math.Round(definition.EmployeeCount * Math.Clamp(identityProfile.ManagedServiceProviderRatio, 0.0, 0.1)));
            results.AddRange(CreateExternalPeopleOfType(
                company,
                employees,
                departments,
                teams,
                offices,
                managedServicesOrg,
                mspCount,
                "ManagedServiceProvider",
                "ExternalOperator",
                rootDomain,
                firstNames,
                lastNames,
                new[] { "Support Engineer", "Identity Administrator", "Platform Operator", "Service Desk Engineer" },
                issuedPersonUpns));
        }

        if (identityProfile.IncludeB2BGuests && guestOrg is not null)
        {
            var guestCount = Math.Max(2, (int)Math.Round(definition.EmployeeCount * Math.Clamp(identityProfile.GuestUserRatio, 0.0, 0.15)));
            results.AddRange(CreateExternalPeopleOfType(
                company,
                employees,
                departments,
                teams,
                offices,
                guestOrg,
                guestCount,
                "Guest",
                "B2BGuest",
                rootDomain,
                firstNames,
                lastNames,
                new[] { "Partner Liaison", "Supplier Coordinator", "Customer Success Lead", "Project Consultant" },
                issuedPersonUpns));
        }

        return results;
    }

    private List<Person> CreateExternalPeopleOfType(
        Company company,
        IReadOnlyList<Person> employees,
        IReadOnlyList<Department> departments,
        IReadOnlyList<Team> teams,
        IReadOnlyList<Office> offices,
        ExternalOrganization employer,
        int count,
        string employmentType,
        string personType,
        string rootDomain,
        IReadOnlyList<string> firstNames,
        IReadOnlyList<string> lastNames,
        IReadOnlyList<string> titles,
        ISet<string> issuedPersonUpns)
    {
        var results = new List<Person>();
        var preferredDepartmentNames = employmentType switch
        {
            "ManagedServiceProvider" => new[] { "Information Technology", "Operations", "Engineering" },
            "Guest" => new[] { "Sales", "Marketing", "Operations", "Information Technology" },
            _ => new[] { "Operations", "Engineering", "Finance", "Information Technology" }
        };
        var targetDepartments = departments.Where(department =>
                preferredDepartmentNames.Any(name => department.Name.Contains(name, StringComparison.OrdinalIgnoreCase)))
            .DefaultIfEmpty(departments.First())
            .ToList();
        var targetTeams = teams
            .Where(team => targetDepartments.Any(department => department.Id == team.DepartmentId))
            .DefaultIfEmpty(teams.First())
            .ToList();

        for (var i = 0; i < count; i++)
        {
            var department = targetDepartments[i % targetDepartments.Count];
            var team = targetTeams[i % targetTeams.Count];
            var sponsor = employees[_randomSource.Next(employees.Count)];
            var firstName = firstNames[_randomSource.Next(firstNames.Count)];
            var lastName = lastNames[_randomSource.Next(lastNames.Count)];
            var workerNumber = employmentType switch
            {
                "ManagedServiceProvider" => $"MSP-{i + 1:0000}",
                "Guest" => $"GST-{i + 1:0000}",
                _ => $"CNT-{i + 1:0000}"
            };
            var office = SelectExternalOffice(offices, employer.Country, sponsor.OfficeId);
            var personUpn = BuildUniqueExternalPersonUpn(firstName, lastName, employmentType, employer, workerNumber, rootDomain, issuedPersonUpns);

            results.Add(new Person
            {
                Id = _idFactory.Next("PERS"),
                CompanyId = company.Id,
                TeamId = team.Id,
                DepartmentId = department.Id,
                FirstName = firstName,
                LastName = lastName,
                DisplayName = $"{firstName} {lastName}",
                Title = titles[i % titles.Count],
                ManagerPersonId = sponsor.Id,
                EmployeeId = workerNumber,
                Country = office?.Country
                    ?? (!string.IsNullOrWhiteSpace(employer.Country) ? employer.Country : sponsor.Country),
                OfficeId = office?.Id ?? sponsor.OfficeId,
                UserPrincipalName = personUpn,
                EmploymentType = employmentType,
                PersonType = personType,
                EmployerOrganizationId = employer.Id,
                SponsorPersonId = sponsor.Id
            });
        }

        return results;
    }

    private List<DirectoryAccount> CreateExternalAccounts(
        Company company,
        IReadOnlyList<Person> externalPeople,
        IReadOnlyList<ExternalOrganization> externalOrganizations,
        IReadOnlyList<DirectoryAccount> existingAccounts,
        IReadOnlyList<DirectoryOrganizationalUnit> ous,
        HashSet<string> issuedPasswords,
        string rootDomain)
    {
        var contractorsOu = ous.First(o => o.Name == "Contractors");
        var managedServicesOu = ous.First(o => o.Name == "Managed Services");
        var guestsOu = ous.First(o => o.Name == "Guests");
        var results = new List<DirectoryAccount>();

        foreach (var person in externalPeople)
        {
            var passwordLastSet = _clock.UtcNow.AddDays(-_randomSource.Next(1, 90));
            var accountType = person.EmploymentType switch
            {
                "ManagedServiceProvider" => "ManagedServiceProvider",
                "Guest" => "Guest",
                _ => "Contractor"
            };
            var userType = person.EmploymentType == "Guest" || person.EmploymentType == "ManagedServiceProvider" ? "Guest" : "Member";
            var employer = externalOrganizations.FirstOrDefault(organization => organization.Id == person.EmployerOrganizationId);
            var sponsorAccount = existingAccounts.FirstOrDefault(account =>
                string.Equals(account.PersonId, person.SponsorPersonId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(account.AccountType, "User", StringComparison.OrdinalIgnoreCase));
            var alternateSponsorAccount = existingAccounts
                .Where(account =>
                    string.Equals(account.AccountType, "User", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(account.Id, sponsorAccount?.Id, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(account.PersonId))
                .OrderBy(account => account.Id, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            var homeTenantDomain = person.EmploymentType == "Guest" || person.EmploymentType == "ManagedServiceProvider"
                ? ResolveExternalHomeTenantDomain(employer)
                : rootDomain;
            var resourceTenantDomain = rootDomain;
            var invitationStatus = accountType == "Guest" || accountType == "ManagedServiceProvider"
                ? (_randomSource.NextDouble() < 0.15 ? "PendingAcceptance" : "Accepted")
                : null;
            DateTimeOffset? invitationSentAt = invitationStatus is null
                ? null
                : _clock.UtcNow.AddDays(-_randomSource.Next(2, 120));
            DateTimeOffset? invitationRedeemedAt = invitationStatus == "Accepted" && invitationSentAt is not null
                ? invitationSentAt.Value.AddHours(_randomSource.Next(4, 240))
                : null;
            DateTimeOffset? accessExpiresAt = accountType switch
            {
                "Guest" => invitationSentAt?.AddDays(_randomSource.Next(45, 365)),
                "Contractor" => passwordLastSet.AddDays(_randomSource.Next(30, 180)),
                _ => null
            };
            var guestLifecycleState = invitationStatus switch
            {
                "PendingAcceptance" => "Invited",
                "Accepted" when accessExpiresAt is not null && accessExpiresAt <= _clock.UtcNow => "Expired",
                "Accepted" => accountType == "ManagedServiceProvider" ? "ManagedAccess" : "Active",
                _ => null
            };
            var crossTenantAccessPolicy = accountType switch
            {
                "ManagedServiceProvider" => "CrossTenantManagedAccess",
                "Guest" => "B2BCollaboration",
                _ => "HybridContractor"
            };
            var entitlementPackageName = accountType switch
            {
                "ManagedServiceProvider" => "Privileged Operations - Tier 1",
                "Guest" when employer?.RelationshipType == "Customer" => "Customer Collaboration Access",
                "Guest" => "Partner Collaboration Access",
                _ => "External Contractor Baseline"
            };
            var entitlementAssignmentState = accountType switch
            {
                "Guest" or "ManagedServiceProvider" when string.Equals(invitationStatus, "PendingAcceptance", StringComparison.OrdinalIgnoreCase) => "AssignmentPendingAcceptance",
                "Guest" or "ManagedServiceProvider" when string.Equals(guestLifecycleState, "Expired", StringComparison.OrdinalIgnoreCase) => "Expired",
                "Guest" or "ManagedServiceProvider" => "ActiveAssignment",
                _ => "Provisioned"
            };
            var lastAccessReviewAt = accountType == "Guest" || accountType == "ManagedServiceProvider"
                ? invitationRedeemedAt?.AddDays(_randomSource.Next(15, 120))
                : null;
            if (lastAccessReviewAt is not null && lastAccessReviewAt > _clock.UtcNow)
            {
                lastAccessReviewAt = _clock.UtcNow.AddDays(-_randomSource.Next(5, 30));
            }
            var accessReviewStatus = accountType switch
            {
                "Guest" or "ManagedServiceProvider" when string.Equals(invitationStatus, "PendingAcceptance", StringComparison.OrdinalIgnoreCase) => "NotStarted",
                "Guest" or "ManagedServiceProvider" when string.Equals(guestLifecycleState, "Expired", StringComparison.OrdinalIgnoreCase) => "Expired",
                "ManagedServiceProvider" => _randomSource.NextDouble() < 0.2 ? "NeedsRevalidation" : "Approved",
                "Guest" => _randomSource.NextDouble() < 0.12 ? "Scheduled" : "Approved",
                _ => "NotApplicable"
            };
            DateTimeOffset? sponsorLastChangedAt = (accountType == "Guest" || accountType == "ManagedServiceProvider")
                                                   && invitationRedeemedAt is not null
                                                   && alternateSponsorAccount is not null
                                                   && _randomSource.NextDouble() < 0.22
                ? invitationRedeemedAt.Value.AddDays(_randomSource.Next(10, 120))
                : null;
            if (sponsorLastChangedAt is not null && sponsorLastChangedAt > _clock.UtcNow)
            {
                sponsorLastChangedAt = _clock.UtcNow.AddDays(-_randomSource.Next(3, 21));
            }
            var localPart = accountType switch
            {
                "Guest" => $"{BuildGuestUpnLocalPart(person.FirstName, person.LastName, homeTenantDomain)}.{Slug(person.EmployeeId)}",
                "ManagedServiceProvider" => $"msp.{Slug(person.FirstName)}.{Slug(person.LastName)}.{Slug(person.EmployeeId)}",
                _ => $"{Slug(person.FirstName)}.{Slug(person.LastName)}.{Slug(person.EmployeeId)}.ctr"
            };
            var upn = accountType == "Guest"
                ? $"{localPart}@{rootDomain}"
                : $"{localPart}@{rootDomain}";
            var targetOu = accountType switch
            {
                "ManagedServiceProvider" => managedServicesOu,
                "Guest" => guestsOu,
                _ => contractorsOu
            };

            results.Add(new DirectoryAccount
            {
                Id = _idFactory.Next("ACT"),
                CompanyId = company.Id,
                PersonId = person.Id,
                AccountType = accountType,
                SamAccountName = BuildExternalSam(person, accountType),
                UserPrincipalName = upn,
                Mail = accountType == "Guest" ? null : upn,
                DistinguishedName = $"CN={EscapeDn(person.DisplayName)},{targetOu.DistinguishedName}",
                OuId = targetOu.Id,
                Enabled = true,
                Privileged = accountType == "ManagedServiceProvider",
                MfaEnabled = true,
                EmployeeId = person.EmployeeId,
                ManagerAccountId = null,
                GeneratedPassword = CreateUniquePassword(issuedPasswords, accountType == "ManagedServiceProvider" ? 20 : 16),
                PasswordProfile = accountType switch
                {
                    "ManagedServiceProvider" => "ExternalOperatorPrivileged",
                    "Guest" => "EntraB2BGuest",
                    _ => "ContractorStandard"
                },
                AdministrativeTier = accountType == "ManagedServiceProvider" ? "Tier1" : null,
                PasswordLastSet = passwordLastSet,
                PasswordExpires = accountType == "Guest" ? null : passwordLastSet.AddDays(90),
                PasswordNeverExpires = accountType == "ManagedServiceProvider",
                MustChangePasswordAtNextLogon = accountType == "Contractor"
                                                && ShouldRequirePasswordReset($"contractor:{company.Id}:{person.Id}:{person.EmployeeId}:{person.EmployerOrganizationId}", 0.04),
                UserType = userType,
                IdentityProvider = accountType == "Guest" || accountType == "ManagedServiceProvider" ? "EntraB2B" : "HybridDirectory",
                InvitedOrganizationId = person.EmployerOrganizationId,
                InvitedByAccountId = sponsorAccount?.Id,
                HomeTenantDomain = homeTenantDomain,
                ResourceTenantDomain = resourceTenantDomain,
                InvitationStatus = invitationStatus,
                InvitationSentAt = invitationSentAt,
                InvitationRedeemedAt = invitationRedeemedAt,
                AccessExpiresAt = accessExpiresAt,
                GuestLifecycleState = guestLifecycleState,
                CrossTenantAccessPolicy = crossTenantAccessPolicy,
                ExternalAccessCategory = accountType,
                EntitlementPackageName = entitlementPackageName,
                EntitlementAssignmentState = entitlementAssignmentState,
                LastAccessReviewAt = lastAccessReviewAt,
                AccessReviewStatus = accessReviewStatus,
                PreviousInvitedByAccountId = sponsorLastChangedAt is not null ? alternateSponsorAccount?.Id : null,
                SponsorLastChangedAt = sponsorLastChangedAt
            });
        }

        return results;
    }

    private List<DirectoryGroup> CreateExternalGroups(Company company, IReadOnlyList<DirectoryOrganizationalUnit> ous)
    {
        var groupsOu = ous.First(o => o.Name == "Groups");
        return new List<DirectoryGroup>
        {
            CreateGroup(company, "SG-ExternalContractors", "Security", "Global", false, groupsOu, "External contractor baseline access"),
            CreateGroup(company, "SG-MSP-Operators", "Security", "Global", false, groupsOu, "Managed service provider operator access"),
            CreateGroup(company, "SG-B2BGuests", "Security", "Global", false, groupsOu, "B2B guest collaboration access"),
            CreateGroup(company, "M365-GuestCollaboration", "M365", "Universal", true, groupsOu, "Guest collaboration membership")
        };
    }

    private List<DirectoryGroupMembership> CreateExternalMemberships(
        Company company,
        IReadOnlyList<Person> externalPeople,
        IReadOnlyList<DirectoryAccount> externalAccounts,
        IReadOnlyList<DirectoryGroup> groups,
        IReadOnlyList<Department> departments)
    {
        var results = new List<DirectoryGroupMembership>();
        var contractorsGroup = FindGroup(groups, company.Id, "SG-ExternalContractors");
        var mspGroup = FindGroup(groups, company.Id, "SG-MSP-Operators");
        var guestsGroup = FindGroup(groups, company.Id, "SG-B2BGuests");
        var guestCollaboration = FindGroup(groups, company.Id, "M365-GuestCollaboration");
        var allEmployeesGroup = FindGroup(groups, company.Id, "SG-AllEmployees");
        var m365AllEmployees = FindGroup(groups, company.Id, "M365-AllEmployees");

        foreach (var account in externalAccounts)
        {
            var person = externalPeople.FirstOrDefault(candidate => candidate.Id == account.PersonId);
            if (person is null)
            {
                continue;
            }

            var departmentGroup = FindDepartmentGroup(groups, departments, person.DepartmentId);
            switch (account.AccountType)
            {
                case "Contractor":
                    if (contractorsGroup is not null)
                    {
                        results.Add(CreateMembership(contractorsGroup.Id, account.Id, "Account"));
                    }
                    if (departmentGroup is not null)
                    {
                        results.Add(CreateMembership(departmentGroup.Id, account.Id, "Account"));
                    }
                    if (m365AllEmployees is not null)
                    {
                        results.Add(CreateMembership(m365AllEmployees.Id, account.Id, "Account"));
                    }
                    break;

                case "ManagedServiceProvider":
                    if (mspGroup is not null)
                    {
                        results.Add(CreateMembership(mspGroup.Id, account.Id, "Account"));
                    }
                    if (guestsGroup is not null)
                    {
                        results.Add(CreateMembership(guestsGroup.Id, account.Id, "Account"));
                    }
                    if (guestCollaboration is not null)
                    {
                        results.Add(CreateMembership(guestCollaboration.Id, account.Id, "Account"));
                    }
                    break;

                case "Guest":
                    if (guestsGroup is not null)
                    {
                        results.Add(CreateMembership(guestsGroup.Id, account.Id, "Account"));
                    }
                    if (guestCollaboration is not null)
                    {
                        results.Add(CreateMembership(guestCollaboration.Id, account.Id, "Account"));
                    }
                    break;
            }
        }

        if (allEmployeesGroup is not null && contractorsGroup is not null)
        {
            results.Add(CreateMembership(allEmployeesGroup.Id, contractorsGroup.Id, "Group"));
        }

        return results;
    }

    private void CreateCrossTenantAccessArtifacts(
        SyntheticEnterpriseWorld world,
        Company company,
        IReadOnlyList<ExternalOrganization> externalOrganizations,
        IReadOnlyList<DirectoryAccount> externalAccounts)
    {
        var guestLikeAccounts = externalAccounts
            .Where(account => string.Equals(account.IdentityProvider, "EntraB2B", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (guestLikeAccounts.Count == 0)
        {
            return;
        }

        var policyIdsByOrganization = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var organizationGroup in guestLikeAccounts
                     .Where(account => !string.IsNullOrWhiteSpace(account.InvitedOrganizationId))
                     .GroupBy(account => account.InvitedOrganizationId!, StringComparer.OrdinalIgnoreCase))
        {
            var organization = externalOrganizations.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, organizationGroup.Key, StringComparison.OrdinalIgnoreCase));
            if (organization is null)
            {
                continue;
            }

            var resourceTenantDomain = organizationGroup
                .Select(account => account.ResourceTenantDomain)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                ?? $"{Slug(company.Name)}.test";
            var homeTenantDomain = organizationGroup
                .Select(account => account.HomeTenantDomain)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                ?? ResolveExternalHomeTenantDomain(organization);
            var trustLevel = organization.RelationshipType == "ManagedServiceProvider" ? "HighTrust" : "StandardTrust";
            var defaultAccess = organization.RelationshipType == "ManagedServiceProvider" ? "AllowedWithControls" : "ScopedAllow";
            var conditionalAccessProfile = organization.RelationshipType == "ManagedServiceProvider"
                ? "PrivilegedOperatorControls"
                : "GuestCollaborationControls";
            var allowedResourceScope = organization.RelationshipType switch
            {
                "ManagedServiceProvider" => "Tier1AdminAndOperations",
                "Customer" => "CustomerCollaborationWorkspaces",
                _ => "PartnerCollaborationAndLOBApps"
            };
            var policy = new CrossTenantAccessPolicyRecord
            {
                Id = _idFactory.Next("XTP"),
                CompanyId = company.Id,
                ExternalOrganizationId = organization.Id,
                ResourceTenantDomain = resourceTenantDomain,
                HomeTenantDomain = homeTenantDomain,
                RelationshipType = organization.RelationshipType,
                PolicyName = $"{organization.Name} Cross-Tenant Access",
                AccessDirection = "Inbound",
                TrustLevel = trustLevel,
                DefaultAccess = defaultAccess,
                ConditionalAccessProfile = conditionalAccessProfile,
                AllowedResourceScope = allowedResourceScope,
                B2BCollaborationEnabled = true,
                InboundTrustMfa = true,
                InboundTrustCompliantDevice = organization.RelationshipType == "ManagedServiceProvider",
                AllowInvitations = true,
                EntitlementManagementEnabled = organization.RelationshipType != "Customer"
            };

            world.CrossTenantAccessPolicies.Add(policy);
            policyIdsByOrganization[organization.Id] = policy.Id;
        }

        foreach (var account in guestLikeAccounts)
        {
            if (string.IsNullOrWhiteSpace(account.InvitedOrganizationId))
            {
                continue;
            }

            policyIdsByOrganization.TryGetValue(account.InvitedOrganizationId, out var policyId);

            if (account.InvitationSentAt is not null)
            {
                AddCrossTenantAccessEvent(
                    world,
                    company.Id,
                    account,
                    policyId,
                    "Invitation",
                    "InvitationSent",
                    "Completed",
                    "Entra ID",
                    account.InvitationSentAt.Value,
                    resourceReference: account.UserPrincipalName);
                AddCrossTenantAccessEvent(
                    world,
                    company.Id,
                    account,
                    policyId,
                    "Governance",
                    "SponsorAssigned",
                    "Completed",
                    "Entra ID",
                    account.InvitationSentAt.Value,
                    resourceReference: account.UserPrincipalName);
                AddCrossTenantAccessEvent(
                    world,
                    company.Id,
                    account,
                    policyId,
                    "Entitlement",
                    "AccessPackageAssigned",
                    account.EntitlementAssignmentState ?? "Assigned",
                    "Entra Entitlement Management",
                    account.InvitationSentAt.Value.AddHours(2),
                    entitlementPackageName: account.EntitlementPackageName,
                    resourceReference: account.UserPrincipalName);
            }

            if (account.InvitationRedeemedAt is not null)
            {
                AddCrossTenantAccessEvent(
                    world,
                    company.Id,
                    account,
                    policyId,
                    "Invitation",
                    "InvitationRedeemed",
                    "Completed",
                    "Entra ID",
                    account.InvitationRedeemedAt.Value,
                    resourceReference: account.UserPrincipalName);
                AddCrossTenantAccessEvent(
                    world,
                    company.Id,
                    account,
                    policyId,
                    "Entitlement",
                    "AccessPackageActivated",
                    account.EntitlementAssignmentState ?? "ActiveAssignment",
                    "Entra Entitlement Management",
                    account.InvitationRedeemedAt.Value.AddHours(3),
                    entitlementPackageName: account.EntitlementPackageName,
                    resourceReference: account.UserPrincipalName);
            }

            if (account.SponsorLastChangedAt is not null)
            {
                AddCrossTenantAccessEvent(
                    world,
                    company.Id,
                    account,
                    policyId,
                    "Governance",
                    "SponsorChanged",
                    "Completed",
                    "Entra ID",
                    account.SponsorLastChangedAt.Value,
                    reviewDecision: account.PreviousInvitedByAccountId,
                    resourceReference: account.UserPrincipalName);
            }

            if (account.LastAccessReviewAt is not null)
            {
                AddCrossTenantAccessEvent(
                    world,
                    company.Id,
                    account,
                    policyId,
                    "Review",
                    "AccessReviewCompleted",
                    account.AccessReviewStatus ?? "Approved",
                    "Entra Access Reviews",
                    account.LastAccessReviewAt.Value,
                    entitlementPackageName: account.EntitlementPackageName,
                    reviewDecision: account.AccessReviewStatus,
                    resourceReference: account.UserPrincipalName);
            }

            if (account.AccessExpiresAt is not null)
            {
                var expiryStatus = account.AccessExpiresAt <= _clock.UtcNow ? "Expired" : "Scheduled";
                AddCrossTenantAccessEvent(
                    world,
                    company.Id,
                    account,
                    policyId,
                    "Lifecycle",
                    "AccessExpirationEvaluated",
                    expiryStatus,
                    "Entra Entitlement Management",
                    account.AccessExpiresAt.Value,
                    entitlementPackageName: account.EntitlementPackageName,
                    resourceReference: account.UserPrincipalName);
            }

            AddCrossTenantAccessEvent(
                world,
                company.Id,
                account,
                policyId,
                "Lifecycle",
                "AccessStateEvaluated",
                account.GuestLifecycleState ?? "Active",
                "CrossTenantAccessPolicy",
                account.InvitationRedeemedAt ?? account.InvitationSentAt ?? _clock.UtcNow,
                entitlementPackageName: account.EntitlementPackageName,
                resourceReference: account.UserPrincipalName);
        }
    }

    private void AddCrossTenantAccessEvent(
        SyntheticEnterpriseWorld world,
        string companyId,
        DirectoryAccount account,
        string? policyId,
        string category,
        string eventType,
        string eventStatus,
        string sourceSystem,
        DateTimeOffset eventAt,
        string? entitlementPackageName = null,
        string? reviewDecision = null,
        string? resourceReference = null)
    {
        world.CrossTenantAccessEvents.Add(new CrossTenantAccessEvent
        {
            Id = _idFactory.Next("XTE"),
            CompanyId = companyId,
            AccountId = account.Id,
            ExternalOrganizationId = account.InvitedOrganizationId ?? string.Empty,
            EventType = eventType,
            EventStatus = eventStatus,
            EventCategory = category,
            ActorAccountId = account.InvitedByAccountId,
            PolicyId = policyId,
            ResourceReference = resourceReference,
            EntitlementPackageName = entitlementPackageName,
            ReviewDecision = reviewDecision,
            SourceSystem = sourceSystem,
            EventAt = eventAt
        });
    }

    private DirectoryOrganizationalUnit CreateOu(
        Company company,
        string name,
        string? parentOuId,
        string distinguishedName,
        string purpose)
    {
        return new DirectoryOrganizationalUnit
        {
            Id = _idFactory.Next("OU"),
            CompanyId = company.Id,
            Name = name,
            ParentOuId = parentOuId,
            DistinguishedName = distinguishedName,
            Purpose = purpose
        };
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
        string? cloudTenantId,
        string? sourceEntityType = null,
        string? sourceEntityId = null)
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
            Name = name,
            PolicyType = policyType,
            Platform = platform,
            Category = category,
            Environment = "Production",
            Status = "Enabled",
            Description = description,
            IdentityStoreId = identityStoreId,
            CloudTenantId = cloudTenantId,
            SourceEntityType = sourceEntityType,
            SourceEntityId = sourceEntityId
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

        world.PolicySettings.Add(new PolicySettingRecord
        {
            Id = _idFactory.Next("PST"),
            CompanyId = companyId,
            PolicyId = policyId,
            SettingName = settingName,
            SettingCategory = settingCategory,
            ValueType = valueType,
            ConfiguredValue = configuredValue,
            IsLegacy = isLegacy,
            IsConflicting = isConflicting,
            SourceReference = sourceReference
        });
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
                && string.Equals(link.AssignmentMode, assignmentMode, StringComparison.OrdinalIgnoreCase)
                && string.Equals(link.FilterType, filterType, StringComparison.OrdinalIgnoreCase)
                && string.Equals(link.FilterValue, filterValue, StringComparison.OrdinalIgnoreCase)))
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

    private DirectoryGroup CreateGroup(
        Company company,
        string name,
        string groupType,
        string scope,
        bool mailEnabled,
        DirectoryOrganizationalUnit groupsOu,
        string purpose,
        string? administrativeTier = null)
    {
        return new DirectoryGroup
        {
            Id = _idFactory.Next("GRP"),
            CompanyId = company.Id,
            Name = name,
            GroupType = groupType,
            Scope = scope,
            MailEnabled = mailEnabled,
            DistinguishedName = $"CN={name},{groupsOu.DistinguishedName}",
            OuId = groupsOu.Id,
            Purpose = purpose,
            AdministrativeTier = administrativeTier
        };
    }

    private DirectoryGroupMembership CreateMembership(string groupId, string memberObjectId, string memberObjectType)
    {
        return new DirectoryGroupMembership
        {
            Id = _idFactory.Next("MEM"),
            GroupId = groupId,
            MemberObjectId = memberObjectId,
            MemberObjectType = memberObjectType
        };
    }

    private static DirectoryOrganizationalUnit? FindAdminTierOu(IReadOnlyList<DirectoryOrganizationalUnit> ous, string name)
        => ous.FirstOrDefault(ou => string.Equals(ou.Name, name, StringComparison.OrdinalIgnoreCase)
                                    && string.Equals(ou.Purpose, $"{name} Administrative Accounts", StringComparison.OrdinalIgnoreCase));

    private static DirectoryGroup? FindGroup(IReadOnlyList<DirectoryGroup> groups, string companyId, string name)
        => groups.FirstOrDefault(group => group.CompanyId == companyId && string.Equals(group.Name, name, StringComparison.OrdinalIgnoreCase));

    private static EnvironmentContainer? FindContainer(
        SyntheticEnterpriseWorld world,
        string companyId,
        string containerType,
        string? identityStoreId = null,
        string? name = null)
        => world.Containers.FirstOrDefault(container =>
            container.CompanyId == companyId
            && string.Equals(container.ContainerType, containerType, StringComparison.OrdinalIgnoreCase)
            && (identityStoreId is null || string.Equals(container.IdentityStoreId, identityStoreId, StringComparison.OrdinalIgnoreCase))
            && (name is null || string.Equals(container.Name, name, StringComparison.OrdinalIgnoreCase)));

    private static DirectoryGroup? FindDepartmentGroup(
        IReadOnlyList<DirectoryGroup> groups,
        IReadOnlyList<Department> departments,
        string departmentId)
    {
        var department = departments.FirstOrDefault(candidate => candidate.Id == departmentId);
        if (department is null)
        {
            return null;
        }

        return groups.FirstOrDefault(group =>
            string.Equals(group.Name, $"SG-{Slug(department.Name)}-Users", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<DirectoryGroup> ResolveAdminGroups(
        DirectoryAccount account,
        Person? person,
        DirectoryGroup? tier0,
        DirectoryGroup? tier1Server,
        DirectoryGroup? tier1Workstation,
        DirectoryGroup? tier2Helpdesk,
        DirectoryGroup? tier2AppSupport)
    {
        var departmentAndTitle = $"{person?.Title} {person?.DisplayName}";
        var indicators = $"{person?.Title} {person?.DepartmentId}".ToLowerInvariant();

        if (account.AdministrativeTier == "Tier0")
        {
            if (tier0 is not null)
            {
                yield return tier0;
            }

            yield break;
        }

        if (account.AdministrativeTier == "Tier1")
        {
            if (departmentAndTitle.Contains("Security", StringComparison.OrdinalIgnoreCase)
                || departmentAndTitle.Contains("Infrastructure", StringComparison.OrdinalIgnoreCase)
                || departmentAndTitle.Contains("Platform", StringComparison.OrdinalIgnoreCase)
                || departmentAndTitle.Contains("Identity", StringComparison.OrdinalIgnoreCase))
            {
                if (tier1Server is not null)
                {
                    yield return tier1Server;
                }
            }
            else if (tier1Workstation is not null)
            {
                yield return tier1Workstation;
            }

            yield break;
        }

        if (departmentAndTitle.Contains("Helpdesk", StringComparison.OrdinalIgnoreCase)
            || departmentAndTitle.Contains("Support", StringComparison.OrdinalIgnoreCase)
            || departmentAndTitle.Contains("Desktop", StringComparison.OrdinalIgnoreCase))
        {
            if (tier2Helpdesk is not null)
            {
                yield return tier2Helpdesk;
            }
        }
        else if (tier2AppSupport is not null)
        {
            yield return tier2AppSupport;
        }
    }

    private static string ResolveAdministrativeTier(Person person)
    {
        var text = $"{person.Title} {person.DisplayName}".ToLowerInvariant();
        return text switch
        {
            _ when text.Contains("chief") || text.Contains("ciso") || text.Contains("cio") => "Tier0",
            _ when text.Contains("security") || text.Contains("identity") || text.Contains("infrastructure") || text.Contains("platform") || text.Contains("server") => "Tier1",
            _ when text.Contains("helpdesk") || text.Contains("support") || text.Contains("desktop") || text.Contains("manager") => "Tier2",
            _ => "Tier2"
        };
    }

    private static string BuildRootDomain(Company company)
    {
        if (!string.IsNullOrWhiteSpace(company.PrimaryDomain))
        {
            return company.PrimaryDomain;
        }

        var normalized = new string(company.Name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? "example.test" : normalized + ".test";
    }

    private static string BuildNamingContext(string domain)
        => string.Join(",", domain
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => $"DC={part}"));

    private static string BuildSam(string firstName, string lastName, string employeeId)
    {
        var safeEmployeeId = string.IsNullOrWhiteSpace(employeeId) ? "000" : employeeId;
        var suffix = safeEmployeeId.Length >= 3 ? safeEmployeeId[^3..] : safeEmployeeId.PadLeft(3, '0');
        var baseValue = $"{Slug(firstName).FirstOrDefault()}{Slug(lastName)}";
        return Truncate($"{baseValue}{suffix}", 20);
    }

    private static string Slug(string value)
        => new string((value ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private static string Sanitize(string value)
        => Slug(value);

    private static string BuildExternalSam(Person person, string accountType)
    {
        var suffix = accountType switch
        {
            "ManagedServiceProvider" => "msp",
            "Guest" => "gst",
            _ => "ctr"
        };

        return Truncate($"{suffix}_{Slug(person.LastName)}", 20);
    }

    private static string BuildGuestUpnLocalPart(string firstName, string lastName, string homeTenantDomain)
    {
        var externalToken = homeTenantDomain.Replace(".", "_", StringComparison.OrdinalIgnoreCase);
        return $"{Slug(firstName)}_{Slug(lastName)}_{externalToken}#EXT#";
    }

    private static string BuildUniqueExternalPersonUpn(
        string firstName,
        string lastName,
        string employmentType,
        ExternalOrganization employer,
        string workerNumber,
        string rootDomain,
        ISet<string> issuedUpns)
    {
        var baseLocalPart = employmentType == "Guest"
            ? $"{Slug(firstName)}.{Slug(lastName)}.{Slug(workerNumber)}"
            : $"{Sanitize(firstName)}.{Sanitize(lastName)}.{employmentType.ToLowerInvariant()}.{Slug(workerNumber)}";
        var domain = employmentType == "Guest"
            ? $"{Slug(employer.Name)}.ext"
            : rootDomain;
        var candidate = $"{baseLocalPart}@{domain}";
        if (issuedUpns.Add(candidate))
        {
            return candidate;
        }

        for (var suffix = 2; suffix < 10000; suffix++)
        {
            candidate = $"{baseLocalPart}{suffix}@{domain}";
            if (issuedUpns.Add(candidate))
            {
                return candidate;
            }
        }

        return $"{Guid.NewGuid():N}@{domain}";
    }

    private static Office? SelectExternalOffice(
        IReadOnlyList<Office> offices,
        string employerCountry,
        string? sponsorOfficeId)
    {
        if (offices.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(sponsorOfficeId))
        {
            var sponsorOffice = offices.FirstOrDefault(office => office.Id == sponsorOfficeId);
            if (sponsorOffice is not null && (string.IsNullOrWhiteSpace(employerCountry) || string.Equals(sponsorOffice.Country, employerCountry, StringComparison.OrdinalIgnoreCase)))
            {
                return sponsorOffice;
            }
        }

        if (!string.IsNullOrWhiteSpace(employerCountry))
        {
            var countryMatch = offices.FirstOrDefault(office => string.Equals(office.Country, employerCountry, StringComparison.OrdinalIgnoreCase));
            if (countryMatch is not null)
            {
                return countryMatch;
            }
        }

        return !string.IsNullOrWhiteSpace(sponsorOfficeId)
            ? offices.FirstOrDefault(office => office.Id == sponsorOfficeId) ?? offices[0]
            : offices[0];
    }

    private static string ResolveExternalHomeTenantDomain(ExternalOrganization? organization)
    {
        if (organization is null || string.IsNullOrWhiteSpace(organization.Website))
        {
            return "external.partner.example";
        }

        if (Uri.TryCreate(organization.Website, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
        {
            return uri.Host;
        }

        return $"{Slug(organization.Name)}.example";
    }

    private static string EscapeDn(string value)
        => (value ?? string.Empty).Replace(",", "\\,");

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    private static bool ShouldRequirePasswordReset(string scopeKey, double threshold)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeKey);
        ArgumentOutOfRangeException.ThrowIfNegative(threshold);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(threshold, 1d);

        Span<byte> hashBytes = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(scopeKey), hashBytes);
        var bucket = BitConverter.ToUInt32(hashBytes[..4]);
        var normalized = bucket / (double)uint.MaxValue;
        return normalized < threshold;
    }

    private static string CreateUniquePassword(HashSet<string> issuedPasswords, int length = 16)
    {
        var targetLength = Math.Max(12, length);
        while (true)
        {
            var password = CreateSecurePassword(targetLength);
            if (issuedPasswords.Add(password))
            {
                return password;
            }
        }
    }

    private static string CreateSecurePassword(int length)
    {
        var buffer = new List<char>(length)
        {
            LowercaseChars[RandomNumberGenerator.GetInt32(LowercaseChars.Length)],
            UppercaseChars[RandomNumberGenerator.GetInt32(UppercaseChars.Length)],
            DigitChars[RandomNumberGenerator.GetInt32(DigitChars.Length)],
            SymbolChars[RandomNumberGenerator.GetInt32(SymbolChars.Length)]
        };

        while (buffer.Count < length)
        {
            buffer.Add(AllPasswordChars[RandomNumberGenerator.GetInt32(AllPasswordChars.Length)]);
        }

        for (var i = buffer.Count - 1; i > 0; i--)
        {
            var swapIndex = RandomNumberGenerator.GetInt32(i + 1);
            (buffer[i], buffer[swapIndex]) = (buffer[swapIndex], buffer[i]);
        }

        return new string(buffer.ToArray());
    }
}
