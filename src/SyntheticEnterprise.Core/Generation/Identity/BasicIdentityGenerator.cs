namespace SyntheticEnterprise.Core.Generation.Identity;

using System.Security.Cryptography;
using System.Text;
using System.Globalization;
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
            var companyOffices = world.Offices.Where(office => office.CompanyId == company.Id).ToList();
            var rootDomain = BuildRootDomain(company);
            var issuedPasswords = new HashSet<string>(StringComparer.Ordinal);
            var issuedAccountUpns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var issuedSamAccountNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var includeAdministrativeTiers = context.Scenario.Identity.IncludeAdministrativeTiers;

            var identityStores = CreateIdentityStores(company, rootDomain, context.Scenario.Identity);
            world.IdentityStores.AddRange(identityStores);

            var ous = CreateOus(company, companyDepartments, companyOffices, rootDomain, includeAdministrativeTiers);
            world.OrganizationalUnits.AddRange(ous);
            world.Containers.AddRange(CreateDirectoryContainers(company, identityStores, ous));

            var peopleAccounts = CreateUserAccounts(company, companyPeople, companyDepartments, ous, rootDomain, issuedPasswords, issuedSamAccountNames);
            world.Accounts.AddRange(peopleAccounts);
            foreach (var upn in peopleAccounts
                         .Select(account => account.UserPrincipalName)
                         .Where(upn => !string.IsNullOrWhiteSpace(upn)))
            {
                issuedAccountUpns.Add(upn!);
            }

            SetManagerRelationships(world, company, companyPeople, peopleAccounts);

            var serviceAccounts = CreateServiceAccounts(company, companyDefinition, ous, rootDomain, issuedPasswords, issuedAccountUpns, issuedSamAccountNames, includeAdministrativeTiers);
            world.Accounts.AddRange(serviceAccounts);

            var sharedAccounts = CreateSharedMailboxes(company, companyDefinition, ous, rootDomain, issuedPasswords, issuedAccountUpns, issuedSamAccountNames);
            world.Accounts.AddRange(sharedAccounts);

            if (companyDefinition.IncludePrivilegedAccounts)
            {
                var privileged = CreatePrivilegedAccounts(company, companyPeople, ous, rootDomain, issuedPasswords, issuedAccountUpns, issuedSamAccountNames, includeAdministrativeTiers);
                world.Accounts.AddRange(privileged);
            }

            var groups = CreateGroups(company, companyDepartments, companyTeams, world.Accounts, ous, includeAdministrativeTiers);
            world.Groups.AddRange(groups);

            var memberships = CreateMemberships(company, companyDepartments, companyTeams, companyPeople, groups, world.Accounts, includeAdministrativeTiers);
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
                    rootDomain,
                    catalogs);
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
                        issuedAccountUpns,
                        issuedSamAccountNames,
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
                Name = rootDomain,
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
        var allEmployeesGroup = FindGroup(world.Groups, company.Id, AllEmployeesSecurityGroupName());
        var guestGroup = FindGroup(world.Groups, company.Id, B2BGuestsGroupName());
        var workstationAdmins = FindGroup(world.Groups, company.Id, Tier1WorkstationAdminsGroupName());
        var serverAdmins = FindGroup(world.Groups, company.Id, Tier1ServerAdminsGroupName());
        var pawUsers = FindGroup(world.Groups, company.Id, Tier0PawUsersGroupName());
        var allManagersGroup = FindGroup(world.Groups, company.Id, AllManagersDistributionGroupName());
        var gpoEditors = FindGroup(world.Groups, company.Id, GroupPolicyEditorsGroupName());
        var lapsReaders = FindGroup(world.Groups, company.Id, LapsReadersGroupName());
        var passwordResetOperators = FindGroup(world.Groups, company.Id, PasswordResetOperatorsGroupName());
        var workstationJoiners = FindGroup(world.Groups, company.Id, WorkstationJoinOperatorsGroupName());
        var remoteSupportOperators = FindGroup(world.Groups, company.Id, RemoteSupportOperatorsGroupName());
        var officeUsers = FindGroup(world.Groups, company.Id, OfficeUsersGroupName());
        var officeAdmins = FindGroup(world.Groups, company.Id, OfficeAdminsGroupName());
        var browserPilotUsers = FindGroup(world.Groups, company.Id, BrowserPilotUsersGroupName());
        var vpnUsers = FindGroup(world.Groups, company.Id, VpnUsersGroupName());
        var serverRdpUsers = FindGroup(world.Groups, company.Id, ServerRemoteDesktopUsersGroupName());

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
        AddPolicySetting(world, company.Id, defaultDomainPolicy.Id, "MaximumPasswordAgeDays", "PasswordPolicy", "Integer", "90");
        AddPolicySetting(world, company.Id, defaultDomainPolicy.Id, "MinimumPasswordAgeDays", "PasswordPolicy", "Integer", "1");
        AddPolicySetting(world, company.Id, defaultDomainPolicy.Id, "KerberosMaxTicketAgeHours", "Authentication", "Integer", "10");
        AddPolicySetting(world, company.Id, defaultDomainPolicy.Id, "KerberosMaxServiceTicketAgeMinutes", "Authentication", "Integer", "600");
        AddPolicySetting(world, company.Id, defaultDomainPolicy.Id, "AuditDirectoryServiceChanges", "AuditPolicy", "String", "Success,Failure");
        AddPolicySetting(world, company.Id, defaultDomainPolicy.Id, "LanManCompatibilityLevel", "LegacyAuthentication", "String", "NTLMv2Only", isLegacy: true, sourceReference: "Commonly retained legacy hardening knob");
        AddPolicyTarget(world, company.Id, defaultDomainPolicy.Id, "Container", domainContainer?.Id, "Linked", true, 1, true);
        AddPolicyTarget(world, company.Id, defaultDomainPolicy.Id, "IdentityStore", activeDirectoryStore.Id, "Scope", false, 1);
        AddPolicyTarget(world, company.Id, defaultDomainPolicy.Id, "Group", gpoEditors?.Id, "DelegatedAdministration", false, 1, true, "Permission", "EditSettings");

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
        AddPolicySetting(world, company.Id, workstationPolicy.Id, "MicrosoftDefenderRealtimeMonitoring", "EndpointProtection", "Boolean", "true");
        AddPolicySetting(world, company.Id, workstationPolicy.Id, "CredentialGuardEnabled", "CredentialProtection", "Boolean", "true");
        AddPolicySetting(world, company.Id, workstationPolicy.Id, "LapsManagedLocalAdministrator", "CredentialProtection", "Boolean", "true");
        AddPolicySetting(world, company.Id, workstationPolicy.Id, "BitLockerStartupMode", "DiskEncryption", "String", "TpmOnly");
        AddPolicySetting(world, company.Id, workstationPolicy.Id, "AllowLocalAdminRemoteUacExemption", "AdministrativeAccess", "Boolean", "false");
        AddPolicySetting(world, company.Id, workstationPolicy.Id, "UsbStorageAccess", "DeviceControl", "String", "ReadWriteApprovedOnly");
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
        AddPolicySetting(world, company.Id, serverPolicy.Id, "WindowsDefenderAntivirusEnabled", "EndpointProtection", "Boolean", "true");
        AddPolicySetting(world, company.Id, serverPolicy.Id, "RdpNlaRequired", "RemoteAccess", "Boolean", "true");
        AddPolicySetting(world, company.Id, serverPolicy.Id, "DisableAnonymousShares", "NetworkSecurity", "Boolean", "true");
        AddPolicySetting(world, company.Id, serverPolicy.Id, "AuditProcessCreation", "AuditPolicy", "String", "Success");
        AddPolicySetting(world, company.Id, serverPolicy.Id, "PowerShellTranscription", "AuditPolicy", "Boolean", "true");
        AddPolicyTarget(world, company.Id, serverPolicy.Id, "Container", serverContainer?.Id, "Linked", true, 1, true);
        AddPolicyTarget(world, company.Id, serverPolicy.Id, "Group", serverAdmins?.Id, "DelegatedAdministration", false, 1, true, "Permission", "EditSettings");
        AddPolicyTarget(world, company.Id, serverPolicy.Id, "Group", serverRdpUsers?.Id, "SecurityFilterInclude", false, 2);
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
            null,
            status: "Disabled");
        AddPolicySetting(world, company.Id, legacyBrowserPolicy.Id, "InternetExplorerModeSiteList", "LegacyCompatibility", "String", "LegacyLineOfBusinessApps", isLegacy: true, sourceReference: "Typical transitional browser-compatibility holdover");
        AddPolicySetting(world, company.Id, legacyBrowserPolicy.Id, "TrustedSitesZoneAssignments", "LegacyCompatibility", "String", "LegacyVendorPortal", isLegacy: true);
        AddPolicyTarget(world, company.Id, legacyBrowserPolicy.Id, "Container", workstationContainer?.Id, "Linked", false, 2, false);
        AddPolicyTarget(world, company.Id, legacyBrowserPolicy.Id, "Group", allEmployeesGroup?.Id, "SecurityFilterInclude", false, 1);
        AddPolicyTarget(world, company.Id, legacyBrowserPolicy.Id, "Container", workstationContainer?.Id, "WmiFilter", false, 2, true, "WmiQuery", "SELECT * FROM Win32_OperatingSystem WHERE ProductType = 1 AND Version LIKE '10.%'");
        AddPolicyTarget(world, company.Id, legacyBrowserPolicy.Id, "Group", workstationAdmins?.Id, "DelegatedAdministration", false, 1, true, "Permission", "EditPermissions");
        AddAccessControlEvidence(world, company.Id, workstationAdmins?.Id, "Group", "Policy", legacyBrowserPolicy.Id, "EditPermissions", "Allow", false, "ActiveDirectory");
        AddAccessControlEvidence(world, company.Id, workstationAdmins?.Id, "Group", "Policy", legacyBrowserPolicy.Id, "ReadPolicy", "Allow", false, "ActiveDirectory");

        var desktopExperiencePolicy = EnsurePolicy(
            world,
            company.Id,
            "Corporate Desktop Experience",
            "GroupPolicyObject",
            "ActiveDirectory",
            "UserExperience",
            "User environment branding, lock screen, and shell behavior.",
            activeDirectoryStore.Id,
            null);
        AddPolicySetting(world, company.Id, desktopExperiencePolicy.Id, "DesktopWallpaperPath", "Branding", "String", $"\\\\files.{company.PrimaryDomain}\\corp\\branding\\wallpaper.jpg");
        AddPolicySetting(world, company.Id, desktopExperiencePolicy.Id, "LockScreenImagePath", "Branding", "String", $"\\\\files.{company.PrimaryDomain}\\corp\\branding\\lockscreen.jpg");
        AddPolicySetting(world, company.Id, desktopExperiencePolicy.Id, "StartMenuLayout", "Shell", "String", "CorpStandardProductivity");
        AddPolicySetting(world, company.Id, desktopExperiencePolicy.Id, "TaskbarPinnedApps", "Shell", "String", "Outlook;Teams;Edge;ERP Portal");
        AddPolicySetting(world, company.Id, desktopExperiencePolicy.Id, "HideConsumerExperience", "Shell", "Boolean", "true");
        AddPolicyTarget(world, company.Id, desktopExperiencePolicy.Id, "Container", workstationContainer?.Id, "Linked", false, 3, true);
        AddPolicyTarget(world, company.Id, desktopExperiencePolicy.Id, "Group", allEmployeesGroup?.Id, "SecurityFilterInclude", false, 1);
        AddPolicyTarget(world, company.Id, desktopExperiencePolicy.Id, "Group", gpoEditors?.Id, "DelegatedAdministration", false, 1, true, "Permission", "EditSettings");

        var browserPolicy = EnsurePolicy(
            world,
            company.Id,
            "Enterprise Browser Controls",
            "GroupPolicyObject",
            "ActiveDirectory",
            "BrowserSecurity",
            "Enterprise browser hardening, proxy, and compatibility controls.",
            activeDirectoryStore.Id,
            null);
        AddPolicySetting(world, company.Id, browserPolicy.Id, "BrowserHomepage", "BrowserConfiguration", "String", $"https://intranet.{company.PrimaryDomain}");
        AddPolicySetting(world, company.Id, browserPolicy.Id, "BrowserProxyMode", "BrowserConfiguration", "String", "AutoDetect");
        AddPolicySetting(world, company.Id, browserPolicy.Id, "BrowserPasswordManagerAllowed", "BrowserSecurity", "Boolean", "false");
        AddPolicySetting(world, company.Id, browserPolicy.Id, "BrowserExtensionAllowList", "BrowserSecurity", "String", "CorpPasswordManager;SSOHelper;EndpointIsolation");
        AddPolicySetting(world, company.Id, browserPolicy.Id, "SmartScreenEnforced", "BrowserSecurity", "Boolean", "true");
        AddPolicyTarget(world, company.Id, browserPolicy.Id, "Container", workstationContainer?.Id, "Linked", false, 4, true);
        AddPolicyTarget(world, company.Id, browserPolicy.Id, "Group", allEmployeesGroup?.Id, "SecurityFilterInclude", false, 1);
        AddPolicyTarget(world, company.Id, browserPolicy.Id, "Group", browserPilotUsers?.Id, "SecurityFilterInclude", false, 2);
        AddPolicyTarget(world, company.Id, browserPolicy.Id, "Group", gpoEditors?.Id, "DelegatedAdministration", false, 1, true, "Permission", "EditSettings");

        var logonPolicy = EnsurePolicy(
            world,
            company.Id,
            "User Logon and Drive Mapping",
            "GroupPolicyObject",
            "ActiveDirectory",
            "UserEnvironment",
            "Maps drives, printers, and logon scripts for employee productivity.",
            activeDirectoryStore.Id,
            null);
        AddPolicySetting(world, company.Id, logonPolicy.Id, "PrimaryLogonScript", "LogonScripts", "String", "\\\\netlogon\\corp\\logon.cmd");
        AddPolicySetting(world, company.Id, logonPolicy.Id, "MapHomeDrive", "DriveMappings", "String", "H:=\\\\files\\home\\%USERNAME%");
        AddPolicySetting(world, company.Id, logonPolicy.Id, "MapDepartmentDrive", "DriveMappings", "String", "S:=\\\\files\\shares\\department");
        AddPolicySetting(world, company.Id, logonPolicy.Id, "DeployFollowMePrinters", "Printers", "Boolean", "true");
        AddPolicySetting(world, company.Id, logonPolicy.Id, "MapCorpAppsShortcut", "Shortcuts", "String", "\\\\files\\corp\\links\\Business Apps.lnk");
        AddPolicyTarget(world, company.Id, logonPolicy.Id, "Container", workstationContainer?.Id, "Linked", false, 5, true);
        AddPolicyTarget(world, company.Id, logonPolicy.Id, "Group", allEmployeesGroup?.Id, "SecurityFilterInclude", false, 1);
        AddPolicyTarget(world, company.Id, logonPolicy.Id, "Group", guestGroup?.Id, "SecurityFilterExclude", false, 2);
        AddPolicyTarget(world, company.Id, logonPolicy.Id, "Group", remoteSupportOperators?.Id, "DelegatedAdministration", false, 1, true, "Permission", "EditSettings");

        var officePolicy = EnsurePolicy(
            world,
            company.Id,
            "Office Productivity Controls",
            "GroupPolicyObject",
            "ActiveDirectory",
            "ApplicationSecurity",
            "Standardized Microsoft Office hardening and collaboration defaults.",
            activeDirectoryStore.Id,
            null);
        AddPolicySetting(world, company.Id, officePolicy.Id, "OfficeMacroPolicy", "OfficeSecurity", "String", "BlockInternetMacros");
        AddPolicySetting(world, company.Id, officePolicy.Id, "OfficeTrustedLocations", "OfficeSecurity", "String", "\\\\files\\templates;\\\\files\\finance\\models");
        AddPolicySetting(world, company.Id, officePolicy.Id, "OfficeDefaultSaveFormat", "OfficeConfiguration", "String", "OpenXml");
        AddPolicySetting(world, company.Id, officePolicy.Id, "OfficeConnectedExperiences", "OfficePrivacy", "String", "Limited");
        AddPolicySetting(world, company.Id, officePolicy.Id, "OfficeTelemetryLevel", "OfficeConfiguration", "String", "Required");
        AddPolicyTarget(world, company.Id, officePolicy.Id, "Container", workstationContainer?.Id, "Linked", false, 6, true);
        AddPolicyTarget(world, company.Id, officePolicy.Id, "Group", officeUsers?.Id ?? allEmployeesGroup?.Id, "SecurityFilterInclude", false, 1);
        AddPolicyTarget(world, company.Id, officePolicy.Id, "Group", officeAdmins?.Id ?? gpoEditors?.Id, "DelegatedAdministration", false, 1, true, "Permission", "EditSettings");

        var updatePolicy = EnsurePolicy(
            world,
            company.Id,
            "Windows Update Enterprise Ring",
            "GroupPolicyObject",
            "ActiveDirectory",
            "PatchManagement",
            "Enterprise update cadence for managed Windows endpoints.",
            activeDirectoryStore.Id,
            null);
        AddPolicySetting(world, company.Id, updatePolicy.Id, "WindowsUpdateDeferralDays", "PatchManagement", "Integer", "7");
        AddPolicySetting(world, company.Id, updatePolicy.Id, "FeatureUpdateDeferralDays", "PatchManagement", "Integer", "30");
        AddPolicySetting(world, company.Id, updatePolicy.Id, "AutoRestartOutsideActiveHours", "PatchManagement", "Boolean", "true");
        AddPolicySetting(world, company.Id, updatePolicy.Id, "ActiveHoursWindow", "PatchManagement", "String", "07:00-19:00");
        AddPolicySetting(world, company.Id, updatePolicy.Id, "WsusServer", "PatchManagement", "String", $"https://wsus.{company.PrimaryDomain}");
        AddPolicyTarget(world, company.Id, updatePolicy.Id, "Container", workstationContainer?.Id, "Linked", false, 7, true);
        AddPolicyTarget(world, company.Id, updatePolicy.Id, "Container", serverContainer?.Id, "Linked", false, 8, true);
        AddPolicyTarget(world, company.Id, updatePolicy.Id, "Group", workstationAdmins?.Id, "DelegatedAdministration", false, 1, true, "Permission", "EditSettings");

        var remoteAccessPolicy = EnsurePolicy(
            world,
            company.Id,
            "Remote Access and VPN Controls",
            "GroupPolicyObject",
            "ActiveDirectory",
            "RemoteAccess",
            "VPN, remote support, and remote desktop controls for managed systems.",
            activeDirectoryStore.Id,
            null);
        AddPolicySetting(world, company.Id, remoteAccessPolicy.Id, "AlwaysOnVpnProfile", "RemoteAccess", "String", "Corp-Production");
        AddPolicySetting(world, company.Id, remoteAccessPolicy.Id, "VpnTunnelRequiresMfa", "RemoteAccess", "Boolean", "true");
        AddPolicySetting(world, company.Id, remoteAccessPolicy.Id, "RemoteAssistanceEnabled", "RemoteSupport", "Boolean", "true");
        AddPolicySetting(world, company.Id, remoteAccessPolicy.Id, "RemoteDesktopUserModePromptForCreds", "RemoteAccess", "Boolean", "true");
        AddPolicySetting(world, company.Id, remoteAccessPolicy.Id, "RemoteDesktopIdleTimeoutMinutes", "RemoteAccess", "Integer", "60");
        AddPolicyTarget(world, company.Id, remoteAccessPolicy.Id, "Container", workstationContainer?.Id, "Linked", false, 9, true);
        AddPolicyTarget(world, company.Id, remoteAccessPolicy.Id, "Group", vpnUsers?.Id ?? allEmployeesGroup?.Id, "SecurityFilterInclude", false, 1);
        AddPolicyTarget(world, company.Id, remoteAccessPolicy.Id, "Group", remoteSupportOperators?.Id, "DelegatedAdministration", false, 1, true, "Permission", "EditSettings");

        var securityAuditPolicy = EnsurePolicy(
            world,
            company.Id,
            "Security Audit and Logging Baseline",
            "GroupPolicyObject",
            "ActiveDirectory",
            "AuditPolicy",
            "Security telemetry and audit policy defaults across managed systems.",
            activeDirectoryStore.Id,
            null);
        AddPolicySetting(world, company.Id, securityAuditPolicy.Id, "AuditLogonEvents", "AuditPolicy", "String", "Success,Failure");
        AddPolicySetting(world, company.Id, securityAuditPolicy.Id, "AuditAccountManagement", "AuditPolicy", "String", "Success,Failure");
        AddPolicySetting(world, company.Id, securityAuditPolicy.Id, "AuditPolicyChange", "AuditPolicy", "String", "Success,Failure");
        AddPolicySetting(world, company.Id, securityAuditPolicy.Id, "AuditObjectAccess", "AuditPolicy", "String", "Success");
        AddPolicySetting(world, company.Id, securityAuditPolicy.Id, "SecurityEventLogMaxSizeMb", "Logging", "Integer", "1024");
        AddPolicyTarget(world, company.Id, securityAuditPolicy.Id, "Container", workstationContainer?.Id, "Linked", false, 10, true);
        AddPolicyTarget(world, company.Id, securityAuditPolicy.Id, "Container", serverContainer?.Id, "Linked", false, 11, true);
        AddPolicyTarget(world, company.Id, securityAuditPolicy.Id, "Group", serverAdmins?.Id, "DelegatedAdministration", false, 1, true, "Permission", "EditSettings");

        var deviceControlPolicy = EnsurePolicy(
            world,
            company.Id,
            "Removable Media and Device Control",
            "GroupPolicyObject",
            "ActiveDirectory",
            "DeviceControl",
            "Controls removable storage, printer redirection, and peripheral trust.",
            activeDirectoryStore.Id,
            null);
        AddPolicySetting(world, company.Id, deviceControlPolicy.Id, "UsbStorageReadPolicy", "DeviceControl", "String", "ApprovedOnly");
        AddPolicySetting(world, company.Id, deviceControlPolicy.Id, "PrinterRedirectionAllowed", "DeviceControl", "Boolean", "false");
        AddPolicySetting(world, company.Id, deviceControlPolicy.Id, "BluetoothPeripheralPairing", "DeviceControl", "String", "UserApproved");
        AddPolicySetting(world, company.Id, deviceControlPolicy.Id, "CameraAccess", "DeviceControl", "String", "AllowedWithConsent");
        AddPolicySetting(world, company.Id, deviceControlPolicy.Id, "ClipboardRedirectionForRemoteSessions", "DeviceControl", "Boolean", "false");
        AddPolicyTarget(world, company.Id, deviceControlPolicy.Id, "Container", workstationContainer?.Id, "Linked", false, 12, true);
        AddPolicyTarget(world, company.Id, deviceControlPolicy.Id, "Group", allEmployeesGroup?.Id, "SecurityFilterInclude", false, 1);
        AddPolicyTarget(world, company.Id, deviceControlPolicy.Id, "Group", guestGroup?.Id, "SecurityFilterExclude", false, 2);

        var privilegedOpsPolicy = EnsurePolicy(
            world,
            company.Id,
            "Delegated Administration Controls",
            "GroupPolicyObject",
            "ActiveDirectory",
            "PrivilegedAccess",
            "Delegation and privileged endpoint operations baseline.",
            activeDirectoryStore.Id,
            null);
        AddPolicySetting(world, company.Id, privilegedOpsPolicy.Id, "AllowWorkstationJoinDelegation", "Delegation", "Boolean", "true");
        AddPolicySetting(world, company.Id, privilegedOpsPolicy.Id, "LapsPasswordReadScope", "Delegation", "String", "Tier1AndHelpdesk");
        AddPolicySetting(world, company.Id, privilegedOpsPolicy.Id, "PasswordResetWorkflow", "Delegation", "String", "HelpdeskWithApproval");
        AddPolicySetting(world, company.Id, privilegedOpsPolicy.Id, "RestrictedAdminMode", "PrivilegedAccess", "Boolean", "true");
        AddPolicySetting(world, company.Id, privilegedOpsPolicy.Id, "PrivilegedSessionBanner", "PrivilegedAccess", "String", "Authorized administrative use only");
        AddPolicyTarget(world, company.Id, privilegedOpsPolicy.Id, "Container", workstationContainer?.Id, "Linked", true, 13, true);
        AddPolicyTarget(world, company.Id, privilegedOpsPolicy.Id, "Container", serverContainer?.Id, "Linked", true, 14, true);
        AddPolicyTarget(world, company.Id, privilegedOpsPolicy.Id, "Group", gpoEditors?.Id, "DelegatedAdministration", false, 1, true, "Permission", "EditSettings");
        AddAccessControlEvidence(world, company.Id, gpoEditors?.Id, "Group", "Policy", privilegedOpsPolicy.Id, "EditSettings", "Allow", false, "ActiveDirectory");
        AddAccessControlEvidence(world, company.Id, lapsReaders?.Id, "Group", "Container", workstationContainer?.Id, "ReadLapsPassword", "Allow", false, "ActiveDirectory");
        AddAccessControlEvidence(world, company.Id, passwordResetOperators?.Id, "Group", "Container", workstationContainer?.Id, "ResetPassword", "Allow", false, "ActiveDirectory");
        AddAccessControlEvidence(world, company.Id, workstationJoiners?.Id, "Group", "Container", workstationContainer?.Id, "CreateComputerObject", "Allow", false, "ActiveDirectory");
        AddAccessControlEvidence(world, company.Id, remoteSupportOperators?.Id, "Group", "Container", workstationContainer?.Id, "RemoteAssist", "Allow", false, "ActiveDirectory");
        AddAccessControlEvidence(world, company.Id, gpoEditors?.Id, "Group", "Policy", defaultDomainPolicy.Id, "EditSettings", "Allow", false, "ActiveDirectory");

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
        var tier0Admins = FindGroup(world.Groups, company.Id, Tier0IdentityAdminsGroupName());
            AddAccessControlEvidence(world, company.Id, pawUsers?.Id, "Group", "Container", pawContainer.Id, "ApplyGroupPolicy", "Allow", false, "ActiveDirectory");
            AddAccessControlEvidence(world, company.Id, tier0Admins?.Id, "Group", "Policy", pawPolicy.Id, "EditSettings", "Allow", false, "ActiveDirectory");
            AddAccessControlEvidence(world, company.Id, tier0Admins?.Id, "Group", "Container", pawContainer.Id, "BlockInheritance", "Allow", false, "ActiveDirectory", notes: "Privileged access OU with explicit inheritance block");
            AddAccessControlEvidence(world, company.Id, tier0Admins?.Id, "Group", "Container", pawContainer.Id, "ResetPassword", "Allow", false, "ActiveDirectory", notes: "Tier-0 delegated recovery on privileged workstation accounts");
        }

        CreateLocationScopedPolicies(world, company, activeDirectoryStore.Id, gpoEditors?.Id);
        CreateDepartmentScopedPolicies(world, company, activeDirectoryStore.Id, gpoEditors?.Id);
        CreateServerRolePolicies(world, company, activeDirectoryStore.Id, serverAdmins?.Id);
        CreateModernManagementPolicies(world, company, activeDirectoryStore, allEmployeesGroup?.Id, guestGroup?.Id, officeUsers?.Id, workstationAdmins?.Id);
        CreateWindowsBenchmarkPolicies(world, company, activeDirectoryStore.Id, workstationContainer?.Id, serverContainer?.Id, gpoEditors?.Id, workstationAdmins?.Id, serverAdmins?.Id);
        CreateBrowserAndOfficeBenchmarkPolicies(world, company, activeDirectoryStore.Id, workstationContainer?.Id, allEmployeesGroup?.Id, browserPilotUsers?.Id, officeUsers?.Id, officeAdmins?.Id ?? gpoEditors?.Id);
    }

    private void CreateCrossTenantPolicyObjects(SyntheticEnterpriseWorld world, Company company)
    {
        var entraStore = world.IdentityStores.FirstOrDefault(store =>
            store.CompanyId == company.Id
            && string.Equals(store.StoreType, "EntraTenant", StringComparison.OrdinalIgnoreCase));
        var guestGroup = FindGroup(world.Groups, company.Id, B2BGuestsGroupName());
        var guestCollaborationGroup = FindGroup(world.Groups, company.Id, "M365 Guest Collaboration");

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

    private void CreateLocationScopedPolicies(
        SyntheticEnterpriseWorld world,
        Company company,
        string activeDirectoryStoreId,
        string? gpoEditorsGroupId)
    {
        foreach (var office in world.Offices.Where(office => office.CompanyId == company.Id))
        {
            var locationWorkstationContainer = FindContainer(world, company.Id, "OrganizationalUnit", activeDirectoryStoreId, office.City);
            var locationUserContainer = world.Containers.FirstOrDefault(container =>
                container.CompanyId == company.Id
                && string.Equals(container.ContainerType, "OrganizationalUnit", StringComparison.OrdinalIgnoreCase)
                && string.Equals(container.Name, office.City, StringComparison.OrdinalIgnoreCase)
                && string.Equals(container.Purpose, "Location Users", StringComparison.OrdinalIgnoreCase));
            if (locationWorkstationContainer is null && locationUserContainer is null)
            {
                continue;
            }

            var desktopPolicy = EnsurePolicy(
                world,
                company.Id,
                $"Desktop Experience - {office.City}",
                "GroupPolicyObject",
                "ActiveDirectory",
                "UserExperience",
                $"Location-specific desktop experience and shell defaults for {office.City}.",
                activeDirectoryStoreId,
                null);
            AddSettingSet(world, company.Id, desktopPolicy.Id,
            [
                ("DesktopWallpaperPath", "Branding", "String", $"\\\\files.{company.PrimaryDomain}\\branding\\{Slug(office.City)}\\wallpaper.jpg"),
                ("LockScreenImagePath", "Branding", "String", $"\\\\files.{company.PrimaryDomain}\\branding\\{Slug(office.City)}\\lockscreen.jpg"),
                ("DefaultTimeZone", "Regionalization", "String", office.TimeZone),
                ("RegionalLocale", "Regionalization", "String", ResolveRegionalLocale(office)),
                ("DefaultOfficeTemplateLibrary", "Productivity", "String", $"\\\\files.{company.PrimaryDomain}\\templates\\{Slug(office.City)}"),
                ("DefaultPrinterSearchScope", "Printing", "String", office.City),
                ("IntranetLandingPage", "BrowserConfiguration", "String", $"https://intranet.{company.PrimaryDomain}/{Slug(office.City)}"),
                ("SupportContactBanner", "Supportability", "String", $"{office.City} IT Service Desk"),
                ("LocalEmergencyNoticeUrl", "Communications", "String", $"https://intranet.{company.PrimaryDomain}/sites/{Slug(office.City)}-operations"),
                ("CorporateSsoRegionHint", "Authentication", "String", office.Country)
            ]);
            AddPolicyTarget(world, company.Id, desktopPolicy.Id, "Container", locationWorkstationContainer?.Id, "Linked", true, 20, true);
            AddPolicyTarget(world, company.Id, desktopPolicy.Id, "Container", locationUserContainer?.Id, "Linked", false, 21, true);
            AddPolicyTarget(world, company.Id, desktopPolicy.Id, "Group", gpoEditorsGroupId, "DelegatedAdministration", false, 1, true, "Permission", "EditSettings");

            var accessPolicy = EnsurePolicy(
                world,
                company.Id,
                $"Branch Access and Drives - {office.City}",
                "GroupPolicyObject",
                "ActiveDirectory",
                "UserEnvironment",
                $"Drive mapping, printer, and branch access defaults for {office.City}.",
                activeDirectoryStoreId,
                null);
            AddSettingSet(world, company.Id, accessPolicy.Id,
            [
                ("PrimaryHomeDrive", "DriveMappings", "String", $"H:=\\\\files.{company.PrimaryDomain}\\home\\%USERNAME%"),
                ("SharedLocationDrive", "DriveMappings", "String", $"L:=\\\\files.{company.PrimaryDomain}\\sites\\{Slug(office.City)}"),
                ("DepartmentLandingShortcut", "Shortcuts", "String", $"\\\\files.{company.PrimaryDomain}\\links\\{Slug(office.City)} Business Apps.lnk"),
                ("DefaultFollowMeQueue", "Printers", "String", $"{office.City}-FollowMe"),
                ("DefaultColorQueue", "Printers", "String", $"{office.City}-Color"),
                ("TrustedWifiProfile", "NetworkAccess", "String", $"{office.City}-CorpWiFi"),
                ("PreferredVpnExitRegion", "RemoteAccess", "String", office.Country),
                ("PeerCacheGroup", "DeliveryOptimization", "String", $"{office.City}-Office"),
                ("OnsiteSupportPhone", "Supportability", "String", $"{office.City} Support Line"),
                ("OfficePresenceCalendar", "Collaboration", "String", $"{office.City} Occupancy Calendar")
            ]);
            AddPolicyTarget(world, company.Id, accessPolicy.Id, "Container", locationWorkstationContainer?.Id, "Linked", true, 22, true);
            AddPolicyTarget(world, company.Id, accessPolicy.Id, "Container", locationUserContainer?.Id, "Linked", false, 23, true);
            AddPolicyTarget(world, company.Id, accessPolicy.Id, "Group", gpoEditorsGroupId, "DelegatedAdministration", false, 1, true, "Permission", "EditSettings");
        }
    }

    private void CreateDepartmentScopedPolicies(
        SyntheticEnterpriseWorld world,
        Company company,
        string activeDirectoryStoreId,
        string? gpoEditorsGroupId)
    {
        var entraStore = world.IdentityStores.FirstOrDefault(store =>
            store.CompanyId == company.Id
            && string.Equals(store.StoreType, "EntraTenant", StringComparison.OrdinalIgnoreCase));

        foreach (var department in world.Departments.Where(department => department.CompanyId == company.Id))
        {
            var departmentContainer = world.Containers.FirstOrDefault(container =>
                container.CompanyId == company.Id
                && string.Equals(container.ContainerType, "OrganizationalUnit", StringComparison.OrdinalIgnoreCase)
                && string.Equals(container.Name, department.Name, StringComparison.OrdinalIgnoreCase)
                && string.Equals(container.Purpose, "Department Users", StringComparison.OrdinalIgnoreCase));
            if (departmentContainer is null)
            {
                continue;
            }

            var collaborationPolicy = EnsurePolicy(
                world,
                company.Id,
                $"Department Collaboration - {department.Name}",
                "GroupPolicyObject",
                "ActiveDirectory",
                "UserEnvironment",
                $"Department-specific collaboration defaults for {department.Name}.",
                activeDirectoryStoreId,
                null);
            AddSettingSet(world, company.Id, collaborationPolicy.Id,
            [
                ("DefaultSharePath", "DriveMappings", "String", $"\\\\files.{company.PrimaryDomain}\\{Slug(department.Name)}"),
                ("SharePointHomeSite", "Collaboration", "String", $"https://collab.{company.PrimaryDomain}/sites/{Slug(department.Name)}"),
                ("TeamsTemplate", "Collaboration", "String", ResolveDepartmentTemplate(department.Name)),
                ("SharedMailboxAddress", "Messaging", "String", $"{Slug(department.Name)}@{company.PrimaryDomain}"),
                ("KnowledgeBaseRoot", "KnowledgeManagement", "String", $"https://collab.{company.PrimaryDomain}/sites/{Slug(department.Name)}/kb"),
                ("BusinessAppLandingPage", "ApplicationAccess", "String", ResolveDepartmentApplicationLanding(company, department.Name)),
                ("DefaultDataClassification", "InformationProtection", "String", ResolveDepartmentDataClassification(department.Name)),
                ("RecordsRetentionLabel", "InformationProtection", "String", ResolveDepartmentRetentionLabel(department.Name))
            ]);
            AddPolicyTarget(world, company.Id, collaborationPolicy.Id, "Container", departmentContainer.Id, "Linked", true, 30, true);
            AddPolicyTarget(world, company.Id, collaborationPolicy.Id, "Group", gpoEditorsGroupId, "DelegatedAdministration", false, 1, true, "Permission", "EditSettings");

            var applicationPolicy = EnsurePolicy(
                world,
                company.Id,
                $"Department App Defaults - {department.Name}",
                "IntuneConfigurationProfile",
                "Intune",
                "ApplicationConfiguration",
                $"Department-specific app configuration and assignment defaults for {department.Name}.",
                null,
                null);
            AddSettingSet(world, company.Id, applicationPolicy.Id,
            [
                ("AssignedEnterpriseApps", "AppAssignment", "String", ResolveDepartmentAssignedApps(department.Name)),
                ("PinnedOfficeShortcuts", "AppAssignment", "String", ResolveDepartmentPinnedApps(department.Name)),
                ("PreferredBrowserBookmarks", "BrowserConfiguration", "String", ResolveDepartmentBookmarks(department.Name)),
                ("M365SensitivityLabelDefault", "InformationProtection", "String", ResolveDepartmentDataClassification(department.Name)),
                ("OfflineFilesAllowed", "Files", "Boolean", ResolveOfflineFilesPreference(department.Name)),
                ("PowerPlatformConnectorPolicy", "LowCodeGovernance", "String", ResolveDepartmentPowerPlatformPolicy(department.Name)),
                ("LineOfBusinessAppInstallRing", "AppAssignment", "String", ResolveDepartmentInstallRing(department.Name)),
                ("DesktopAnalyticsTag", "Telemetry", "String", Slug(department.Name))
            ]);
            AddPolicyTarget(world, company.Id, applicationPolicy.Id, "IdentityStore", entraStore?.Id, "Scope", false, 1);
            AddPolicyTarget(world, company.Id, applicationPolicy.Id, "Container", departmentContainer.Id, "Scope", true, 1, true);
            AddPolicyTarget(world, company.Id, applicationPolicy.Id, "Group", gpoEditorsGroupId, "DelegatedAdministration", false, 1, true, "Permission", "EditSettings");
        }
    }

    private void CreateServerRolePolicies(
        SyntheticEnterpriseWorld world,
        Company company,
        string activeDirectoryStoreId,
        string? serverAdminsGroupId)
    {
        foreach (var serverRole in new[]
                 {
                     "Domain Controller",
                     "File Server",
                     "SQL Server",
                     "Web Server",
                     "Application Server",
                     "Jump Server"
                 })
        {
            var container = FindServerRoleContainer(world, company.Id, activeDirectoryStoreId, serverRole);
            var rolePolicy = EnsurePolicy(
                world,
                company.Id,
                $"{serverRole} Controls",
                "GroupPolicyObject",
                "ActiveDirectory",
                "ServerRole",
                $"Role-specific controls for {serverRole} workloads.",
                activeDirectoryStoreId,
                null);
            AddSettingSet(world, company.Id, rolePolicy.Id, BuildServerRoleSettings(serverRole, company));
            AddPolicyTarget(world, company.Id, rolePolicy.Id, "Container", container?.Id, "Linked", true, 40, true);
            AddPolicyTarget(world, company.Id, rolePolicy.Id, "Group", serverAdminsGroupId, "DelegatedAdministration", false, 1, true, "Permission", "EditSettings");
        }
    }

    private void CreateModernManagementPolicies(
        SyntheticEnterpriseWorld world,
        Company company,
        IdentityStore activeDirectoryStore,
        string? allEmployeesGroupId,
        string? guestGroupId,
        string? officeUsersGroupId,
        string? workstationAdminsGroupId)
    {
        var entraStore = world.IdentityStores.FirstOrDefault(store =>
            store.CompanyId == company.Id
            && string.Equals(store.StoreType, "EntraTenant", StringComparison.OrdinalIgnoreCase));

        var intunePolicies = new[]
        {
            ("Intune Windows Security Baseline", "EndpointSecurity", new (string,string,string,string)[] {
                ("RequireTamperProtection", "EndpointProtection", "Boolean", "true"),
                ("RequireMicrosoftDefender", "EndpointProtection", "Boolean", "true"),
                ("RequireFirewallAllProfiles", "NetworkSecurity", "Boolean", "true"),
                ("RequireExploitProtection", "EndpointProtection", "Boolean", "true"),
                ("RequireSmartAppControl", "ApplicationControl", "Boolean", "true"),
                ("RequireCredentialGuard", "CredentialProtection", "Boolean", "true"),
                ("RequireAttackSurfaceReduction", "EndpointProtection", "String", "Block"),
                ("MinimumOsVersion", "Compliance", "String", "10.0.19045"),
                ("AllowedEdition", "Compliance", "String", "Enterprise"),
                ("RequireMdeRiskScore", "Compliance", "String", "MediumOrLower"),
                ("RequireSecureBoot", "DeviceHealth", "Boolean", "true"),
                ("RequireTpm20", "DeviceHealth", "Boolean", "true")
            }),
            ("Intune BitLocker Standard", "DiskEncryption", new (string,string,string,string)[] {
                ("BitLockerOsDriveEncryption", "DiskEncryption", "Boolean", "true"),
                ("BitLockerFixedDriveEncryption", "DiskEncryption", "Boolean", "true"),
                ("BitLockerRecoveryKeyRotation", "DiskEncryption", "String", "OnUse"),
                ("BitLockerRecoveryEscrow", "DiskEncryption", "String", "EntraID"),
                ("BitLockerStartupAuth", "DiskEncryption", "String", "TpmOnly"),
                ("BitLockerRemovableDrivePolicy", "DiskEncryption", "String", "BlockWriteUnlessEncrypted"),
                ("BitLockerRecoveryKeyLength", "DiskEncryption", "Integer", "48"),
                ("BitLockerAllowStandardUserEncryption", "DiskEncryption", "Boolean", "false")
            }),
            ("Intune Microsoft 365 Apps Deployment", "ApplicationConfiguration", new (string,string,string,string)[] {
                ("AssignedApps", "AppAssignment", "String", "Microsoft365Apps;Teams;OneDrive"),
                ("UpdateChannel", "AppAssignment", "String", "MonthlyEnterprise"),
                ("InstallArchitecture", "AppAssignment", "String", "x64"),
                ("SharedComputerActivation", "AppAssignment", "Boolean", "false"),
                ("OneDriveKnownFolderMove", "AppAssignment", "Boolean", "true"),
                ("TeamsAutoStart", "AppAssignment", "Boolean", "true"),
                ("OutlookCachedMode", "AppAssignment", "Boolean", "true"),
                ("OfficeSelfServiceInstall", "AppAssignment", "Boolean", "false")
            }),
            ("Intune Remote Help Assignment", "RemoteSupport", new (string,string,string,string)[] {
                ("RemoteHelpEnabled", "RemoteSupport", "Boolean", "true"),
                ("RemoteHelpRequireElevationApproval", "RemoteSupport", "Boolean", "true"),
                ("RemoteHelpSessionRecording", "RemoteSupport", "Boolean", "true"),
                ("RemoteHelpClipboardSharing", "RemoteSupport", "Boolean", "false"),
                ("RemoteHelpFileTransfer", "RemoteSupport", "Boolean", "false"),
                ("RemoteHelpAllowedGroups", "RemoteSupport", "String", "GG Tier2 Helpdesk;GG Remote Support Operators")
            }),
            ("Intune Shared Device Profile", "SharedDevice", new (string,string,string,string)[] {
                ("SharedPcMode", "SharedDevice", "Boolean", "true"),
                ("KioskProfileAssigned", "SharedDevice", "Boolean", "true"),
                ("LocalStorageRetentionDays", "SharedDevice", "Integer", "7"),
                ("AccountManagementMode", "SharedDevice", "String", "GuestAndShiftWorkers"),
                ("FastFirstSignIn", "SharedDevice", "Boolean", "true"),
                ("TemporaryProfileCleanup", "SharedDevice", "Boolean", "true"),
                ("EdgePublicBrowsingMode", "SharedDevice", "Boolean", "true")
            })
        };

        foreach (var (name, category, settings) in intunePolicies)
        {
            var policy = EnsurePolicy(world, company.Id, name, "IntuneConfigurationProfile", "Intune", category, $"{name} managed through Intune.", entraStore?.Id, null);
            AddSettingSet(world, company.Id, policy.Id, settings);
            AddPolicyTarget(world, company.Id, policy.Id, "IdentityStore", entraStore?.Id, "Scope", false, 1);
            AddPolicyTarget(world, company.Id, policy.Id, "Group", officeUsersGroupId ?? allEmployeesGroupId, "Include", false, 1);
            AddPolicyTarget(world, company.Id, policy.Id, "Group", workstationAdminsGroupId, "DelegatedAdministration", false, 1, true, "Permission", "EditSettings");
        }

        var conditionalAccessPolicies = new[]
        {
            ("Conditional Access - Admin MFA", "Authentication", "AllAdministrativeRoles"),
            ("Conditional Access - Device Compliance", "DeviceTrust", "CorporateUsers"),
            ("Conditional Access - Guest Collaboration", "ExternalAccess", "Guests"),
            ("Conditional Access - Legacy Auth Block", "Authentication", "AllUsers"),
            ("Conditional Access - VPN Access", "RemoteAccess", "VpnUsers"),
            ("Conditional Access - High Risk Sign-In", "RiskBased", "AllUsers")
        };

        foreach (var (name, category, audience) in conditionalAccessPolicies)
        {
            var policy = EnsurePolicy(world, company.Id, name, "ConditionalAccessPolicy", "EntraID", category, $"{name} managed in Entra Conditional Access.", entraStore?.Id, null);
            AddSettingSet(world, company.Id, policy.Id,
            [
                ("IncludedAudience", category, "String", audience),
                ("GrantControls", category, "String", "RequireMfa"),
                ("SessionControls", category, "String", "SignInFrequency=12h"),
                ("RiskLevelThreshold", category, "String", "Medium"),
                ("ClientAppTypes", category, "String", "Browser,Mobile,Desktop"),
                ("IncludedLocations", category, "String", "AllTrustedAndUntrusted"),
                ("ExcludedEmergencyAccounts", category, "Boolean", "true"),
                ("RequireCompliantDevice", category, "Boolean", audience is "CorporateUsers" or "VpnUsers" ? "true" : "false"),
                ("RequireHybridJoin", category, "Boolean", audience == "AllAdministrativeRoles" ? "true" : "false"),
                ("TokenProtection", category, "Boolean", "true")
            ]);
            AddPolicyTarget(world, company.Id, policy.Id, "IdentityStore", entraStore?.Id, "Scope", false, 1);
            AddPolicyTarget(world, company.Id, policy.Id, "Group", allEmployeesGroupId, "Include", false, 1);
            AddPolicyTarget(world, company.Id, policy.Id, "Group", guestGroupId, audience == "Guests" ? "Include" : "Exclude", false, 2);
        }
    }

    private void CreateWindowsBenchmarkPolicies(
        SyntheticEnterpriseWorld world,
        Company company,
        string activeDirectoryStoreId,
        string? workstationContainerId,
        string? serverContainerId,
        string? gpoEditorsGroupId,
        string? workstationAdminsGroupId,
        string? serverAdminsGroupId)
    {
        var accountAndLockoutPolicy = EnsurePolicy(
            world,
            company.Id,
            "Windows Account and Lockout Hardening",
            "GroupPolicyObject",
            "ActiveDirectory",
            "IdentityBaseline",
            "Expanded account, lockout, and Kerberos guidance aligned to common CIS-style baselines.",
            activeDirectoryStoreId,
            null);
        AddSettingSet(world, company.Id, accountAndLockoutPolicy.Id,
        [
            ("EnforcePasswordHistory", "PasswordPolicy", "Integer", "24"),
            ("MaximumPasswordAge", "PasswordPolicy", "Integer", "365"),
            ("MinimumPasswordAge", "PasswordPolicy", "Integer", "1"),
            ("MinimumPasswordLength", "PasswordPolicy", "Integer", "14"),
            ("PasswordComplexityRequired", "PasswordPolicy", "Boolean", "true"),
            ("RelaxMinimumPasswordLengthLimits", "PasswordPolicy", "Boolean", "true"),
            ("StorePasswordsUsingReversibleEncryption", "PasswordPolicy", "Boolean", "false"),
            ("AccountLockoutDuration", "LockoutPolicy", "Integer", "15"),
            ("AccountLockoutThreshold", "LockoutPolicy", "Integer", "5"),
            ("AdministratorAccountLockout", "LockoutPolicy", "Boolean", "true"),
            ("ResetAccountLockoutCounterAfter", "LockoutPolicy", "Integer", "15"),
            ("KerberosEnforceUserLogonRestrictions", "Authentication", "Boolean", "true"),
            ("KerberosMaxLifetimeForServiceTicket", "Authentication", "Integer", "600"),
            ("KerberosMaxLifetimeForUserTicket", "Authentication", "Integer", "10"),
            ("KerberosMaxLifetimeForUserTicketRenewal", "Authentication", "Integer", "7"),
            ("KerberosMaxToleranceForComputerClockSynchronization", "Authentication", "Integer", "5"),
            ("CachedLogonsCount", "Authentication", "Integer", "4"),
            ("DoNotDisplayLastSignedIn", "Authentication", "Boolean", "true"),
            ("BlockMicrosoftAccounts", "Authentication", "String", "UsersCantAddOrLogOn"),
            ("AllowPKU2UAuthenticationRequests", "Authentication", "Boolean", "false"),
            ("AllowDelegatingSavedCredentials", "Authentication", "String", "DefaultRestricted"),
            ("AllowDelegatingSavedCredentialsWithNtLmOnlyServerAuth", "Authentication", "String", "Disabled"),
            ("NetworkSecurityLanManagerAuthenticationLevel", "Authentication", "String", "SendNTLMv2ResponseOnlyRefuseLMAndNTLM"),
            ("NetworkSecurityDoNotStoreLanManagerHash", "Authentication", "Boolean", "true"),
            ("MicrosoftNetworkClientDigitallySignCommunicationsAlways", "Authentication", "Boolean", "true"),
            ("MicrosoftNetworkServerDigitallySignCommunicationsAlways", "Authentication", "Boolean", "true"),
            ("MachineAccountPasswordMaximumAge", "IdentityLifecycle", "Integer", "30"),
            ("DisableMachineAccountPasswordChanges", "IdentityLifecycle", "Boolean", "false"),
            ("DomainMemberRequireStrongSessionKey", "IdentityLifecycle", "Boolean", "true"),
            ("DomainMemberRequireSignOrSeal", "IdentityLifecycle", "Boolean", "true"),
            ("DomainMemberDisablePasswordChange", "IdentityLifecycle", "Boolean", "false"),
            ("InteractiveLogonMachineInactivityLimit", "InteractiveLogon", "Integer", "900"),
            ("InteractiveLogonSmartCardRemovalBehavior", "InteractiveLogon", "String", "LockWorkstation"),
            ("InteractiveLogonPromptUserToChangePasswordBeforeExpiration", "InteractiveLogon", "Integer", "14"),
            ("InteractiveLogonDoNotRequireCtrlAltDel", "InteractiveLogon", "Boolean", "false"),
            ("InteractiveLogonMessageTitle", "InteractiveLogon", "String", "Authorized Use Only"),
            ("InteractiveLogonMessageText", "InteractiveLogon", "String", "This system is for authorized enterprise use only."),
            ("RenameAdministratorAccount", "InteractiveLogon", "Boolean", "true"),
            ("RenameGuestAccount", "InteractiveLogon", "Boolean", "true"),
            ("AccountsGuestAccountStatus", "InteractiveLogon", "Boolean", "false")
        ]);
        AddPolicyTarget(world, company.Id, accountAndLockoutPolicy.Id, "Container", serverContainerId, "Linked", true, 50, true);
        AddPolicyTarget(world, company.Id, accountAndLockoutPolicy.Id, "Group", gpoEditorsGroupId, "DelegatedAdministration", false, 1, true, "Permission", "EditSettings");

        var userRightsPolicy = EnsurePolicy(
            world,
            company.Id,
            "Windows User Rights Assignment Baseline",
            "GroupPolicyObject",
            "ActiveDirectory",
            "UserRights",
            "User rights assignment baseline inspired by benchmarked enterprise defaults.",
            activeDirectoryStoreId,
            null);
        AddSettingSet(world, company.Id, userRightsPolicy.Id,
        [
            ("AccessCredentialManagerAsTrustedCaller", "UserRightsAssignment", "String", "NoOne"),
            ("AccessThisComputerFromTheNetwork", "UserRightsAssignment", "String", "Administrators;Remote Desktop Users"),
            ("ActAsPartOfTheOperatingSystem", "UserRightsAssignment", "String", "NoOne"),
            ("AdjustMemoryQuotasForAProcess", "UserRightsAssignment", "String", "Administrators;LOCAL SERVICE;NETWORK SERVICE"),
            ("AllowLogOnLocally", "UserRightsAssignment", "String", "Administrators;Users"),
            ("AllowLogOnThroughRemoteDesktopServices", "UserRightsAssignment", "String", "Administrators;Remote Desktop Users"),
            ("BackUpFilesAndDirectories", "UserRightsAssignment", "String", "Administrators"),
            ("ChangeTheSystemTime", "UserRightsAssignment", "String", "Administrators;LOCAL SERVICE"),
            ("CreateAPagefile", "UserRightsAssignment", "String", "Administrators"),
            ("CreateATokenObject", "UserRightsAssignment", "String", "NoOne"),
            ("CreateGlobalObjects", "UserRightsAssignment", "String", "Administrators;LOCAL SERVICE;NETWORK SERVICE;SERVICE"),
            ("CreatePermanentSharedObjects", "UserRightsAssignment", "String", "NoOne"),
            ("CreateSymbolicLinks", "UserRightsAssignment", "String", "Administrators"),
            ("DebugPrograms", "UserRightsAssignment", "String", "Administrators"),
            ("DenyAccessToThisComputerFromTheNetwork", "UserRightsAssignment", "String", "Guests;LocalAccount"),
            ("DenyLogOnAsABatchJob", "UserRightsAssignment", "String", "Guests"),
            ("DenyLogOnAsAService", "UserRightsAssignment", "String", "Guests"),
            ("DenyLogOnLocally", "UserRightsAssignment", "String", "Guests"),
            ("DenyLogOnThroughRemoteDesktopServices", "UserRightsAssignment", "String", "Guests;LocalAccount"),
            ("EnableComputerAndUserAccountsToBeTrustedForDelegation", "UserRightsAssignment", "String", "NoOne"),
            ("ForceShutdownFromARemoteSystem", "UserRightsAssignment", "String", "Administrators"),
            ("GenerateSecurityAudits", "UserRightsAssignment", "String", "LOCAL SERVICE;NETWORK SERVICE"),
            ("ImpersonateAClientAfterAuthentication", "UserRightsAssignment", "String", "Administrators;LOCAL SERVICE;NETWORK SERVICE;SERVICE"),
            ("IncreaseAProcessWorkingSet", "UserRightsAssignment", "String", "Administrators;LOCAL SERVICE"),
            ("IncreaseSchedulingPriority", "UserRightsAssignment", "String", "Administrators"),
            ("LoadAndUnloadDeviceDrivers", "UserRightsAssignment", "String", "Administrators"),
            ("LockPagesInMemory", "UserRightsAssignment", "String", "NoOne"),
            ("LogOnAsABatchJob", "UserRightsAssignment", "String", "Administrators"),
            ("LogOnAsAService", "UserRightsAssignment", "String", "ManagedServiceAccountsOnly"),
            ("ManageAuditingAndSecurityLog", "UserRightsAssignment", "String", "Administrators"),
            ("ModifyAnObjectLabel", "UserRightsAssignment", "String", "NoOne"),
            ("ModifyFirmwareEnvironmentValues", "UserRightsAssignment", "String", "Administrators"),
            ("PerformVolumeMaintenanceTasks", "UserRightsAssignment", "String", "Administrators"),
            ("ProfileSingleProcess", "UserRightsAssignment", "String", "Administrators"),
            ("ProfileSystemPerformance", "UserRightsAssignment", "String", "Administrators"),
            ("ReplaceAProcessLevelToken", "UserRightsAssignment", "String", "LOCAL SERVICE;NETWORK SERVICE"),
            ("RestoreFilesAndDirectories", "UserRightsAssignment", "String", "Administrators"),
            ("ShutDownTheSystem", "UserRightsAssignment", "String", "Administrators;Users"),
            ("SynchronizeDirectoryServiceData", "UserRightsAssignment", "String", "NoOne"),
            ("TakeOwnershipOfFilesOrOtherObjects", "UserRightsAssignment", "String", "Administrators")
        ]);
        AddPolicyTarget(world, company.Id, userRightsPolicy.Id, "Container", workstationContainerId, "Linked", true, 51, true);
        AddPolicyTarget(world, company.Id, userRightsPolicy.Id, "Container", serverContainerId, "Linked", true, 52, true);
        AddPolicyTarget(world, company.Id, userRightsPolicy.Id, "Group", serverAdminsGroupId ?? workstationAdminsGroupId, "DelegatedAdministration", false, 1, true, "Permission", "EditSettings");

        var securityOptionsPolicy = EnsurePolicy(
            world,
            company.Id,
            "Windows Security Options Baseline",
            "GroupPolicyObject",
            "ActiveDirectory",
            "SecurityOptions",
            "Expanded Windows security options baseline inspired by enterprise benchmark guidance.",
            activeDirectoryStoreId,
            null);
        AddSettingSet(world, company.Id, securityOptionsPolicy.Id,
        [
            ("AccountsAdministratorAccountStatus", "SecurityOptions", "Boolean", "false"),
            ("AccountsBlockMicrosoftAccounts", "SecurityOptions", "String", "UsersCantAddOrLogOn"),
            ("AccountsGuestAccountStatus", "SecurityOptions", "Boolean", "false"),
            ("AccountsLimitLocalAccountUseOfBlankPasswords", "SecurityOptions", "Boolean", "true"),
            ("AuditForceAuditPolicySubcategorySettings", "SecurityOptions", "Boolean", "true"),
            ("DevicesAllowedToFormatAndEjectRemovableMedia", "SecurityOptions", "String", "Administrators"),
            ("DomainMemberDigitallyEncryptOrSignSecureChannelDataAlways", "SecurityOptions", "Boolean", "true"),
            ("DomainMemberDigitallyEncryptSecureChannelDataWhenPossible", "SecurityOptions", "Boolean", "true"),
            ("DomainMemberDigitallySignSecureChannelDataWhenPossible", "SecurityOptions", "Boolean", "true"),
            ("DomainMemberDisableMachineAccountPasswordChanges", "SecurityOptions", "Boolean", "false"),
            ("DomainMemberMaximumMachineAccountPasswordAge", "SecurityOptions", "Integer", "30"),
            ("InteractiveLogonDoNotDisplayLastUserName", "SecurityOptions", "Boolean", "true"),
            ("InteractiveLogonDoNotRequireCtrlAltDel", "SecurityOptions", "Boolean", "false"),
            ("InteractiveLogonMachineAccountLockoutThreshold", "SecurityOptions", "Integer", "5"),
            ("InteractiveLogonMachineInactivityLimit", "SecurityOptions", "Integer", "900"),
            ("InteractiveLogonMessageTextForUsersAttemptingToLogOn", "SecurityOptions", "String", "Authorized use only."),
            ("InteractiveLogonMessageTitleForUsersAttemptingToLogOn", "SecurityOptions", "String", "Enterprise Warning"),
            ("InteractiveLogonNumberOfPreviousLogonsToCache", "SecurityOptions", "Integer", "4"),
            ("InteractiveLogonPromptUserToChangePasswordBeforeExpiration", "SecurityOptions", "Integer", "14"),
            ("InteractiveLogonRequireDomainControllerAuthenticationToUnlock", "SecurityOptions", "Boolean", "false"),
            ("MicrosoftNetworkClientDigitallySignCommunicationsAlways", "SecurityOptions", "Boolean", "true"),
            ("MicrosoftNetworkClientDigitallySignCommunicationsIfServerAgrees", "SecurityOptions", "Boolean", "true"),
            ("MicrosoftNetworkClientSendUnencryptedPasswordToThirdPartySmbServers", "SecurityOptions", "Boolean", "false"),
            ("MicrosoftNetworkServerAmountOfIdleTimeRequiredBeforeSuspendingSession", "SecurityOptions", "Integer", "15"),
            ("MicrosoftNetworkServerDigitallySignCommunicationsAlways", "SecurityOptions", "Boolean", "true"),
            ("MicrosoftNetworkServerDigitallySignCommunicationsIfClientAgrees", "SecurityOptions", "Boolean", "true"),
            ("MicrosoftNetworkServerDisconnectClientsWhenLogonHoursExpire", "SecurityOptions", "Boolean", "true"),
            ("MicrosoftNetworkServerServerSpnTargetNameValidationLevel", "SecurityOptions", "String", "AcceptIfProvidedByClient"),
            ("NetworkAccessAllowAnonymousSidNameTranslation", "SecurityOptions", "Boolean", "false"),
            ("NetworkAccessDoNotAllowAnonymousEnumerationOfSamAccounts", "SecurityOptions", "Boolean", "true"),
            ("NetworkAccessDoNotAllowAnonymousEnumerationOfSamAccountsAndShares", "SecurityOptions", "Boolean", "true"),
            ("NetworkAccessLetEveryonePermissionsApplyToAnonymousUsers", "SecurityOptions", "Boolean", "false"),
            ("NetworkAccessNamedPipesThatCanBeAccessedAnonymously", "SecurityOptions", "String", "None"),
            ("NetworkAccessSharesThatCanBeAccessedAnonymously", "SecurityOptions", "String", "None"),
            ("NetworkAccessRestrictAnonymousAccessToNamedPipesAndShares", "SecurityOptions", "Boolean", "true"),
            ("NetworkSecurityAllowLocalSystemToUseComputerIdentityForNtLm", "SecurityOptions", "Boolean", "true"),
            ("NetworkSecurityDoNotStoreLanManagerHashValue", "SecurityOptions", "Boolean", "true"),
            ("NetworkSecurityForceLogoffWhenLogonHoursExpire", "SecurityOptions", "Boolean", "true"),
            ("NetworkSecurityLanManagerAuthenticationLevel", "SecurityOptions", "String", "SendNTLMv2ResponseOnlyRefuseLMAndNTLM"),
            ("NetworkSecurityLdapClientSigningRequirements", "SecurityOptions", "String", "NegotiateSigning"),
            ("NetworkSecurityMinimumSessionSecurityForNtlmSspBasedClients", "SecurityOptions", "String", "RequireNtLmV2And128Bit"),
            ("NetworkSecurityMinimumSessionSecurityForNtlmSspBasedServers", "SecurityOptions", "String", "RequireNtLmV2And128Bit"),
            ("RecoveryConsoleAllowAutomaticAdministrativeLogon", "SecurityOptions", "Boolean", "false"),
            ("RecoveryConsoleAllowFloppyCopyAndAccessToAllDrivesAndFolders", "SecurityOptions", "Boolean", "false"),
            ("ShutdownAllowSystemToBeShutDownWithoutHavingToLogOn", "SecurityOptions", "Boolean", "false"),
            ("ShutdownClearVirtualMemoryPagefile", "SecurityOptions", "Boolean", "true"),
            ("SystemCryptographyUseFipsCompliantAlgorithms", "SecurityOptions", "Boolean", "false"),
            ("SystemObjectsDefaultOwnerForObjectsCreatedByMembersOfAdministratorsGroup", "SecurityOptions", "String", "AdministratorsGroup"),
            ("SystemObjectsRequireCaseInsensitivityForNonWindowsSubsystems", "SecurityOptions", "Boolean", "true"),
            ("SystemSettingsOptionalSubsystems", "SecurityOptions", "String", "Disabled"),
            ("UserAccountControlAdminApprovalModeForTheBuiltInAdministratorAccount", "SecurityOptions", "Boolean", "true"),
            ("UserAccountControlAllowUiAccessApplicationsToPromptForElevationWithoutUsingTheSecureDesktop", "SecurityOptions", "Boolean", "false"),
            ("UserAccountControlBehaviorOfTheElevationPromptForAdministrators", "SecurityOptions", "String", "PromptForConsentOnSecureDesktop"),
            ("UserAccountControlBehaviorOfTheElevationPromptForStandardUsers", "SecurityOptions", "String", "AutomaticallyDenyElevationRequests"),
            ("UserAccountControlDetectApplicationInstallationsAndPromptForElevation", "SecurityOptions", "Boolean", "true"),
            ("UserAccountControlOnlyElevateExecutablesThatAreSignedAndValidated", "SecurityOptions", "Boolean", "false"),
            ("UserAccountControlOnlyElevateUiAccessApplicationsInstalledInSecureLocations", "SecurityOptions", "Boolean", "true"),
            ("UserAccountControlRunAllAdministratorsInAdminApprovalMode", "SecurityOptions", "Boolean", "true"),
            ("UserAccountControlSwitchToTheSecureDesktopWhenPromptingForElevation", "SecurityOptions", "Boolean", "true"),
            ("UserAccountControlVirtualizeFileAndRegistryWriteFailuresToPerUserLocations", "SecurityOptions", "Boolean", "true")
        ]);
        AddPolicyTarget(world, company.Id, securityOptionsPolicy.Id, "Container", workstationContainerId, "Linked", true, 53, true);
        AddPolicyTarget(world, company.Id, securityOptionsPolicy.Id, "Container", serverContainerId, "Linked", true, 54, true);
        AddPolicyTarget(world, company.Id, securityOptionsPolicy.Id, "Group", gpoEditorsGroupId, "DelegatedAdministration", false, 1, true, "Permission", "EditSettings");

        var advancedAuditPolicy = EnsurePolicy(
            world,
            company.Id,
            "Windows Advanced Audit Baseline",
            "GroupPolicyObject",
            "ActiveDirectory",
            "AuditPolicy",
            "Advanced audit policy categories and subcategories for endpoints and servers.",
            activeDirectoryStoreId,
            null);
        AddSettingSet(world, company.Id, advancedAuditPolicy.Id,
        [
            ("AuditCredentialValidation", "AdvancedAudit", "String", "Success,Failure"),
            ("AuditKerberosAuthenticationService", "AdvancedAudit", "String", "Success,Failure"),
            ("AuditKerberosServiceTicketOperations", "AdvancedAudit", "String", "Success,Failure"),
            ("AuditComputerAccountManagement", "AdvancedAudit", "String", "Success,Failure"),
            ("AuditSecurityGroupManagement", "AdvancedAudit", "String", "Success,Failure"),
            ("AuditUserAccountManagement", "AdvancedAudit", "String", "Success,Failure"),
            ("AuditDirectoryServiceAccess", "AdvancedAudit", "String", "Failure"),
            ("AuditDirectoryServiceChanges", "AdvancedAudit", "String", "Success,Failure"),
            ("AuditLogon", "AdvancedAudit", "String", "Success,Failure"),
            ("AuditLogoff", "AdvancedAudit", "String", "Success"),
            ("AuditSpecialLogon", "AdvancedAudit", "String", "Success"),
            ("AuditOtherLogonLogoffEvents", "AdvancedAudit", "String", "Success,Failure"),
            ("AuditDetailedFileShare", "AdvancedAudit", "String", "Failure"),
            ("AuditFileShare", "AdvancedAudit", "String", "Success,Failure"),
            ("AuditFileSystem", "AdvancedAudit", "String", "Failure"),
            ("AuditFilteringPlatformConnection", "AdvancedAudit", "String", "Success"),
            ("AuditFilteringPlatformPacketDrop", "AdvancedAudit", "String", "Failure"),
            ("AuditOtherObjectAccessEvents", "AdvancedAudit", "String", "Success,Failure"),
            ("AuditRegistry", "AdvancedAudit", "String", "Failure"),
            ("AuditApplicationGenerated", "AdvancedAudit", "String", "Success"),
            ("AuditCertificationServices", "AdvancedAudit", "String", "Success,Failure"),
            ("AuditDetailedTracking", "AdvancedAudit", "String", "Success"),
            ("AuditPnpActivity", "AdvancedAudit", "String", "Success"),
            ("AuditProcessCreation", "AdvancedAudit", "String", "Success"),
            ("AuditProcessTermination", "AdvancedAudit", "String", "Success"),
            ("AuditDpapiActivity", "AdvancedAudit", "String", "Success,Failure"),
            ("AuditRpcEvents", "AdvancedAudit", "String", "Failure"),
            ("AuditPolicyChange", "AdvancedAudit", "String", "Success,Failure"),
            ("AuditAuthenticationPolicyChange", "AdvancedAudit", "String", "Success"),
            ("AuditAuthorizationPolicyChange", "AdvancedAudit", "String", "Success"),
            ("AuditMpsSvcRuleLevelPolicyChange", "AdvancedAudit", "String", "Success,Failure"),
            ("AuditSensitivePrivilegeUse", "AdvancedAudit", "String", "Success,Failure"),
            ("AuditIpsecDriver", "AdvancedAudit", "String", "Success,Failure"),
            ("AuditOtherSystemEvents", "AdvancedAudit", "String", "Success,Failure"),
            ("AuditSecurityStateChange", "AdvancedAudit", "String", "Success"),
            ("AuditSecuritySystemExtension", "AdvancedAudit", "String", "Success"),
            ("AuditSystemIntegrity", "AdvancedAudit", "String", "Success,Failure")
        ]);
        AddPolicyTarget(world, company.Id, advancedAuditPolicy.Id, "Container", workstationContainerId, "Linked", true, 55, true);
        AddPolicyTarget(world, company.Id, advancedAuditPolicy.Id, "Container", serverContainerId, "Linked", true, 56, true);

        var defenderPolicy = EnsurePolicy(
            world,
            company.Id,
            "Windows Defender and ASR Baseline",
            "GroupPolicyObject",
            "ActiveDirectory",
            "EndpointProtection",
            "Expanded Defender, SmartScreen, Exploit Guard, and ASR configuration baseline.",
            activeDirectoryStoreId,
            null);
        AddSettingSet(world, company.Id, defenderPolicy.Id,
        [
            ("DefenderRealtimeProtection", "Defender", "Boolean", "true"),
            ("DefenderBehaviorMonitoring", "Defender", "Boolean", "true"),
            ("DefenderCloudDeliveredProtection", "Defender", "String", "High"),
            ("DefenderBlockAtFirstSeen", "Defender", "Boolean", "true"),
            ("DefenderPotentiallyUnwantedAppProtection", "Defender", "Boolean", "true"),
            ("DefenderScanArchiveFiles", "Defender", "Boolean", "true"),
            ("DefenderEmailScanning", "Defender", "Boolean", "true"),
            ("DefenderScriptScanning", "Defender", "Boolean", "true"),
            ("DefenderRemediationDelay", "Defender", "Integer", "0"),
            ("DefenderScheduledQuickScanTime", "Defender", "String", "12:00"),
            ("DefenderScheduledFullScanDay", "Defender", "String", "Sunday"),
            ("DefenderScheduledFullScanTime", "Defender", "String", "02:00"),
            ("DefenderAutomaticRemediation", "Defender", "Boolean", "true"),
            ("DefenderTamperProtection", "Defender", "Boolean", "true"),
            ("DefenderNetworkProtection", "Defender", "String", "Block"),
            ("DefenderControlledFolderAccess", "Defender", "String", "Enabled"),
            ("DefenderSmartScreenForExplorer", "Defender", "String", "Warn"),
            ("DefenderSmartScreenForEdge", "Defender", "String", "Block"),
            ("ExploitGuardAttackSurfaceReductionOfficeChildProcess", "ASR", "String", "Block"),
            ("ExploitGuardAttackSurfaceReductionOfficeExecutableContent", "ASR", "String", "Block"),
            ("ExploitGuardAttackSurfaceReductionScriptDownloadedPayload", "ASR", "String", "Block"),
            ("ExploitGuardAttackSurfaceReductionLsassCredentialStealing", "ASR", "String", "Block"),
            ("ExploitGuardAttackSurfaceReductionPsexecAndWmi", "ASR", "String", "Audit"),
            ("ExploitGuardAttackSurfaceReductionUntrustedUsbProcess", "ASR", "String", "Block"),
            ("ExploitGuardAttackSurfaceReductionAbuseSignedDrivers", "ASR", "String", "Block"),
            ("ExploitGuardNetworkProtection", "ASR", "String", "Enabled"),
            ("ExploitGuardControlledFolderAccessProtectedFolders", "ASR", "String", "Documents;Desktop;Finance"),
            ("ExploitProtectionDataExecutionPrevention", "ExploitProtection", "Boolean", "true"),
            ("ExploitProtectionMandatoryAslr", "ExploitProtection", "Boolean", "true"),
            ("ExploitProtectionBottomUpAslr", "ExploitProtection", "Boolean", "true"),
            ("ExploitProtectionValidateExceptionChains", "ExploitProtection", "Boolean", "true"),
            ("ExploitProtectionValidateHeapIntegrity", "ExploitProtection", "Boolean", "true"),
            ("ExploitProtectionBlockRemoteImageLoads", "ExploitProtection", "Boolean", "true"),
            ("ExploitProtectionBlockLowIntegrityImages", "ExploitProtection", "Boolean", "true"),
            ("ExploitProtectionCodeIntegrityGuard", "ExploitProtection", "Boolean", "true"),
            ("ExploitProtectionExtensionPointDisable", "ExploitProtection", "Boolean", "true")
        ]);
        AddPolicyTarget(world, company.Id, defenderPolicy.Id, "Container", workstationContainerId, "Linked", true, 57, true);
        AddPolicyTarget(world, company.Id, defenderPolicy.Id, "Container", serverContainerId, "Linked", true, 58, true);

        var networkFirewallPolicy = EnsurePolicy(
            world,
            company.Id,
            "Windows Network and Firewall Baseline",
            "GroupPolicyObject",
            "ActiveDirectory",
            "NetworkSecurity",
            "Expanded firewall, networking, and name resolution hardening controls.",
            activeDirectoryStoreId,
            null);
        AddSettingSet(world, company.Id, networkFirewallPolicy.Id,
        [
            ("WindowsFirewallDomainProfileState", "Firewall", "Boolean", "true"),
            ("WindowsFirewallPrivateProfileState", "Firewall", "Boolean", "true"),
            ("WindowsFirewallPublicProfileState", "Firewall", "Boolean", "true"),
            ("WindowsFirewallDomainProfileInboundAction", "Firewall", "String", "Block"),
            ("WindowsFirewallPrivateProfileInboundAction", "Firewall", "String", "Block"),
            ("WindowsFirewallPublicProfileInboundAction", "Firewall", "String", "Block"),
            ("WindowsFirewallDomainProfileOutboundAction", "Firewall", "String", "Allow"),
            ("WindowsFirewallLogDroppedPackets", "Firewall", "Boolean", "true"),
            ("WindowsFirewallLogSuccessfulConnections", "Firewall", "Boolean", "true"),
            ("WindowsFirewallDisableNotifications", "Firewall", "Boolean", "true"),
            ("WindowsFirewallApplyLocalFirewallRules", "Firewall", "Boolean", "false"),
            ("WindowsFirewallApplyLocalConnectionSecurityRules", "Firewall", "Boolean", "false"),
            ("WindowsFirewallDisplayName", "Firewall", "String", "Enterprise Firewall"),
            ("DisableIpv6SourceRouting", "Networking", "Boolean", "true"),
            ("TcpIpDisableIpSourceRouting", "Networking", "String", "HighestProtection"),
            ("EnableIpv6RandomizeIdentifiers", "Networking", "Boolean", "true"),
            ("DisableNetbiosOverTcpIp", "Networking", "Boolean", "true"),
            ("DisableLltdio", "Networking", "Boolean", "true"),
            ("DisableRspndr", "Networking", "Boolean", "true"),
            ("EnableDnsOverHttps", "Networking", "String", "Automatic"),
            ("DisableLLMNR", "Networking", "Boolean", "true"),
            ("DisableWPAD", "Networking", "Boolean", "true"),
            ("DisableProxyFallback", "Networking", "Boolean", "true"),
            ("DisableInternetConnectionSharing", "Networking", "Boolean", "true"),
            ("DisableNetworkBridge", "Networking", "Boolean", "true"),
            ("DisableTeredo", "Networking", "Boolean", "true"),
            ("DisableIsatap", "Networking", "Boolean", "true"),
            ("DisableIpHttps", "Networking", "Boolean", "false"),
            ("DisableSimpleTcpIpServices", "Networking", "Boolean", "true"),
            ("DisableServerMessageBlock1", "Networking", "Boolean", "true"),
            ("RequireSmbEncryption", "Networking", "Boolean", "true"),
            ("RequireRpcAuthentication", "Networking", "Boolean", "true"),
            ("RestrictRemoteNamedPipes", "Networking", "Boolean", "true"),
            ("RestrictRemoteSam", "Networking", "String", "AdministratorsOnly"),
            ("DisableMulticastNameResolution", "Networking", "Boolean", "true"),
            ("DisableRemoteAssistanceSolicited", "Networking", "Boolean", "true"),
            ("DisableRemoteAssistanceOffer", "Networking", "Boolean", "true"),
            ("RemoteDesktopRequireNetworkLevelAuthentication", "RemoteAccess", "Boolean", "true"),
            ("RemoteDesktopRequireSecureRpcCommunication", "RemoteAccess", "Boolean", "true"),
            ("RemoteDesktopSetClientConnectionEncryptionLevel", "RemoteAccess", "String", "High")
        ]);
        AddPolicyTarget(world, company.Id, networkFirewallPolicy.Id, "Container", workstationContainerId, "Linked", true, 59, true);
        AddPolicyTarget(world, company.Id, networkFirewallPolicy.Id, "Container", serverContainerId, "Linked", true, 60, true);
    }

    private void CreateBrowserAndOfficeBenchmarkPolicies(
        SyntheticEnterpriseWorld world,
        Company company,
        string activeDirectoryStoreId,
        string? workstationContainerId,
        string? allEmployeesGroupId,
        string? browserPilotUsersGroupId,
        string? officeUsersGroupId,
        string? officeAdminsGroupId)
    {
        var chromeSecurityPolicy = EnsurePolicy(
            world,
            company.Id,
            "Chrome Enterprise Security Baseline",
            "ChromeEnterprisePolicy",
            "ActiveDirectory",
            "BrowserSecurity",
            "Expanded Chrome enterprise security policy baseline inspired by CIS guidance.",
            activeDirectoryStoreId,
            null);
        AddSettingSet(world, company.Id, chromeSecurityPolicy.Id,
        [
            ("ChromeCrossOriginHttpAuthenticationPrompts", "ChromeSecurity", "Boolean", "false"),
            ("ChromeSafeBrowsingAllowList", "ChromeSecurity", "String", "Disabled"),
            ("ChromeSafeBrowsingProtectionLevel", "ChromeSecurity", "String", "Standard"),
            ("ChromeAllowGoogleCastOnAllIpAddresses", "ChromeSecurity", "Boolean", "false"),
            ("ChromeAllowTimeQueries", "ChromeSecurity", "Boolean", "true"),
            ("ChromeAudioSandboxEnabled", "ChromeSecurity", "Boolean", "true"),
            ("ChromePromptForDownloadLocation", "ChromeSecurity", "Boolean", "true"),
            ("ChromeBackgroundAppsEnabled", "ChromeSecurity", "Boolean", "false"),
            ("ChromeVariationServiceEnabled", "ChromeSecurity", "Boolean", "false"),
            ("ChromeCertificateTransparencyCaExceptions", "ChromeSecurity", "String", "Disabled"),
            ("ChromeCertificateTransparencySpkiExceptions", "ChromeSecurity", "String", "Disabled"),
            ("ChromeCertificateTransparencyUrlExceptions", "ChromeSecurity", "String", "Disabled"),
            ("ChromeSavingBrowserHistoryDisabled", "ChromeSecurity", "Boolean", "false"),
            ("ChromeDnsInterceptionChecksEnabled", "ChromeSecurity", "Boolean", "true"),
            ("ChromeComponentUpdatesEnabled", "ChromeSecurity", "Boolean", "true"),
            ("ChromeGloballyScopedHttpAuthCacheEnabled", "ChromeSecurity", "Boolean", "false"),
            ("ChromeOnlineRevocationChecksEnabled", "ChromeSecurity", "Boolean", "false"),
            ("ChromeRendererCodeIntegrityEnabled", "ChromeSecurity", "Boolean", "true"),
            ("ChromeCommandLineFlagSecurityWarningsEnabled", "ChromeSecurity", "Boolean", "true"),
            ("ChromeThirdPartyBlockingEnabled", "ChromeSecurity", "Boolean", "true"),
            ("ChromeEnterpriseHardwarePlatformApiAllowed", "ChromeSecurity", "Boolean", "false"),
            ("ChromeEphemeralProfileEnabled", "ChromeSecurity", "Boolean", "false"),
            ("ChromeImportAutofillFormDataDisabled", "ChromeSecurity", "Boolean", "true"),
            ("ChromeImportHomePageDisabled", "ChromeSecurity", "Boolean", "true"),
            ("ChromeImportSearchEngineDisabled", "ChromeSecurity", "Boolean", "true"),
            ("ChromeInsecureOriginsExceptionsDisabled", "ChromeSecurity", "Boolean", "true"),
            ("ChromePasswordManagerEnabled", "ChromeSecurity", "Boolean", "false"),
            ("ChromeAutofillAddressEnabled", "ChromeSecurity", "Boolean", "false"),
            ("ChromeAutofillCreditCardEnabled", "ChromeSecurity", "Boolean", "false"),
            ("ChromeBuiltInDnsClientEnabled", "ChromeSecurity", "Boolean", "true"),
            ("ChromeQuicAllowed", "ChromeSecurity", "Boolean", "false"),
            ("ChromeBrowserSigninAllowed", "ChromeSecurity", "Boolean", "false"),
            ("ChromeIncognitoModeAvailability", "ChromeSecurity", "String", "Disabled"),
            ("ChromeSitePerProcessEnabled", "ChromeSecurity", "Boolean", "true"),
            ("ChromeHttpsOnlyMode", "ChromeSecurity", "Boolean", "true"),
            ("ChromePopupsAllowedForUrls", "ChromeSecurity", "String", "Disabled"),
            ("ChromeDefaultNotificationsSetting", "ChromeSecurity", "String", "Block"),
            ("ChromeDefaultGeolocationSetting", "ChromeSecurity", "String", "Block"),
            ("ChromeDefaultWebUsbGuardSetting", "ChromeSecurity", "String", "Block"),
            ("ChromeDefaultWebBluetoothGuardSetting", "ChromeSecurity", "String", "Block"),
            ("ChromeDefaultSerialGuardSetting", "ChromeSecurity", "String", "Block"),
            ("ChromeRemoteAccessHostFirewallTraversal", "ChromeSecurity", "Boolean", "false"),
            ("ChromeRemoteAccessHostDomainList", "ChromeSecurity", "String", company.PrimaryDomain)
        ]);
        AddPolicyTarget(world, company.Id, chromeSecurityPolicy.Id, "Container", workstationContainerId, "Linked", true, 61, true);
        AddPolicyTarget(world, company.Id, chromeSecurityPolicy.Id, "Group", allEmployeesGroupId, "SecurityFilterInclude", false, 1);
        AddPolicyTarget(world, company.Id, chromeSecurityPolicy.Id, "Group", browserPilotUsersGroupId, "SecurityFilterInclude", false, 2);

        var chromeContentPolicy = EnsurePolicy(
            world,
            company.Id,
            "Chrome Content and Update Controls",
            "ChromeEnterprisePolicy",
            "ActiveDirectory",
            "BrowserConfiguration",
            "Chrome content controls, updates, download restrictions, and extension governance.",
            activeDirectoryStoreId,
            null);
        AddSettingSet(world, company.Id, chromeContentPolicy.Id,
        [
            ("ChromeAutoUpdateCheckPeriodMinutes", "ChromeUpdate", "Integer", "720"),
            ("ChromeTargetVersionPrefix", "ChromeUpdate", "String", "Stable"),
            ("ChromeRelaunchNotification", "ChromeUpdate", "String", "Required"),
            ("ChromeRelaunchNotificationPeriod", "ChromeUpdate", "Integer", "4320"),
            ("ChromeDownloadRestrictions", "ChromeContent", "String", "BlockDangerous"),
            ("ChromeDefaultDownloadDirectory", "ChromeContent", "String", "%USERPROFILE%\\Downloads"),
            ("ChromeOpenPdfDownloadInSystemReader", "ChromeContent", "Boolean", "false"),
            ("ChromePrintingAllowedBackgroundGraphicsMode", "ChromeContent", "String", "Disabled"),
            ("ChromeBookmarksBarEnabled", "ChromeContent", "Boolean", "true"),
            ("ChromeManagedBookmarks", "ChromeContent", "String", "Intranet;Help Center;Service Desk"),
            ("ChromeHomepageLocation", "ChromeContent", "String", $"https://intranet.{company.PrimaryDomain}"),
            ("ChromeHomepageIsNewTabPage", "ChromeContent", "Boolean", "false"),
            ("ChromeStartupUrls", "ChromeContent", "String", $"https://intranet.{company.PrimaryDomain};https://collab.{company.PrimaryDomain}"),
            ("ChromeRestoreOnStartup", "ChromeContent", "String", "OpenSpecificPages"),
            ("ChromeDeveloperToolsAvailability", "ChromeContent", "String", "Disallowed"),
            ("ChromeTaskManagerEndProcessEnabled", "ChromeContent", "Boolean", "false"),
            ("ChromeDefaultCookiesSetting", "ChromeContent", "String", "Allow"),
            ("ChromeBlockThirdPartyCookies", "ChromeContent", "Boolean", "true"),
            ("ChromeDefaultImagesSetting", "ChromeContent", "String", "Allow"),
            ("ChromeDefaultJavaScriptSetting", "ChromeContent", "String", "Allow"),
            ("ChromeDefaultPluginsSetting", "ChromeContent", "String", "Block"),
            ("ChromeDefaultPopupsSetting", "ChromeContent", "String", "Block"),
            ("ChromeDefaultFileSystemReadGuardSetting", "ChromeContent", "String", "Block"),
            ("ChromeDefaultFileSystemWriteGuardSetting", "ChromeContent", "String", "Block"),
            ("ChromeExtensionInstallAllowlist", "ChromeExtensions", "String", "CorpPasswordManager;SSOHelper;MdeBrowserIsolation"),
            ("ChromeExtensionInstallBlocklist", "ChromeExtensions", "String", "*"),
            ("ChromeExtensionInstallForcelist", "ChromeExtensions", "String", "CorpPasswordManager;SSOHelper"),
            ("ChromeBrowserLabsEnabled", "ChromeContent", "Boolean", "false"),
            ("ChromeSpellcheckEnabled", "ChromeContent", "Boolean", "true"),
            ("ChromeMetricsReportingEnabled", "ChromeContent", "Boolean", "false"),
            ("ChromeSearchSuggestEnabled", "ChromeContent", "Boolean", "false"),
            ("ChromeTranslateEnabled", "ChromeContent", "Boolean", "false"),
            ("ChromeSavingBrowserHistoryDisabled", "ChromeContent", "Boolean", "false"),
            ("ChromeEnterpriseReportingEnabled", "ChromeContent", "Boolean", "true"),
            ("ChromeEnterpriseReportingUploadFrequency", "ChromeContent", "Integer", "720"),
            ("ChromeDataLeakPreventionRuleSet", "ChromeContent", "String", "CorpStandard")
        ]);
        AddPolicyTarget(world, company.Id, chromeContentPolicy.Id, "Container", workstationContainerId, "Linked", true, 62, true);
        AddPolicyTarget(world, company.Id, chromeContentPolicy.Id, "Group", allEmployeesGroupId, "SecurityFilterInclude", false, 1);

        var officeTrustPolicy = EnsurePolicy(
            world,
            company.Id,
            "Office Trust Center and Macro Baseline",
            "OfficePolicy",
            "ActiveDirectory",
            "ApplicationSecurity",
            "Expanded Office macro, trust center, ActiveX, and document protection baseline.",
            activeDirectoryStoreId,
            null);
        AddSettingSet(world, company.Id, officeTrustPolicy.Id,
        [
            ("OfficeBlockFlashActivation", "OfficeSecurity", "String", "BlockAllActivation"),
            ("OfficeRestrictLegacyJScriptExecution", "OfficeSecurity", "Boolean", "true"),
            ("OfficeAllowTrustedLocationsOnNetwork", "OfficeSecurity", "Boolean", "false"),
            ("OfficeDisableAllTrustedLocations", "OfficeSecurity", "Boolean", "false"),
            ("OfficeTrustBarNotificationForUnsignedAddIns", "OfficeSecurity", "Boolean", "false"),
            ("OfficeRequireApplicationAddinsToBeSigned", "OfficeSecurity", "Boolean", "true"),
            ("OfficeDisableAllActiveX", "OfficeSecurity", "Boolean", "true"),
            ("OfficeDisableEmbeddedFiles", "OfficeSecurity", "Boolean", "true"),
            ("OfficeOpenXmlFileValidation", "OfficeSecurity", "Boolean", "true"),
            ("OfficeProtectedViewForInternetFiles", "OfficeSecurity", "Boolean", "true"),
            ("OfficeProtectedViewForUnsafeLocations", "OfficeSecurity", "Boolean", "true"),
            ("OfficeProtectedViewForOutlookAttachments", "OfficeSecurity", "Boolean", "true"),
            ("OfficeProtectedViewAllowEditing", "OfficeSecurity", "Boolean", "false"),
            ("OfficeMacroRuntimeScanning", "OfficeSecurity", "Boolean", "true"),
            ("OfficeVbaMacroNotificationSettings", "OfficeSecurity", "String", "DisableExceptSigned"),
            ("OfficeBlockMacrosFromInternet", "OfficeSecurity", "Boolean", "true"),
            ("OfficeDisableExcel4Macros", "OfficeSecurity", "Boolean", "true"),
            ("OfficeDisableDdeServerLaunch", "OfficeSecurity", "Boolean", "true"),
            ("OfficeDisableAutomaticLinkUpdate", "OfficeSecurity", "Boolean", "true"),
            ("OfficeDisableHiddenDataAndPersonalInfo", "OfficeSecurity", "Boolean", "true"),
            ("OfficeDisableCustomUiLoadingFromDocument", "OfficeSecurity", "Boolean", "true"),
            ("OfficeEnableDataExecutionPrevention", "OfficeSecurity", "Boolean", "true"),
            ("OfficeRequireMacroScanningByAntivirus", "OfficeSecurity", "Boolean", "true"),
            ("OfficeVstoSuppressPrompts", "OfficeSecurity", "Boolean", "true"),
            ("OfficeTrustedPublisherLockdown", "OfficeSecurity", "Boolean", "true"),
            ("OfficeOneNoteEmbeddedFilesProtection", "OfficeSecurity", "Boolean", "true"),
            ("OfficeOutlookReadAsPlainText", "OfficeSecurity", "Boolean", "true"),
            ("OfficeOutlookAutomaticPictureDownload", "OfficeSecurity", "Boolean", "false"),
            ("OfficeOutlookSmtpAuthenticationRequired", "OfficeSecurity", "Boolean", "true"),
            ("OfficeOutlookPromptForProfile", "OfficeSecurity", "Boolean", "false"),
            ("OfficeDisablePublisherMacroRuntime", "OfficeSecurity", "Boolean", "true"),
            ("OfficeDisableAccessSandboxMode", "OfficeSecurity", "Boolean", "false"),
            ("OfficeDisableAccessLegacyBarcodes", "OfficeSecurity", "Boolean", "true"),
            ("OfficeDisablePowerPointExternalContent", "OfficeSecurity", "Boolean", "true"),
            ("OfficeDisableAutomaticUnsafeHyperlinks", "OfficeSecurity", "Boolean", "true"),
            ("OfficeIEMimeHandling", "OfficeSecurity", "Boolean", "true"),
            ("OfficeIESavedFromUrlProtection", "OfficeSecurity", "Boolean", "true"),
            ("OfficeIERestrictFileDownload", "OfficeSecurity", "Boolean", "true"),
            ("OfficeIELocalMachineZoneLockdown", "OfficeSecurity", "Boolean", "true"),
            ("OfficeIENavigateUrlRestrictions", "OfficeSecurity", "Boolean", "true"),
            ("OfficeIEScriptedWindowSecurityRestrictions", "OfficeSecurity", "Boolean", "true")
        ]);
        AddPolicyTarget(world, company.Id, officeTrustPolicy.Id, "Container", workstationContainerId, "Linked", true, 63, true);
        AddPolicyTarget(world, company.Id, officeTrustPolicy.Id, "Group", officeUsersGroupId ?? allEmployeesGroupId, "SecurityFilterInclude", false, 1);
        AddPolicyTarget(world, company.Id, officeTrustPolicy.Id, "Group", officeAdminsGroupId, "DelegatedAdministration", false, 1, true, "Permission", "EditSettings");

        var officePrivacyPolicy = EnsurePolicy(
            world,
            company.Id,
            "Office Privacy and Update Baseline",
            "OfficePolicy",
            "ActiveDirectory",
            "ApplicationConfiguration",
            "Office privacy, telemetry, connected experiences, and update governance baseline.",
            activeDirectoryStoreId,
            null);
        AddSettingSet(world, company.Id, officePrivacyPolicy.Id,
        [
            ("OfficeEnableAutomaticUpdates", "OfficeUpdates", "Boolean", "true"),
            ("OfficeHideEnableDisableUpdatesOption", "OfficeUpdates", "Boolean", "true"),
            ("OfficeUpdateChannel", "OfficeUpdates", "String", "MonthlyEnterprise"),
            ("OfficeDelayDownloadOfUpdates", "OfficeUpdates", "Boolean", "false"),
            ("OfficeTelemetryLevel", "OfficePrivacy", "String", "Required"),
            ("OfficeConnectedExperiences", "OfficePrivacy", "String", "Limited"),
            ("OfficeOptionalConnectedExperiences", "OfficePrivacy", "Boolean", "false"),
            ("OfficeDiagnosticDataLevel", "OfficePrivacy", "String", "Required"),
            ("OfficeSendPersonalInformation", "OfficePrivacy", "Boolean", "false"),
            ("OfficeOnlineContentDownload", "OfficePrivacy", "String", "Disabled"),
            ("OfficeRoamingSettingsEnabled", "OfficePrivacy", "Boolean", "false"),
            ("OfficeLinkedInFeaturesEnabled", "OfficePrivacy", "Boolean", "false"),
            ("OfficeAddinStoreAccess", "OfficePrivacy", "Boolean", "false"),
            ("OfficeFeedbackEnabled", "OfficePrivacy", "Boolean", "false"),
            ("OfficeFileOpenBlockLegacyFormats", "OfficeConfiguration", "Boolean", "true"),
            ("OfficeDefaultSaveFormat", "OfficeConfiguration", "String", "OpenXml"),
            ("OfficeDefaultFileBlockBehavior", "OfficeConfiguration", "String", "OpenInProtectedView"),
            ("OfficeModernCommentsEnabled", "OfficeConfiguration", "Boolean", "true"),
            ("OfficeAdobePdfIntegrationEnabled", "OfficeConfiguration", "Boolean", "false"),
            ("OfficeAutoRecoverEnabled", "OfficeConfiguration", "Boolean", "true"),
            ("OfficeAutoRecoverIntervalMinutes", "OfficeConfiguration", "Integer", "10"),
            ("OfficeSyncIntegrationEnabled", "OfficeConfiguration", "Boolean", "true"),
            ("OfficeOutlookCachedModeEnabled", "OfficeConfiguration", "Boolean", "true"),
            ("OfficeTeamsMeetingAddInEnabled", "OfficeConfiguration", "Boolean", "true"),
            ("OfficeCloudFontsEnabled", "OfficeConfiguration", "Boolean", "false"),
            ("OfficeStartupBoostEnabled", "OfficeConfiguration", "Boolean", "false"),
            ("OfficePowerQueryExternalDataWarning", "OfficeConfiguration", "Boolean", "true"),
            ("OfficeWorkbookLinksWarning", "OfficeConfiguration", "Boolean", "true"),
            ("OfficeCoauthoringAllowed", "OfficeConfiguration", "Boolean", "true"),
            ("OfficeOneDriveKnownFolderMovePrompt", "OfficeConfiguration", "Boolean", "false")
        ]);
        AddPolicyTarget(world, company.Id, officePrivacyPolicy.Id, "Container", workstationContainerId, "Linked", true, 64, true);
        AddPolicyTarget(world, company.Id, officePrivacyPolicy.Id, "Group", officeUsersGroupId ?? allEmployeesGroupId, "SecurityFilterInclude", false, 1);
    }

    private void AddSettingSet(
        SyntheticEnterpriseWorld world,
        string companyId,
        string policyId,
        IEnumerable<(string Name, string Category, string ValueType, string Value)> settings)
    {
        foreach (var (name, category, valueType, value) in settings)
        {
            AddPolicySetting(world, companyId, policyId, name, category, valueType, value);
        }
    }

    private static string ResolveRegionalLocale(Office office)
        => office.Country switch
        {
            "Canada" => office.City.Contains("Montr", StringComparison.OrdinalIgnoreCase) ? "fr-CA" : "en-CA",
            "Mexico" => "es-MX",
            "United Kingdom" => "en-GB",
            _ => "en-US"
        };

    private static string ResolveDepartmentTemplate(string departmentName)
        => departmentName.ToLowerInvariant() switch
        {
            var name when name.Contains("sales") => "Sales Pursuit Workspace",
            var name when name.Contains("marketing") => "Campaign Operations Workspace",
            var name when name.Contains("support") => "Case Management Workspace",
            var name when name.Contains("engineering") => "Engineering Delivery Workspace",
            var name when name.Contains("quality") => "Quality Review Workspace",
            var name when name.Contains("finance") || name.Contains("account") => "Controlled Finance Workspace",
            _ => "Department Operations Workspace"
        };

    private string ResolveDepartmentApplicationLanding(Company company, string departmentName)
        => departmentName.ToLowerInvariant() switch
        {
            var name when name.Contains("sales") || name.Contains("marketing") => $"https://crm.{company.PrimaryDomain}",
            var name when name.Contains("human") || name.Contains("people") => $"https://hris.{company.PrimaryDomain}",
            var name when name.Contains("finance") || name.Contains("account") || name.Contains("procurement") => $"https://erp.{company.PrimaryDomain}",
            _ => $"https://portal.{company.PrimaryDomain}"
        };

    private static string ResolveDepartmentDataClassification(string departmentName)
        => departmentName.ToLowerInvariant() switch
        {
            var name when name.Contains("finance") || name.Contains("account") || name.Contains("payroll") => "Confidential-Finance",
            var name when name.Contains("human") || name.Contains("people") => "Confidential-HR",
            var name when name.Contains("legal") || name.Contains("compliance") => "HighlyConfidential-Legal",
            var name when name.Contains("engineering") || name.Contains("product") => "Internal-Engineering",
            _ => "Internal-General"
        };

    private static string ResolveDepartmentRetentionLabel(string departmentName)
        => departmentName.ToLowerInvariant() switch
        {
            var name when name.Contains("finance") || name.Contains("account") => "Finance-7Y",
            var name when name.Contains("human") || name.Contains("people") => "HR-6Y",
            var name when name.Contains("quality") || name.Contains("manufacturing") => "Operations-5Y",
            _ => "Corporate-3Y"
        };

    private static string ResolveDepartmentAssignedApps(string departmentName)
        => departmentName.ToLowerInvariant() switch
        {
            var name when name.Contains("sales") || name.Contains("marketing") => "CRM;PowerBI;DocuSign",
            var name when name.Contains("support") => "ITSM;RemoteHelp;KnowledgeBase",
            var name when name.Contains("engineering") || name.Contains("product") => "VisualStudioCode;GitHubDesktop;PowerBI",
            var name when name.Contains("finance") || name.Contains("account") => "ERP;ExcelAddins;PowerBI",
            var name when name.Contains("human") || name.Contains("people") => "HRIS;AdobeSign;PowerBI",
            _ => "Microsoft365Apps;Edge;CompanyPortal"
        };

    private static string ResolveDepartmentPinnedApps(string departmentName)
        => departmentName.ToLowerInvariant() switch
        {
            var name when name.Contains("sales") => "Outlook;Teams;CRM;PowerBI",
            var name when name.Contains("support") => "Teams;ITSM;RemoteHelp;Edge",
            var name when name.Contains("engineering") => "Teams;VisualStudioCode;Edge;PowerShell",
            _ => "Outlook;Teams;Edge;Office"
        };

    private static string ResolveDepartmentBookmarks(string departmentName)
        => departmentName.ToLowerInvariant() switch
        {
            var name when name.Contains("sales") => "CRM Home;Pricing Desk;Partner Portal",
            var name when name.Contains("marketing") => "Campaign Calendar;Brand Center;Analytics",
            var name when name.Contains("support") => "Case Console;Runbooks;Knowledge Base",
            var name when name.Contains("engineering") => "Build Dashboard;Repo Portal;Architecture Wiki",
            _ => "Company Portal;Intranet;Help Center"
        };

    private static string ResolveOfflineFilesPreference(string departmentName)
        => departmentName.Contains("engineering", StringComparison.OrdinalIgnoreCase) || departmentName.Contains("sales", StringComparison.OrdinalIgnoreCase)
            ? "true"
            : "false";

    private static string ResolveDepartmentPowerPlatformPolicy(string departmentName)
        => departmentName.ToLowerInvariant() switch
        {
            var name when name.Contains("finance") || name.Contains("human") => "RestrictedConnectors",
            var name when name.Contains("operations") || name.Contains("engineering") => "ApprovedBusinessConnectors",
            _ => "StandardConnectors"
        };

    private static string ResolveDepartmentInstallRing(string departmentName)
        => departmentName.ToLowerInvariant() switch
        {
            var name when name.Contains("information technology") || name.Contains("engineering") => "Pilot",
            var name when name.Contains("finance") || name.Contains("human") => "BroadWithApproval",
            _ => "Broad"
        };

    private EnvironmentContainer? FindServerRoleContainer(
        SyntheticEnterpriseWorld world,
        string companyId,
        string activeDirectoryStoreId,
        string serverRole)
    {
        var containerName = serverRole.ToLowerInvariant() switch
        {
            var role when role.Contains("domain") => "Identity",
            var role when role.Contains("file") || role.Contains("print") => "File and Print",
            var role when role.Contains("database") || role.Contains("sql") => "Database",
            var role when role.Contains("web") => "Web",
            var role when role.Contains("application") || role.Contains("app") => "Application",
            var role when role.Contains("jump") || role.Contains("vpn") || role.Contains("remote") => "Remote Access",
            _ => "Management"
        };

        return FindContainer(world, companyId, "OrganizationalUnit", activeDirectoryStoreId, containerName)
               ?? FindContainer(world, companyId, "OrganizationalUnit", activeDirectoryStoreId, "Servers");
    }

    private IEnumerable<(string Name, string Category, string ValueType, string Value)> BuildServerRoleSettings(string serverRole, Company company)
    {
        var common = new List<(string, string, string, string)>
        {
            ("WindowsFirewallEnabled", "NetworkSecurity", "Boolean", "true"),
            ("PowerShellTranscription", "AuditPolicy", "Boolean", "true"),
            ("ServiceAccountRestrictionMode", "IdentityProtection", "String", "ManagedOnly"),
            ("PatchWindow", "PatchManagement", "String", "Sunday-0200"),
            ("ApprovedBackupPolicy", "Operations", "String", "DailyIncremental"),
            ("MonitoringProfile", "Operations", "String", "EnterpriseStandard")
        };

        var roleSpecific = serverRole.ToLowerInvariant() switch
        {
            var role when role.Contains("domain") => new (string, string, string, string)[]
            {
                ("InteractiveLogonAllowed", "IdentityProtection", "Boolean", "false"),
                ("DcShadowProtection", "IdentityProtection", "Boolean", "true"),
                ("PrivilegedAccessWorkflow", "IdentityProtection", "String", "Tier0Only"),
                ("KerberosArmoring", "Authentication", "Boolean", "true"),
                ("LdapChannelBinding", "Authentication", "String", "Required"),
                ("ReplicationMonitoring", "Operations", "String", "Critical")
            },
            var role when role.Contains("file") || role.Contains("print") => new (string, string, string, string)[]
            {
                ("SmbEncryptionRequired", "FileServices", "Boolean", "true"),
                ("AccessBasedEnumeration", "FileServices", "Boolean", "true"),
                ("PrintDriverInstallRestrictions", "PrintServices", "String", "AdminsOnly"),
                ("RansomwareProtectionMode", "FileServices", "String", "Enhanced"),
                ("ShadowCopySchedule", "FileServices", "String", "Daily"),
                ("QuotaTemplate", "FileServices", "String", "DepartmentStandard")
            },
            var role when role.Contains("database") || role.Contains("sql") => new (string, string, string, string)[]
            {
                ("SqlTlsMinimumVersion", "DatabaseSecurity", "String", "TLS1.2"),
                ("SqlAuditingEnabled", "DatabaseSecurity", "Boolean", "true"),
                ("SqlSaAccountDisabled", "DatabaseSecurity", "Boolean", "true"),
                ("SqlTempDbSizingProfile", "DatabasePerformance", "String", "Production"),
                ("SqlAgentProxyRestriction", "DatabaseSecurity", "String", "ManagedAccountsOnly"),
                ("SqlBackupEncryption", "DatabaseSecurity", "Boolean", "true")
            },
            var role when role.Contains("web") => new (string, string, string, string)[]
            {
                ("IisDynamicCompression", "WebConfiguration", "Boolean", "true"),
                ("IisRequestFiltering", "WebSecurity", "String", "Strict"),
                ("HttpStrictTransportSecurity", "WebSecurity", "Boolean", "true"),
                ("TlsCertificateAutoRenewal", "WebSecurity", "Boolean", "true"),
                ("WebAppPoolIdentityMode", "WebConfiguration", "String", "ServiceAccountOnly"),
                ("ReverseProxyWafProfile", "WebSecurity", "String", "EnterpriseStandard")
            },
            var role when role.Contains("application") || role.Contains("app") => new (string, string, string, string)[]
            {
                ("AppServiceAccountRotation", "ApplicationSecurity", "String", "30Days"),
                ("MiddlewareJitAdmin", "ApplicationSecurity", "Boolean", "true"),
                ("ApprovedOutboundDestinations", "NetworkSecurity", "String", $"api.{company.PrimaryDomain};id.{company.PrimaryDomain}"),
                ("AppTelemetryProfile", "Observability", "String", "DeepDiagnostics"),
                ("InMemorySecretsAllowed", "ApplicationSecurity", "Boolean", "false"),
                ("ServiceRecoveryActions", "Operations", "String", "Restart-Restart-Alert")
            },
            _ => new (string, string, string, string)[]
            {
                ("RemoteAdminSourceIps", "NetworkSecurity", "String", "CorpManagementSubnets"),
                ("InteractiveLogonAllowed", "IdentityProtection", "Boolean", "false"),
                ("PrivilegedSessionRecording", "AuditPolicy", "Boolean", "true"),
                ("ChangeWindowEnforcement", "Operations", "String", "Required"),
                ("ServiceNowChangeLinkRequired", "Operations", "Boolean", "true"),
                ("LocalAdminBreakGlassPolicy", "IdentityProtection", "String", "EscrowedAndMonitored")
            }
        };

        return common.Concat(roleSpecific);
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
        IReadOnlyList<Office> offices,
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
        var workstationStandard = CreateOu(company, "Corporate Standard", workstations.Id, $"OU=Corporate Standard,{workstations.DistinguishedName}", "Corporate workstation baseline");
        var workstationRemote = CreateOu(company, "Remote Workforce", workstations.Id, $"OU=Remote Workforce,{workstations.DistinguishedName}", "Remote and mobile workforce devices");
        var workstationKiosk = CreateOu(company, "Shared Kiosks", workstations.Id, $"OU=Shared Kiosks,{workstations.DistinguishedName}", "Shared kiosk and frontline devices");
        var workstationIt = CreateOu(company, "IT Administration", workstations.Id, $"OU=IT Administration,{workstations.DistinguishedName}", "IT and support workstations");
        var serverIdentity = CreateOu(company, "Identity", productionServers.Id, $"OU=Identity,{productionServers.DistinguishedName}", "Identity and directory servers");
        var serverFilePrint = CreateOu(company, "File and Print", productionServers.Id, $"OU=File and Print,{productionServers.DistinguishedName}", "File and print servers");
        var serverDatabase = CreateOu(company, "Database", productionServers.Id, $"OU=Database,{productionServers.DistinguishedName}", "Database servers");
        var serverWeb = CreateOu(company, "Web", productionServers.Id, $"OU=Web,{productionServers.DistinguishedName}", "Web servers");
        var serverApplication = CreateOu(company, "Application", productionServers.Id, $"OU=Application,{productionServers.DistinguishedName}", "Application servers");
        var serverManagement = CreateOu(company, "Management", productionServers.Id, $"OU=Management,{productionServers.DistinguishedName}", "Management and monitoring servers");
        var serverRemoteAccess = CreateOu(company, "Remote Access", productionServers.Id, $"OU=Remote Access,{productionServers.DistinguishedName}", "VPN and remote access servers");

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
            developmentServers,
            workstationStandard,
            workstationRemote,
            workstationKiosk,
            workstationIt,
            serverIdentity,
            serverFilePrint,
            serverDatabase,
            serverWeb,
            serverApplication,
            serverManagement,
            serverRemoteAccess
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

        foreach (var office in offices
                     .GroupBy(office => office.City, StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First()))
        {
            var officeToken = EscapeDn(office.City);
            result.Add(CreateOu(
                company,
                office.City,
                workstationStandard.Id,
                $"OU={officeToken},{workstationStandard.DistinguishedName}",
                "Location Workstations"));
            result.Add(CreateOu(
                company,
                office.City,
                users.Id,
                $"OU={officeToken},{users.DistinguishedName}",
                "Location Users"));
        }

        return result;
    }

    private List<DirectoryAccount> CreateUserAccounts(
        Company company,
        IReadOnlyList<Person> people,
        IReadOnlyList<Department> departments,
        IReadOnlyList<DirectoryOrganizationalUnit> ous,
        string rootDomain,
        HashSet<string> issuedPasswords,
        ISet<string> issuedSamAccountNames)
    {
        var usersOu = ous.First(o => o.Name == "Users");
        var departmentOus = ous
            .Where(o => o.ParentOuId == usersOu.Id)
            .GroupBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var departmentNamesById = departments.ToDictionary(d => d.Id, d => d.Name, StringComparer.OrdinalIgnoreCase);

        return people.Select(person =>
        {
            var sam = EnsureUniqueSamAccountName(BuildSam(person.FirstName, person.LastName, person.EmployeeId), issuedSamAccountNames);
            var targetOu = departmentNamesById.TryGetValue(person.DepartmentId, out var departmentName) &&
                           departmentOus.TryGetValue(departmentName, out var departmentOu)
                ? departmentOu
                : usersOu;
            var passwordLastSet = _clock.UtcNow.AddDays(-_randomSource.Next(1, 90));
            var lifecycle = CreateAccountLifecycle(passwordLastSet, 120, 1825, 14);

            return new DirectoryAccount
            {
                Id = _idFactory.Next("ACT"),
                CompanyId = company.Id,
                PersonId = person.Id,
                AccountType = "User",
                DisplayName = person.DisplayName,
                SamAccountName = sam,
                UserPrincipalName = person.UserPrincipalName,
                Mail = person.UserPrincipalName,
                Domain = rootDomain,
                DistinguishedName = $"CN={EscapeDn(person.DisplayName)},{targetOu.DistinguishedName}",
                OuId = targetOu.Id,
                Enabled = true,
                Privileged = false,
                MfaEnabled = true,
                EmployeeId = person.EmployeeId,
                GeneratedPassword = CreateUniquePassword(issuedPasswords),
                PasswordProfile = "EmployeeStandard",
                AdministrativeTier = null,
                LastLogon = lifecycle.LastLogon,
                WhenCreated = lifecycle.WhenCreated,
                WhenModified = lifecycle.WhenModified,
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
        ISet<string> issuedAccountUpns,
        ISet<string> issuedSamAccountNames,
        bool includeAdministrativeTiers)
    {
        var defaultOu = ous.First(o => o.Name == "Service Accounts");
        var tier1Ou = includeAdministrativeTiers
            ? FindAdminTierOu(ous, "Tier 1") ?? defaultOu
            : defaultOu;
        var results = new List<DirectoryAccount>();

        for (var i = 0; i < definition.ServiceAccountCount; i++)
        {
            var blueprint = BuildServiceAccountBlueprint(company, i);
            var targetOu = blueprint.AdministrativeTier is null ? defaultOu : tier1Ou;
            var upn = BuildUniqueDirectoryAccountUpn(blueprint.UserPrincipalNameLocalPart, rootDomain, issuedAccountUpns);
            var samAccountName = EnsureUniqueSamAccountName(blueprint.SamAccountName, issuedSamAccountNames);
            var passwordLastSet = _clock.UtcNow.AddDays(-_randomSource.Next(7, 180));
            var lifecycle = CreateAccountLifecycle(passwordLastSet, 180, 3650, 30);
            results.Add(new DirectoryAccount
            {
                Id = _idFactory.Next("ACT"),
                CompanyId = company.Id,
                AccountType = "Service",
                DisplayName = blueprint.CommonName,
                SamAccountName = samAccountName,
                UserPrincipalName = upn,
                Mail = null,
                Domain = rootDomain,
                DistinguishedName = $"CN={blueprint.CommonName},{targetOu.DistinguishedName}",
                OuId = targetOu.Id,
                Enabled = true,
                Privileged = blueprint.Privileged,
                MfaEnabled = false,
                GeneratedPassword = CreateUniquePassword(issuedPasswords, 20),
                PasswordProfile = blueprint.PasswordProfile,
                AdministrativeTier = blueprint.AdministrativeTier,
                LastLogon = lifecycle.LastLogon,
                WhenCreated = lifecycle.WhenCreated,
                WhenModified = lifecycle.WhenModified,
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
        HashSet<string> issuedPasswords,
        ISet<string> issuedAccountUpns,
        ISet<string> issuedSamAccountNames)
    {
        var targetOu = ous.First(o => o.Name == "Shared Mailboxes");
        var results = new List<DirectoryAccount>();

        for (var i = 0; i < definition.SharedMailboxCount; i++)
        {
            var blueprint = BuildSharedMailboxBlueprint(i);
            var localPart = blueprint.LocalPart;
            var upn = BuildUniqueDirectoryAccountUpn(localPart, rootDomain, issuedAccountUpns);
            var samAccountName = EnsureUniqueSamAccountName(Truncate(localPart.Replace("-", ""), 20), issuedSamAccountNames);
            var passwordLastSet = _clock.UtcNow.AddDays(-_randomSource.Next(14, 180));
            var lifecycle = CreateAccountLifecycle(passwordLastSet, 90, 2190, 21);
            results.Add(new DirectoryAccount
            {
                Id = _idFactory.Next("ACT"),
                CompanyId = company.Id,
                AccountType = "Shared",
                DisplayName = blueprint.DisplayName,
                SamAccountName = samAccountName,
                UserPrincipalName = upn,
                Mail = upn,
                Domain = rootDomain,
                DistinguishedName = $"CN={EscapeDn(blueprint.DisplayName)},{targetOu.DistinguishedName}",
                OuId = targetOu.Id,
                Enabled = true,
                Privileged = false,
                MfaEnabled = false,
                GeneratedPassword = CreateUniquePassword(issuedPasswords, 18),
                PasswordProfile = "SharedMailbox",
                AdministrativeTier = null,
                LastLogon = lifecycle.LastLogon,
                WhenCreated = lifecycle.WhenCreated,
                WhenModified = lifecycle.WhenModified,
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

    private static string EnsureUniqueSamAccountName(string baseValue, ISet<string> issuedValues)
    {
        var normalizedBase = string.IsNullOrWhiteSpace(baseValue) ? "shared" : baseValue.Trim();
        var candidate = Truncate(normalizedBase, 20);
        if (issuedValues.Add(candidate))
        {
            return candidate;
        }

        for (var suffix = 2; suffix < 1000; suffix++)
        {
            var suffixText = suffix.ToString(CultureInfo.InvariantCulture);
            candidate = Truncate($"{normalizedBase}{suffixText}", 20);
            if (issuedValues.Add(candidate))
            {
                return candidate;
            }
        }

        return candidate;
    }

    private SharedMailboxBlueprint BuildSharedMailboxBlueprint(int index)
    {
        var patterns = new[]
        {
            new SharedMailboxPattern("helpdesk", "Help Desk", ["North America", "Canada", "Mexico", "VIP", "Field Services", "Manufacturing", "After Hours", "Escalations", "Contractors"]),
            new SharedMailboxPattern("payroll", "Payroll", ["Corporate", "United States", "Canada", "Mexico", "Hourly", "Salaried", "Leadership", "Escalations", "Audits"]),
            new SharedMailboxPattern("accounts-payable", "Accounts Payable", ["Corporate", "Operations", "North America", "Canada", "Mexico", "Vendors", "Escalations", "Shared Services", "Travel"]),
            new SharedMailboxPattern("sales-ops", "Sales Operations", ["North America", "Strategic Accounts", "Channel", "Renewals", "Forecasting", "Deal Desk", "Commercial", "Escalations", "Leadership"]),
            new SharedMailboxPattern("recruiting", "Recruiting", ["Corporate", "Campus", "North America", "Manufacturing", "Technology", "Operations", "Executive", "Escalations", "Programs"]),
            new SharedMailboxPattern("facilities", "Facilities", ["Corporate", "Plant Operations", "North America", "Canada", "Mexico", "Real Estate", "Safety", "Escalations", "Projects"]),
            new SharedMailboxPattern("it-ops", "IT Operations", ["Service Desk", "Infrastructure", "Endpoint", "Identity", "North America", "Canada", "Mexico", "After Hours", "Escalations"])
        };

        var pattern = patterns[index % patterns.Length];
        var variantIndex = index / patterns.Length;
        if (variantIndex <= 0)
        {
            return new SharedMailboxBlueprint(pattern.LocalPart, pattern.DisplayName);
        }

        if (variantIndex - 1 < pattern.Qualifiers.Count)
        {
            var qualifier = pattern.Qualifiers[variantIndex - 1];
            return new SharedMailboxBlueprint(
                $"{pattern.LocalPart}-{Slug(qualifier)}",
                $"{pattern.DisplayName} - {qualifier}");
        }

        return new SharedMailboxBlueprint(
            $"{pattern.LocalPart}-shared-{variantIndex:00}",
            $"{pattern.DisplayName} - Shared Services {variantIndex:00}");
    }

    private List<DirectoryAccount> CreatePrivilegedAccounts(
        Company company,
        IReadOnlyList<Person> people,
        IReadOnlyList<DirectoryOrganizationalUnit> ous,
        string rootDomain,
        HashSet<string> issuedPasswords,
        ISet<string> issuedAccountUpns,
        ISet<string> issuedSamAccountNames,
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
            var upn = BuildUniqueDirectoryAccountUpn(localPart, rootDomain, issuedAccountUpns);
            var samAccountName = EnsureUniqueSamAccountName(Truncate($"adm_{Slug(person.LastName)}_{employeeSuffix}", 20), issuedSamAccountNames);
            var passwordLastSet = _clock.UtcNow.AddDays(-_randomSource.Next(1, 45));
            var lifecycle = CreateAccountLifecycle(passwordLastSet, 60, 1460, 7);

            return new DirectoryAccount
            {
                Id = _idFactory.Next("ACT"),
                CompanyId = company.Id,
                PersonId = person.Id,
                AccountType = "Privileged",
                DisplayName = BuildSecondaryAccountDisplayName(person, "Admin"),
                SamAccountName = samAccountName,
                UserPrincipalName = upn,
                Mail = null,
                Domain = rootDomain,
                DistinguishedName = $"CN={EscapeDn(person.DisplayName)} Admin,{targetOu.DistinguishedName}",
                OuId = targetOu.Id,
                Enabled = true,
                Privileged = true,
                MfaEnabled = _randomSource.NextDouble() >= 0.15,
                EmployeeId = BuildSecondaryEmployeeId(person.EmployeeId, "A"),
                GeneratedPassword = CreateUniquePassword(issuedPasswords, 20),
                PasswordProfile = "PrivilegedElevated",
                AdministrativeTier = tier,
                LastLogon = lifecycle.LastLogon,
                WhenCreated = lifecycle.WhenCreated,
                WhenModified = lifecycle.WhenModified,
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

    private AccountLifecycle CreateAccountLifecycle(
        DateTimeOffset passwordLastSet,
        int minCreatedAgeDays,
        int maxCreatedAgeDays,
        int maxLastLogonAgeDays)
    {
        var whenCreated = CreateHistoricalTimestamp(minCreatedAgeDays, maxCreatedAgeDays);
        if (whenCreated > passwordLastSet)
        {
            whenCreated = passwordLastSet.AddDays(-RandomInclusive(7, 120));
        }

        var lastLogon = CreateLastLogon(whenCreated, maxLastLogonAgeDays);
        var whenModified = CreateWhenModified(whenCreated, passwordLastSet, lastLogon);
        return new AccountLifecycle(lastLogon, whenCreated, whenModified);
    }

    private AccountLifecycle CreateExternalAccountLifecycle(
        DateTimeOffset passwordLastSet,
        DateTimeOffset? invitationSentAt,
        DateTimeOffset? invitationRedeemedAt,
        DateTimeOffset? lastAccessReviewAt,
        int minCreatedAgeDays,
        int maxCreatedAgeDays,
        int maxLastLogonAgeDays)
    {
        var whenCreated = invitationSentAt?.AddDays(-RandomInclusive(1, 30))
                          ?? CreateHistoricalTimestamp(minCreatedAgeDays, maxCreatedAgeDays);
        if (whenCreated > passwordLastSet)
        {
            whenCreated = passwordLastSet.AddDays(-RandomInclusive(3, 45));
        }

        DateTimeOffset? lastLogon = invitationSentAt is not null && invitationRedeemedAt is null
            ? null
            : CreateLastLogon(whenCreated, maxLastLogonAgeDays);
        if (invitationRedeemedAt is not null)
        {
            var latestPossibleLastLogonAge = Math.Min(
                maxLastLogonAgeDays,
                Math.Max(0, (int)Math.Floor((_clock.UtcNow - invitationRedeemedAt.Value).TotalDays)));
            lastLogon = _clock.UtcNow.AddDays(-RandomInclusive(0, latestPossibleLastLogonAge));
            if (lastLogon < invitationRedeemedAt)
            {
                lastLogon = invitationRedeemedAt;
            }
        }

        var whenModified = CreateWhenModified(
            whenCreated,
            passwordLastSet,
            lastLogon,
            invitationSentAt,
            invitationRedeemedAt,
            lastAccessReviewAt);
        return new AccountLifecycle(lastLogon, whenCreated, whenModified);
    }

    private DateTimeOffset CreateHistoricalTimestamp(int minAgeDays, int maxAgeDays)
        => _clock.UtcNow.AddDays(-RandomInclusive(minAgeDays, maxAgeDays));

    private DateTimeOffset CreateLastLogon(DateTimeOffset whenCreated, int maxLastLogonAgeDays)
    {
        var lastLogon = _clock.UtcNow.AddDays(-RandomInclusive(0, maxLastLogonAgeDays));
        if (lastLogon < whenCreated)
        {
            var ageWindow = Math.Max(0, (int)Math.Floor((_clock.UtcNow - whenCreated).TotalDays));
            lastLogon = whenCreated.AddDays(RandomInclusive(0, ageWindow));
        }

        return lastLogon;
    }

    private DateTimeOffset CreateWhenModified(DateTimeOffset whenCreated, params DateTimeOffset?[] candidateEvents)
    {
        var floor = candidateEvents
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .Append(whenCreated)
            .Max();
        var maxTailDays = Math.Min(45, Math.Max(0, (int)Math.Floor((_clock.UtcNow - floor).TotalDays)));
        return floor.AddDays(RandomInclusive(0, maxTailDays));
    }

    private int RandomInclusive(int minInclusive, int maxInclusive)
    {
        if (maxInclusive <= minInclusive)
        {
            return minInclusive;
        }

        return _randomSource.Next(minInclusive, maxInclusive + 1);
    }

    private sealed record AccountLifecycle(DateTimeOffset? LastLogon, DateTimeOffset WhenCreated, DateTimeOffset WhenModified);
    private sealed record SharedMailboxPattern(string LocalPart, string DisplayName, IReadOnlyList<string> Qualifiers);
    private sealed record SharedMailboxBlueprint(string LocalPart, string DisplayName);

    private List<DirectoryGroup> CreateGroups(
        Company company,
        IReadOnlyList<Department> departments,
        IReadOnlyList<Team> teams,
        IReadOnlyList<DirectoryAccount> accounts,
        IReadOnlyList<DirectoryOrganizationalUnit> ous,
        bool includeAdministrativeTiers)
    {
        var groupsOu = ous.First(o => o.Name == "Groups");
        var result = new List<DirectoryGroup>();

        foreach (var department in departments)
        {
            result.Add(CreateGroup(company, DepartmentUserGroupName(department), "Security", "Global", false, groupsOu, $"Baseline access for {department.Name}"));
            result.Add(CreateGroup(company, DepartmentDistributionGroupName(department), "Distribution", "Universal", true, groupsOu, $"Mail distribution for {department.Name}"));
            result.Add(CreateGroup(company, DepartmentLeadershipGroupName(department), "Distribution", "Universal", true, groupsOu, $"Management and leads for {department.Name}"));
            result.Add(CreateGroup(company, DepartmentFileAccessGroupName(department), "Security", "DomainLocal", false, groupsOu, $"{department.Name} departmental file share access"));
            result.Add(CreateGroup(company, DepartmentMailboxAccessGroupName(department), "Security", "DomainLocal", false, groupsOu, $"{department.Name} shared mailbox delegation"));
        }

        result.Add(CreateGroup(company, AllEmployeesSecurityGroupName(), "Security", "Global", false, groupsOu, "All employee baseline access"));
        result.Add(CreateGroup(company, "M365 All Employees", "M365", "Universal", true, groupsOu, "Collaboration membership"));
        result.Add(CreateGroup(company, AllEmployeesDistributionGroupName(), "Distribution", "Universal", true, groupsOu, "All employee announcements"));
        result.Add(CreateGroup(company, AllManagersDistributionGroupName(), "Distribution", "Universal", true, groupsOu, "Manager and leadership announcements"));
        result.Add(CreateGroup(company, ExternalContractorsGroupName(), "Security", "Global", false, groupsOu, "External contractor baseline access"));
        result.Add(CreateGroup(company, MspOperatorsGroupName(), "Security", "Global", false, groupsOu, "Managed service provider operator access"));
        result.Add(CreateGroup(company, B2BGuestsGroupName(), "Security", "Global", false, groupsOu, "B2B guest collaboration access"));
        result.Add(CreateGroup(company, "M365 Guest Collaboration", "M365", "Universal", true, groupsOu, "Guest collaboration membership"));
        result.Add(CreateGroup(company, VpnUsersGroupName(), "Security", "Global", false, groupsOu, "VPN user assignment"));
        result.Add(CreateGroup(company, CorpWifiUsersGroupName(), "Security", "Global", false, groupsOu, "Corporate Wi-Fi access"));
        result.Add(CreateGroup(company, OfficeUsersGroupName(), "Security", "Global", false, groupsOu, "Microsoft 365 and Office productivity user access"));
        result.Add(CreateGroup(company, OfficeAdminsGroupName(), "Security", "Global", false, groupsOu, "Microsoft 365 and Office administration"));
        result.Add(CreateGroup(company, ErpUsersGroupName(), "Security", "Global", false, groupsOu, "ERP user access"));
        result.Add(CreateGroup(company, ErpAdminsGroupName(), "Security", "Global", false, groupsOu, "ERP administration"));
        result.Add(CreateGroup(company, CrmUsersGroupName(), "Security", "Global", false, groupsOu, "CRM user access"));
        result.Add(CreateGroup(company, CrmAdminsGroupName(), "Security", "Global", false, groupsOu, "CRM administration"));
        result.Add(CreateGroup(company, HrisUsersGroupName(), "Security", "Global", false, groupsOu, "HRIS user access"));
        result.Add(CreateGroup(company, HrisAdminsGroupName(), "Security", "Global", false, groupsOu, "HRIS administration"));
        result.Add(CreateGroup(company, ItsmAgentsGroupName(), "Security", "Global", false, groupsOu, "ITSM analyst and agent access"));
        result.Add(CreateGroup(company, ItsmAdminsGroupName(), "Security", "Global", false, groupsOu, "ITSM administrator access"));
        result.Add(CreateGroup(company, BrowserPilotUsersGroupName(), "Security", "Global", false, groupsOu, "Enterprise browser pilot and staged compatibility users"));
        result.Add(CreateGroup(company, RemoteSupportOperatorsGroupName(), "Security", "Global", false, groupsOu, "Remote support and assistance operators"));
        result.Add(CreateGroup(company, ServerRemoteDesktopUsersGroupName(), "Security", "Global", false, groupsOu, "Authorized server remote desktop users"));
        result.Add(CreateGroup(company, ServerRemoteDesktopAdminsGroupName(), "Security", "Global", false, groupsOu, "Server remote desktop administrators"));
        result.Add(CreateGroup(company, SqlAdminsGroupName(), "Security", "Global", false, groupsOu, "Database administrators"));
        result.Add(CreateGroup(company, BackupOperatorsGroupName(), "Security", "Global", false, groupsOu, "Backup and restore operators"));
        result.Add(CreateGroup(company, GroupPolicyEditorsGroupName(), "Security", "Global", false, groupsOu, "Group Policy editors and reviewers"));
        result.Add(CreateGroup(company, LapsReadersGroupName(), "Security", "Global", false, groupsOu, "LAPS password readers"));
        result.Add(CreateGroup(company, PasswordResetOperatorsGroupName(), "Security", "Global", false, groupsOu, "Password reset operators"));
        result.Add(CreateGroup(company, WorkstationJoinOperatorsGroupName(), "Security", "Global", false, groupsOu, "Delegated workstation join operators"));
        result.Add(CreateGroup(company, PrintOperatorsGroupName(), "Security", "Global", false, groupsOu, "Printer and queue operators"));

        foreach (var team in teams)
        {
            var department = departments.FirstOrDefault(candidate => candidate.Id == team.DepartmentId);
            result.Add(CreateGroup(company, TeamUserGroupName(department, team), "Security", "Global", false, groupsOu, $"Team access for {team.Name}"));
            result.Add(CreateGroup(company, TeamDistributionGroupName(department, team), "Distribution", "Universal", true, groupsOu, $"Mail distribution for {team.Name}"));
        }

        foreach (var sharedAccount in accounts.Where(account => account.CompanyId == company.Id && account.AccountType == "Shared"))
        {
            result.Add(CreateGroup(company, SharedMailboxAccessGroupName(sharedAccount), "Security", "DomainLocal", false, groupsOu, $"Delegated access for shared mailbox {sharedAccount.UserPrincipalName}"));
        }

        if (includeAdministrativeTiers)
        {
            result.Add(CreateGroup(company, PrivilegedAccessGroupName(), "Security", "Universal", false, groupsOu, "Umbrella privileged access group", "Tier0"));
            result.Add(CreateGroup(company, Tier0IdentityAdminsGroupName(), "Security", "Global", false, groupsOu, "Tier 0 identity administrators", "Tier0"));
            result.Add(CreateGroup(company, Tier0PawUsersGroupName(), "Security", "Global", false, groupsOu, "Tier 0 privileged access workstation users", "Tier0"));
            result.Add(CreateGroup(company, Tier0PawDevicesGroupName(), "Security", "Global", false, groupsOu, "Tier 0 privileged access workstation devices", "Tier0"));
            result.Add(CreateGroup(company, Tier1ServerAdminsGroupName(), "Security", "Global", false, groupsOu, "Tier 1 server administrators", "Tier1"));
            result.Add(CreateGroup(company, Tier1WorkstationAdminsGroupName(), "Security", "Global", false, groupsOu, "Tier 1 workstation administrators", "Tier1"));
            result.Add(CreateGroup(company, Tier1PawUsersGroupName(), "Security", "Global", false, groupsOu, "Tier 1 privileged access workstation users", "Tier1"));
            result.Add(CreateGroup(company, Tier1PawDevicesGroupName(), "Security", "Global", false, groupsOu, "Tier 1 privileged access workstation devices", "Tier1"));
            result.Add(CreateGroup(company, Tier1ManagedWorkstationsGroupName(), "Security", "Global", false, groupsOu, "Tier 1 managed workstation computer objects", "Tier1"));
            result.Add(CreateGroup(company, Tier1ManagedServersGroupName(), "Security", "Global", false, groupsOu, "Tier 1 managed server computer objects", "Tier1"));
            result.Add(CreateGroup(company, Tier2HelpdeskGroupName(), "Security", "Global", false, groupsOu, "Tier 2 helpdesk operators", "Tier2"));
            result.Add(CreateGroup(company, Tier2ApplicationSupportGroupName(), "Security", "Global", false, groupsOu, "Tier 2 application support", "Tier2"));
        }

        return result;
    }

    private List<DirectoryGroupMembership> CreateMemberships(
        Company company,
        IReadOnlyList<Department> departments,
        IReadOnlyList<Team> teams,
        IReadOnlyList<Person> people,
        IReadOnlyList<DirectoryGroup> groups,
        IReadOnlyList<DirectoryAccount> accounts,
        bool includeAdministrativeTiers)
    {
        var results = new List<DirectoryGroupMembership>();
        var userAccounts = accounts.Where(a => a.CompanyId == company.Id && (a.AccountType == "User" || a.AccountType == "Contractor")).ToList();
        var privilegedAccounts = accounts.Where(a => a.CompanyId == company.Id && a.AccountType == "Privileged").ToList();
        var sharedAccounts = accounts.Where(a => a.CompanyId == company.Id && a.AccountType == "Shared").ToList();

        var allEmployeesGroup = FindGroup(groups, company.Id, AllEmployeesSecurityGroupName());
        var m365Group = FindGroup(groups, company.Id, "M365 All Employees");
        var allEmployeesDl = FindGroup(groups, company.Id, AllEmployeesDistributionGroupName());
        var allManagersDl = FindGroup(groups, company.Id, AllManagersDistributionGroupName());
        var vpnUsers = FindGroup(groups, company.Id, VpnUsersGroupName());
        var wifiUsers = FindGroup(groups, company.Id, CorpWifiUsersGroupName());
        var officeUsers = FindGroup(groups, company.Id, OfficeUsersGroupName());
        var browserPilotUsers = FindGroup(groups, company.Id, BrowserPilotUsersGroupName());
        var remoteSupportOperators = FindGroup(groups, company.Id, RemoteSupportOperatorsGroupName());
        var rdpUsers = FindGroup(groups, company.Id, ServerRemoteDesktopUsersGroupName());
        var rdpAdmins = FindGroup(groups, company.Id, ServerRemoteDesktopAdminsGroupName());
        var sqlAdmins = FindGroup(groups, company.Id, SqlAdminsGroupName());
        var backupOperators = FindGroup(groups, company.Id, BackupOperatorsGroupName());
        var gpoEditors = FindGroup(groups, company.Id, GroupPolicyEditorsGroupName());
        var lapsReaders = FindGroup(groups, company.Id, LapsReadersGroupName());
        var passwordResetOperators = FindGroup(groups, company.Id, PasswordResetOperatorsGroupName());
        var workstationJoiners = FindGroup(groups, company.Id, WorkstationJoinOperatorsGroupName());
        var printOperators = FindGroup(groups, company.Id, PrintOperatorsGroupName());
        var erpUsers = FindGroup(groups, company.Id, ErpUsersGroupName());
        var erpAdmins = FindGroup(groups, company.Id, ErpAdminsGroupName());
        var crmUsers = FindGroup(groups, company.Id, CrmUsersGroupName());
        var crmAdmins = FindGroup(groups, company.Id, CrmAdminsGroupName());
        var hrisUsers = FindGroup(groups, company.Id, HrisUsersGroupName());
        var hrisAdmins = FindGroup(groups, company.Id, HrisAdminsGroupName());
        var itsmAgents = FindGroup(groups, company.Id, ItsmAgentsGroupName());
        var itsmAdmins = FindGroup(groups, company.Id, ItsmAdminsGroupName());

        foreach (var account in userAccounts)
        {
            AddMembershipIfPresent(results, m365Group, account.Id, "Account");
            AddMembershipIfPresent(results, allEmployeesDl, account.Id, "Account");
            AddMembershipIfPresent(results, vpnUsers, account.Id, "Account");
            AddMembershipIfPresent(results, wifiUsers, account.Id, "Account");
            AddMembershipIfPresent(results, officeUsers, account.Id, "Account");
        }

        foreach (var department in departments)
        {
            var sg = FindGroup(groups, company.Id, DepartmentUserGroupName(department));
            var dl = FindGroup(groups, company.Id, DepartmentDistributionGroupName(department));
            var leadershipDl = FindGroup(groups, company.Id, DepartmentLeadershipGroupName(department));
            var fileShareGroup = FindGroup(groups, company.Id, DepartmentFileAccessGroupName(department));
            var mailboxAccessGroup = FindGroup(groups, company.Id, DepartmentMailboxAccessGroupName(department));

            if (allEmployeesGroup is not null && sg is not null)
            {
                results.Add(CreateMembership(allEmployeesGroup.Id, sg.Id, "Group"));
            }

            AddMembershipIfPresent(results, fileShareGroup, sg?.Id, "Group");
            AddMembershipIfPresent(results, mailboxAccessGroup, sg?.Id, "Group");
            AddDepartmentResourceMemberships(results, department, sg, groups, company.Id);

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

                var title = person.Title ?? string.Empty;
                if (IsLeadershipTitle(title))
                {
                    AddMembershipIfPresent(results, leadershipDl, account.Id, "Account");
                    AddMembershipIfPresent(results, allManagersDl, account.Id, "Account");
                }

                if (LooksLikePilotUser(person))
                {
                    AddMembershipIfPresent(results, browserPilotUsers, account.Id, "Account");
                }

                if (LooksLikeSupportUser(person))
                {
                    AddMembershipIfPresent(results, remoteSupportOperators, account.Id, "Account");
                    AddMembershipIfPresent(results, passwordResetOperators, account.Id, "Account");
                    AddMembershipIfPresent(results, printOperators, account.Id, "Account");
                }

                if (LooksLikeServerAdmin(title))
                {
                    AddMembershipIfPresent(results, rdpUsers, account.Id, "Account");
                }
            }
        }

        foreach (var team in teams)
        {
            var department = departments.FirstOrDefault(candidate => candidate.Id == team.DepartmentId);
            var teamSg = FindGroup(groups, company.Id, TeamUserGroupName(department, team));
            var teamDl = FindGroup(groups, company.Id, TeamDistributionGroupName(department, team));

            foreach (var person in people.Where(p => p.CompanyId == company.Id && p.TeamId == team.Id))
            {
                var account = userAccounts.FirstOrDefault(a => a.PersonId == person.Id);
                if (account is null)
                {
                    continue;
                }

                AddMembershipIfPresent(results, teamSg, account.Id, "Account");
                AddMembershipIfPresent(results, teamDl, account.Id, "Account");
            }
        }

        foreach (var sharedAccount in sharedAccounts)
        {
            var mailboxGroup = FindGroup(groups, company.Id, SharedMailboxAccessGroupName(sharedAccount));
            if (mailboxGroup is null)
            {
                continue;
            }

            foreach (var departmentGroup in ResolveSharedMailboxDepartmentGroups(sharedAccount, groups, departments, company.Id))
            {
                AddMembershipIfPresent(results, mailboxGroup, departmentGroup.Id, "Group");
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
            var privilegedUmbrella = FindGroup(groups, company.Id, PrivilegedAccessGroupName());
            var tier0 = FindGroup(groups, company.Id, Tier0IdentityAdminsGroupName());
            var tier0Paw = FindGroup(groups, company.Id, Tier0PawUsersGroupName());
            var tier1Server = FindGroup(groups, company.Id, Tier1ServerAdminsGroupName());
            var tier1Workstation = FindGroup(groups, company.Id, Tier1WorkstationAdminsGroupName());
            var tier1Paw = FindGroup(groups, company.Id, Tier1PawUsersGroupName());
            var tier2Helpdesk = FindGroup(groups, company.Id, Tier2HelpdeskGroupName());
            var tier2AppSupport = FindGroup(groups, company.Id, Tier2ApplicationSupportGroupName());

            foreach (var adminGroup in new[] { tier0, tier0Paw, tier1Server, tier1Workstation, tier1Paw, tier2Helpdesk, tier2AppSupport })
            {
                if (privilegedUmbrella is not null && adminGroup is not null)
                {
                    results.Add(CreateMembership(privilegedUmbrella.Id, adminGroup.Id, "Group"));
                }
            }

            AddMembershipIfPresent(results, gpoEditors, tier1Workstation?.Id, "Group");
            AddMembershipIfPresent(results, gpoEditors, tier1Server?.Id, "Group");
            AddMembershipIfPresent(results, gpoEditors, tier0?.Id, "Group");
            AddMembershipIfPresent(results, lapsReaders, tier1Workstation?.Id, "Group");
            AddMembershipIfPresent(results, lapsReaders, tier2Helpdesk?.Id, "Group");
            AddMembershipIfPresent(results, passwordResetOperators, tier2Helpdesk?.Id, "Group");
            AddMembershipIfPresent(results, workstationJoiners, tier1Workstation?.Id, "Group");
            AddMembershipIfPresent(results, remoteSupportOperators, tier2Helpdesk?.Id, "Group");
            AddMembershipIfPresent(results, backupOperators, tier1Server?.Id, "Group");
            AddMembershipIfPresent(results, rdpAdmins, tier1Server?.Id, "Group");
            AddMembershipIfPresent(results, sqlAdmins, tier1Server?.Id, "Group");
            AddMembershipIfPresent(results, itsmAdmins, tier2AppSupport?.Id, "Group");

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

        foreach (var account in userAccounts)
        {
            var person = people.FirstOrDefault(candidate => candidate.Id == account.PersonId);
            if (person is null)
            {
                continue;
            }

            if (MatchesDepartment(person, departments, "finance", "accounting", "payroll", "procurement"))
            {
                AddMembershipIfPresent(results, erpUsers, account.Id, "Account");
            }

            if (MatchesDepartment(person, departments, "human resources", "hr", "people"))
            {
                AddMembershipIfPresent(results, hrisUsers, account.Id, "Account");
            }

            if (MatchesDepartment(person, departments, "sales", "marketing", "customer", "commercial"))
            {
                AddMembershipIfPresent(results, crmUsers, account.Id, "Account");
            }

            if (MatchesDepartment(person, departments, "information technology", "it", "security", "operations", "engineering", "platform", "infrastructure"))
            {
                AddMembershipIfPresent(results, itsmAgents, account.Id, "Account");
            }

            if (IsLeadershipTitle(person.Title ?? string.Empty))
            {
                AddMembershipIfPresent(results, erpAdmins, account.Id, "Account");
                if (MatchesDepartment(person, departments, "sales", "marketing"))
                {
                    AddMembershipIfPresent(results, crmAdmins, account.Id, "Account");
                }

                if (MatchesDepartment(person, departments, "human resources", "hr", "people"))
                {
                    AddMembershipIfPresent(results, hrisAdmins, account.Id, "Account");
                }
            }
        }

        return results
            .DistinctBy(membership => $"{membership.GroupId}|{membership.MemberObjectId}|{membership.MemberObjectType}", StringComparer.OrdinalIgnoreCase)
            .ToList();
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
            EnsureExternalOrganization(world, company, "NorthBridge Supply Alliance", "Partner", country, ownerDepartmentId, "B2BPartner", "Medium", "northbridgesupply.example")
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
        if (string.Equals(name, company.Name, StringComparison.OrdinalIgnoreCase))
        {
            name = $"{name} {country} {relationshipType switch
            {
                "Vendor" => "Supply Network",
                "ManagedServiceProvider" => "Services",
                _ => "Partner Services"
            }}";
        }

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
            RelationshipBasis = relationshipType switch
            {
                "ManagedServiceProvider" => "ManagedService",
                "StaffingPartner" => "ContractedLabor",
                "Partner" => "BusinessPartnership",
                _ => "ExternalWorkforce"
            },
            RelationshipScope = relationshipType switch
            {
                "ManagedServiceProvider" => "Enterprise",
                "StaffingPartner" => "Department",
                _ => "BusinessUnit"
            },
            RelationshipDefinition = relationshipType switch
            {
                "ManagedServiceProvider" => $"{name} provides managed services supporting {company.Name} operations.",
                "StaffingPartner" => $"{name} supplies temporary contracted labor to support {company.Name}.",
                "Partner" => $"{name} collaborates with {company.Name} on external workforce operations.",
                _ => $"{name} participates in the extended workforce supporting {company.Name}."
            },
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
        string rootDomain,
        CatalogSet catalogs)
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
        var nameCountries = definition.Countries
            .Concat(externalOrganizations.Select(organization => organization.Country))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var firstNames = BuildExternalFirstNamePool(catalogs, nameCountries, employees);
        var lastNames = BuildExternalLastNamePool(catalogs, nameCountries, employees);
        var issuedPersonUpns = new HashSet<string>(
            employees.Select(employee => employee.UserPrincipalName).Where(value => !string.IsNullOrWhiteSpace(value)),
            StringComparer.OrdinalIgnoreCase);
        var issuedDisplayNames = new HashSet<string>(
            employees.Select(employee => employee.DisplayName).Where(value => !string.IsNullOrWhiteSpace(value)),
            StringComparer.OrdinalIgnoreCase);

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
                issuedPersonUpns,
                issuedDisplayNames));
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
                issuedPersonUpns,
                issuedDisplayNames));
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
                issuedPersonUpns,
                issuedDisplayNames));
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
        ISet<string> issuedPersonUpns,
        ISet<string> issuedDisplayNames)
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
            var departmentTeams = targetTeams
                .Where(team => string.Equals(team.DepartmentId, department.Id, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var teamPool = departmentTeams.Count > 0 ? departmentTeams : targetTeams;
            var team = teamPool[i % teamPool.Count];
            var sponsorPool = employees
                .Where(person => string.Equals(person.TeamId, team.Id, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (sponsorPool.Count == 0)
            {
                sponsorPool = employees
                    .Where(person => string.Equals(person.DepartmentId, team.DepartmentId, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (sponsorPool.Count == 0)
            {
                sponsorPool = employees.ToList();
            }

            var sponsor = SelectExternalSponsor(sponsorPool);
            var workerNumber = employmentType switch
            {
                "ManagedServiceProvider" => $"MSP-{i + 1:0000}",
                "Guest" => $"GST-{i + 1:0000}",
                _ => $"CNT-{i + 1:0000}"
            };
            var office = SelectExternalOffice(offices, employer.Country, sponsor.OfficeId);
            var (firstName, lastName, displayName) = BuildUniqueExternalDisplayName(
                firstNames,
                lastNames,
                issuedDisplayNames,
                sponsor,
                i,
                employmentType,
                employer.Name,
                workerNumber);
            var personUpn = BuildUniqueExternalPersonUpn(firstName, lastName, employmentType, employer, workerNumber, rootDomain, issuedPersonUpns);

            results.Add(new Person
            {
                Id = _idFactory.Next("PERS"),
                CompanyId = company.Id,
                TeamId = team.Id,
                DepartmentId = team.DepartmentId,
                FirstName = firstName,
                LastName = lastName,
                DisplayName = displayName,
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

    private static Person SelectExternalSponsor(IReadOnlyList<Person> sponsorPool)
    {
        return sponsorPool
            .OrderBy(person => GetExternalSponsorPriority(person.Title))
            .ThenBy(person => person.LastName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(person => person.FirstName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(person => person.EmployeeId, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private static int GetExternalSponsorPriority(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return 5;
        }

        if (title.Contains("Manager", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (title.Contains("Director", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (title.Contains("Vice President", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (title.Contains("Chief Executive Officer", StringComparison.OrdinalIgnoreCase))
        {
            return 4;
        }

        if (title.Contains("Lead", StringComparison.OrdinalIgnoreCase)
            || title.Contains("Principal", StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        return 5;
    }

    private IReadOnlyList<string> BuildExternalFirstNamePool(
        CatalogSet catalogs,
        IReadOnlyCollection<string> countries,
        IReadOnlyList<Person> employees)
    {
        var catalogNames = ReadCatalogNameValues(catalogs, "first_names_country", "Name", countries).ToList();
        if (catalogNames.Count == 0)
        {
            catalogNames.AddRange(ReadCatalogNameValues(catalogs, "first_names_gendered", "Name", Array.Empty<string>()));
        }

        if (catalogNames.Count == 0)
        {
            catalogNames.AddRange(ReadCatalogNameValues(catalogs, "given_names_male", "Name", countries));
            catalogNames.AddRange(ReadCatalogNameValues(catalogs, "given_names_female", "Name", countries));
        }

        catalogNames.AddRange(employees.Select(employee => employee.FirstName));
        catalogNames = catalogNames
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (catalogNames.Count == 0)
        {
            catalogNames.AddRange(new[] { "Alex", "Jordan", "Taylor", "Morgan", "Casey", "Avery" });
        }

        return catalogNames;
    }

    private IReadOnlyList<string> BuildExternalLastNamePool(
        CatalogSet catalogs,
        IReadOnlyCollection<string> countries,
        IReadOnlyList<Person> employees)
    {
        var catalogNames = ReadCatalogNameValues(catalogs, "last_names_country", "Name", countries).ToList();
        if (catalogNames.Count == 0)
        {
            catalogNames.AddRange(ReadCatalogNameValues(catalogs, "surnames_reference", "Value", Array.Empty<string>()));
        }

        catalogNames.AddRange(employees.Select(employee => employee.LastName));
        catalogNames = catalogNames
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (catalogNames.Count == 0)
        {
            catalogNames.AddRange(new[] { "Carter", "Patel", "Reed", "Nguyen", "Brooks", "Sullivan" });
        }

        return catalogNames;
    }

    private static IReadOnlyList<string> ReadCatalogNameValues(
        CatalogSet catalogs,
        string catalogName,
        string fieldName,
        IReadOnlyCollection<string> countries)
    {
        if (!catalogs.CsvCatalogs.TryGetValue(catalogName, out var rows))
        {
            return Array.Empty<string>();
        }

        var countryFilter = countries
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return rows
            .Where(row => countryFilter.Count == 0
                          || !row.TryGetValue("Country", out var country)
                          || string.IsNullOrWhiteSpace(country)
                          || countryFilter.Contains(country))
            .Select(row => row.TryGetValue(fieldName, out var value) ? value : null)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private (string FirstName, string LastName, string DisplayName) BuildUniqueExternalDisplayName(
        IReadOnlyList<string> firstNames,
        IReadOnlyList<string> lastNames,
        ISet<string> issuedDisplayNames,
        Person sponsor,
        int ordinal,
        string employmentType,
        string employerName,
        string workerNumber)
    {
        for (var attempt = 0; attempt < 48; attempt++)
        {
            var firstName = firstNames[_randomSource.Next(firstNames.Count)];
            var lastName = lastNames[_randomSource.Next(lastNames.Count)];
            var displayName = $"{firstName} {lastName}";
            if (issuedDisplayNames.Add(displayName))
            {
                return (firstName, lastName, displayName);
            }
        }

        var firstSeed = Math.Abs(HashCode.Combine(ordinal, employmentType, employerName));
        var lastSeed = Math.Abs(HashCode.Combine(ordinal, sponsor.Id, employerName));

        for (var firstOffset = 0; firstOffset < firstNames.Count; firstOffset++)
        {
            var firstName = firstNames[(firstSeed + firstOffset) % firstNames.Count];
            for (var lastOffset = 0; lastOffset < lastNames.Count; lastOffset++)
            {
                var lastName = lastNames[(lastSeed + lastOffset) % lastNames.Count];
                var displayName = $"{firstName} {lastName}";
                if (issuedDisplayNames.Add(displayName))
                {
                    return (firstName, lastName, displayName);
                }
            }
        }

        var fallbackFirst = firstNames[firstSeed % firstNames.Count];
        var fallbackLast = lastNames[lastSeed % lastNames.Count];
        var fallbackDisplayName = $"{fallbackFirst} {fallbackLast} {workerNumber}";
        _ = issuedDisplayNames.Add(fallbackDisplayName);
        return (fallbackFirst, fallbackLast, fallbackDisplayName);
    }

    private List<DirectoryAccount> CreateExternalAccounts(
        Company company,
        IReadOnlyList<Person> externalPeople,
        IReadOnlyList<ExternalOrganization> externalOrganizations,
        IReadOnlyList<DirectoryAccount> existingAccounts,
        IReadOnlyList<DirectoryOrganizationalUnit> ous,
        HashSet<string> issuedPasswords,
        ISet<string> issuedAccountUpns,
        ISet<string> issuedSamAccountNames,
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
            var upn = BuildUniqueDirectoryAccountUpn(localPart, rootDomain, issuedAccountUpns);
            var samAccountName = EnsureUniqueSamAccountName(BuildExternalSam(person, accountType), issuedSamAccountNames);
            var targetOu = accountType switch
            {
                "ManagedServiceProvider" => managedServicesOu,
                "Guest" => guestsOu,
                _ => contractorsOu
            };
            var lifecycle = CreateExternalAccountLifecycle(
                passwordLastSet,
                invitationSentAt,
                invitationRedeemedAt,
                lastAccessReviewAt,
                30,
                1095,
                accountType == "ManagedServiceProvider" ? 7 : 21);

            results.Add(new DirectoryAccount
            {
                Id = _idFactory.Next("ACT"),
                CompanyId = company.Id,
                PersonId = person.Id,
                AccountType = accountType,
                DisplayName = person.DisplayName,
                SamAccountName = samAccountName,
                UserPrincipalName = upn,
                Mail = accountType == "Guest" ? null : upn,
                Domain = rootDomain,
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
                LastLogon = lifecycle.LastLogon,
                WhenCreated = lifecycle.WhenCreated,
                WhenModified = lifecycle.WhenModified,
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
            CreateGroup(company, ExternalContractorsGroupName(), "Security", "Global", false, groupsOu, "External contractor baseline access"),
            CreateGroup(company, MspOperatorsGroupName(), "Security", "Global", false, groupsOu, "Managed service provider operator access"),
            CreateGroup(company, B2BGuestsGroupName(), "Security", "Global", false, groupsOu, "B2B guest collaboration access"),
            CreateGroup(company, "M365 Guest Collaboration", "M365", "Universal", true, groupsOu, "Guest collaboration membership")
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
        var contractorsGroup = FindGroup(groups, company.Id, ExternalContractorsGroupName());
        var mspGroup = FindGroup(groups, company.Id, MspOperatorsGroupName());
        var guestsGroup = FindGroup(groups, company.Id, B2BGuestsGroupName());
        var guestCollaboration = FindGroup(groups, company.Id, "M365 Guest Collaboration");
        var allEmployeesGroup = FindGroup(groups, company.Id, AllEmployeesSecurityGroupName());
        var m365AllEmployees = FindGroup(groups, company.Id, "M365 All Employees");

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
        string? sourceEntityId = null,
        string status = "Enabled")
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
            Status = status,
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
        string? sourceReference = null,
        string? policyPath = null,
        string? registryPath = null)
    {
        if (world.PolicySettings.Any(setting =>
                setting.CompanyId == companyId
                && string.Equals(setting.PolicyId, policyId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(setting.SettingName, settingName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var metadata = ResolvePolicySettingMetadata(
            world,
            companyId,
            policyId,
            settingName,
            settingCategory,
            policyPath,
            registryPath);
        var source = ResolvePolicySettingSource(world, policyId, settingCategory, metadata.PolicyPath, metadata.RegistryPath);
        var behavior = ResolvePolicySettingBehavior(settingCategory, metadata.PolicyPath, metadata.RegistryPath);

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
            Source = source,
            Behavior = behavior,
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

    private (string PolicyPath, string? RegistryPath) ResolvePolicySettingMetadata(
        SyntheticEnterpriseWorld world,
        string companyId,
        string policyId,
        string settingName,
        string settingCategory,
        string? explicitPolicyPath,
        string? explicitRegistryPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPolicyPath))
        {
            return (explicitPolicyPath, explicitRegistryPath);
        }

        var policy = world.Policies.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, policyId, StringComparison.OrdinalIgnoreCase));
        var company = world.Companies.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, companyId, StringComparison.OrdinalIgnoreCase));

        if (policy is null)
        {
            return (BuildCustomPolicyRegistryPath(company, false, "UnknownPolicy", settingCategory, settingName), explicitRegistryPath);
        }

        if (string.Equals(policy.PolicyType, "ConditionalAccessPolicy", StringComparison.OrdinalIgnoreCase))
        {
            return ($"Microsoft Entra ID\\Protection\\Conditional Access\\Policies\\{policy.Name}", null);
        }

        if (string.Equals(policy.Platform, "Intune", StringComparison.OrdinalIgnoreCase)
            || string.Equals(policy.PolicyType, "IntuneConfigurationProfile", StringComparison.OrdinalIgnoreCase)
            || string.Equals(policy.PolicyType, "IntuneCompliancePolicy", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveIntunePolicySettingMetadata(policy, settingName, settingCategory);
        }

        if (string.Equals(policy.Platform, "EntraID", StringComparison.OrdinalIgnoreCase)
            && string.Equals(settingCategory, "CrossTenantAccess", StringComparison.OrdinalIgnoreCase))
        {
            return ($"Microsoft Entra ID\\External Identities\\Cross-tenant access settings\\{policy.Name}", null);
        }

        if (string.Equals(policy.Platform, "ActiveDirectory", StringComparison.OrdinalIgnoreCase)
            || string.Equals(policy.PolicyType, "GroupPolicyObject", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveGroupPolicySettingMetadata(policy, company, settingName, settingCategory);
        }

        var fallbackRegistryPath = BuildCustomPolicyRegistryPath(company, IsUserScopedPolicySetting(policy, settingCategory, settingName), policy.Name, settingCategory, settingName);
        return (fallbackRegistryPath, fallbackRegistryPath);
    }

    private static string ResolvePolicySettingSource(
        SyntheticEnterpriseWorld world,
        string policyId,
        string settingCategory,
        string policyPath,
        string? registryPath)
    {
        var policy = world.Policies.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, policyId, StringComparison.OrdinalIgnoreCase));
        if (policy is null)
        {
            return "CustomProfile";
        }

        if (string.Equals(policy.Platform, "Intune", StringComparison.OrdinalIgnoreCase)
            || string.Equals(policy.PolicyType, "IntuneConfigurationProfile", StringComparison.OrdinalIgnoreCase)
            || string.Equals(policy.PolicyType, "IntuneCompliancePolicy", StringComparison.OrdinalIgnoreCase)
            || string.Equals(policy.Platform, "EntraID", StringComparison.OrdinalIgnoreCase))
        {
            return "CustomProfile";
        }

        if (string.Equals(settingCategory, "AuditPolicy", StringComparison.OrdinalIgnoreCase))
        {
            return "AuditCsv";
        }

        if (policyPath.Contains("Security Settings", StringComparison.OrdinalIgnoreCase)
            || policyPath.Contains("Account Policies", StringComparison.OrdinalIgnoreCase)
            || string.Equals(settingCategory, "UserRightsAssignment", StringComparison.OrdinalIgnoreCase))
        {
            return "SecTemplate";
        }

        if (string.Equals(settingCategory, "DriveMappings", StringComparison.OrdinalIgnoreCase)
            || string.Equals(settingCategory, "Printers", StringComparison.OrdinalIgnoreCase)
            || string.Equals(settingCategory, "Shortcuts", StringComparison.OrdinalIgnoreCase))
        {
            return "GPP";
        }

        if (!string.IsNullOrWhiteSpace(registryPath) || policyPath.Contains("Administrative Templates", StringComparison.OrdinalIgnoreCase))
        {
            return "GPO";
        }

        return "CustomProfile";
    }

    private static string ResolvePolicySettingBehavior(
        string settingCategory,
        string policyPath,
        string? registryPath)
    {
        if (!string.IsNullOrWhiteSpace(registryPath))
        {
            return registryPath.Contains("\\Policies\\", StringComparison.OrdinalIgnoreCase)
                ? "BlueDot"
                : "RedDot";
        }

        if (policyPath.Contains("Administrative Templates", StringComparison.OrdinalIgnoreCase))
        {
            return "BlueDot";
        }

        if (policyPath.Contains("Security Settings", StringComparison.OrdinalIgnoreCase)
            || policyPath.Contains("Account Policies", StringComparison.OrdinalIgnoreCase)
            || string.Equals(settingCategory, "UserRightsAssignment", StringComparison.OrdinalIgnoreCase)
            || string.Equals(settingCategory, "AuditPolicy", StringComparison.OrdinalIgnoreCase))
        {
            return "RedDot";
        }

        return "Unknown";
    }

    private (string PolicyPath, string? RegistryPath) ResolveGroupPolicySettingMetadata(
        PolicyRecord policy,
        Company? company,
        string settingName,
        string settingCategory)
    {
        if (string.Equals(policy.Name, "Default Domain Policy", StringComparison.OrdinalIgnoreCase)
            || string.Equals(policy.Name, "Windows Account and Lockout Hardening", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(settingCategory, "PasswordPolicy", StringComparison.OrdinalIgnoreCase))
            {
                return ("Computer Configuration\\Windows Settings\\Account Policies\\Password Policy", null);
            }

            if (string.Equals(settingCategory, "LockoutPolicy", StringComparison.OrdinalIgnoreCase))
            {
                return ("Computer Configuration\\Windows Settings\\Account Policies\\Account Lockout Policy", null);
            }

            if (settingName.StartsWith("Kerberos", StringComparison.OrdinalIgnoreCase))
            {
                return ("Computer Configuration\\Windows Settings\\Local Policies\\Kerberos Policy", null);
            }

            if (string.Equals(settingCategory, "InteractiveLogon", StringComparison.OrdinalIgnoreCase)
                || string.Equals(settingCategory, "IdentityLifecycle", StringComparison.OrdinalIgnoreCase)
                || settingName.StartsWith("InteractiveLogon", StringComparison.OrdinalIgnoreCase)
                || settingName.StartsWith("Rename", StringComparison.OrdinalIgnoreCase)
                || settingName.StartsWith("Accounts", StringComparison.OrdinalIgnoreCase)
                || settingName.StartsWith("NetworkSecurity", StringComparison.OrdinalIgnoreCase)
                || settingName.StartsWith("MicrosoftNetwork", StringComparison.OrdinalIgnoreCase)
                || settingName.StartsWith("DomainMember", StringComparison.OrdinalIgnoreCase)
                || settingName.StartsWith("CachedLogons", StringComparison.OrdinalIgnoreCase)
                || settingName.StartsWith("BlockMicrosoftAccounts", StringComparison.OrdinalIgnoreCase))
            {
                return ("Computer Configuration\\Windows Settings\\Security Settings\\Local Policies\\Security Options", ResolveRegistryPath(policy, settingName, settingCategory));
            }

            return ("Computer Configuration\\Windows Settings\\Security Settings\\Local Policies\\Security Options", ResolveRegistryPath(policy, settingName, settingCategory));
        }

        if (string.Equals(policy.Name, "Windows User Rights Assignment Baseline", StringComparison.OrdinalIgnoreCase)
            || string.Equals(policy.Name, "Delegated Administration Controls", StringComparison.OrdinalIgnoreCase)
            || string.Equals(settingCategory, "UserRightsAssignment", StringComparison.OrdinalIgnoreCase))
        {
            return ("Computer Configuration\\Windows Settings\\Security Settings\\Local Policies\\User Rights Assignment", null);
        }

        if (string.Equals(policy.Name, "Windows Security Options Baseline", StringComparison.OrdinalIgnoreCase))
        {
            return ("Computer Configuration\\Windows Settings\\Security Settings\\Local Policies\\Security Options", ResolveRegistryPath(policy, settingName, settingCategory));
        }

        if (string.Equals(policy.Name, "Windows Advanced Audit Baseline", StringComparison.OrdinalIgnoreCase)
            || string.Equals(policy.Name, "Security Audit and Logging Baseline", StringComparison.OrdinalIgnoreCase)
            || string.Equals(settingCategory, "AuditPolicy", StringComparison.OrdinalIgnoreCase))
        {
            if (settingName.Contains("PowerShell", StringComparison.OrdinalIgnoreCase))
            {
                return ("Computer Configuration\\Administrative Templates\\Windows Components\\Windows PowerShell", ResolveRegistryPath(policy, settingName, settingCategory));
            }

            return ("Computer Configuration\\Windows Settings\\Security Settings\\Advanced Audit Policy Configuration\\Audit Policies", null);
        }

        if (string.Equals(policy.Name, "Windows Defender and ASR Baseline", StringComparison.OrdinalIgnoreCase))
        {
            if (settingName.Contains("AttackSurfaceReduction", StringComparison.OrdinalIgnoreCase)
                || settingName.Contains("ExploitGuard", StringComparison.OrdinalIgnoreCase))
            {
                return ("Computer Configuration\\Administrative Templates\\Windows Components\\Microsoft Defender Antivirus\\Microsoft Defender Exploit Guard\\Attack Surface Reduction", ResolveRegistryPath(policy, settingName, settingCategory));
            }

            return ("Computer Configuration\\Administrative Templates\\Windows Components\\Microsoft Defender Antivirus", ResolveRegistryPath(policy, settingName, settingCategory));
        }

        if (string.Equals(policy.Name, "Windows Network and Firewall Baseline", StringComparison.OrdinalIgnoreCase)
            || string.Equals(policy.Name, "Workstation Security Baseline", StringComparison.OrdinalIgnoreCase)
            || string.Equals(policy.Name, "Server Security Baseline", StringComparison.OrdinalIgnoreCase))
        {
            if (settingName.Contains("Firewall", StringComparison.OrdinalIgnoreCase))
            {
                return ("Computer Configuration\\Windows Settings\\Security Settings\\Windows Defender Firewall with Advanced Security", ResolveRegistryPath(policy, settingName, settingCategory));
            }

            if (settingName.Contains("Defender", StringComparison.OrdinalIgnoreCase)
                || string.Equals(settingCategory, "EndpointProtection", StringComparison.OrdinalIgnoreCase)
                || string.Equals(settingCategory, "CredentialProtection", StringComparison.OrdinalIgnoreCase)
                || string.Equals(settingCategory, "ApplicationControl", StringComparison.OrdinalIgnoreCase))
            {
                return ("Computer Configuration\\Administrative Templates\\Windows Components\\Microsoft Defender Antivirus", ResolveRegistryPath(policy, settingName, settingCategory));
            }

            if (string.Equals(settingCategory, "RemoteManagement", StringComparison.OrdinalIgnoreCase)
                || string.Equals(settingCategory, "RemoteAccess", StringComparison.OrdinalIgnoreCase))
            {
                return ("Computer Configuration\\Administrative Templates\\System\\Remote Assistance", ResolveRegistryPath(policy, settingName, settingCategory));
            }

            if (string.Equals(settingCategory, "DeviceControl", StringComparison.OrdinalIgnoreCase))
            {
                return ("Computer Configuration\\Administrative Templates\\System\\Removable Storage Access", ResolveRegistryPath(policy, settingName, settingCategory));
            }

            return ("Computer Configuration\\Windows Settings\\Security Settings\\Local Policies\\Security Options", ResolveRegistryPath(policy, settingName, settingCategory));
        }

        if (string.Equals(policy.Name, "Corporate Desktop Experience", StringComparison.OrdinalIgnoreCase))
        {
            if (settingName.Contains("Wallpaper", StringComparison.OrdinalIgnoreCase)
                || settingName.Contains("LockScreen", StringComparison.OrdinalIgnoreCase))
            {
                return ("User Configuration\\Administrative Templates\\Desktop\\Desktop", ResolveRegistryPath(policy, settingName, settingCategory));
            }

            return ("User Configuration\\Administrative Templates\\Start Menu and Taskbar", ResolveRegistryPath(policy, settingName, settingCategory));
        }

        if (string.Equals(policy.Name, "Enterprise Browser Controls", StringComparison.OrdinalIgnoreCase)
            || settingName.StartsWith("Chrome", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveBrowserPolicyPath(policy, settingName, settingCategory);
        }

        if (string.Equals(policy.Name, "User Logon and Drive Mapping", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(settingCategory, "LogonScripts", StringComparison.OrdinalIgnoreCase))
            {
                return ("User Configuration\\Windows Settings\\Scripts (Logon/Logoff)", null);
            }

            if (string.Equals(settingCategory, "DriveMappings", StringComparison.OrdinalIgnoreCase))
            {
                return ("User Configuration\\Preferences\\Windows Settings\\Drive Maps", null);
            }

            if (string.Equals(settingCategory, "Printers", StringComparison.OrdinalIgnoreCase))
            {
                return ("User Configuration\\Preferences\\Control Panel Settings\\Printers", null);
            }

            if (string.Equals(settingCategory, "Shortcuts", StringComparison.OrdinalIgnoreCase))
            {
                return ("User Configuration\\Preferences\\Windows Settings\\Shortcuts", null);
            }

            return ("User Configuration\\Preferences\\Windows Settings", null);
        }

        if (string.Equals(policy.Name, "Office Productivity Controls", StringComparison.OrdinalIgnoreCase)
            || policy.Name.Contains("Office", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveOfficePolicyPath(policy, settingName, settingCategory);
        }

        if (string.Equals(policy.Name, "Windows Update Enterprise Ring", StringComparison.OrdinalIgnoreCase))
        {
            return ("Computer Configuration\\Administrative Templates\\Windows Components\\Windows Update\\Manage updates offered from Windows Update", ResolveRegistryPath(policy, settingName, settingCategory));
        }

        if (string.Equals(policy.Name, "Remote Access and VPN Controls", StringComparison.OrdinalIgnoreCase))
        {
            return ("Computer Configuration\\Administrative Templates\\Network\\Network Connections", ResolveRegistryPath(policy, settingName, settingCategory));
        }

        if (string.Equals(policy.Name, "Removable Media and Device Control", StringComparison.OrdinalIgnoreCase))
        {
            return ("Computer Configuration\\Administrative Templates\\System\\Removable Storage Access", ResolveRegistryPath(policy, settingName, settingCategory));
        }

        if (policy.Name.StartsWith("Location Desktop Experience - ", StringComparison.OrdinalIgnoreCase))
        {
            return ("User Configuration\\Administrative Templates\\Desktop\\Desktop", ResolveRegistryPath(policy, settingName, settingCategory));
        }

        if (policy.Name.StartsWith("Department Collaboration - ", StringComparison.OrdinalIgnoreCase)
            || policy.Name.StartsWith("Department App Defaults - ", StringComparison.OrdinalIgnoreCase))
        {
            var customRegistryPath = BuildCustomPolicyRegistryPath(company, true, policy.Name, settingCategory, settingName);
            return (customRegistryPath, customRegistryPath);
        }

        if (policy.Name.EndsWith("Controls", StringComparison.OrdinalIgnoreCase))
        {
            return ("Computer Configuration\\Administrative Templates", ResolveRegistryPath(policy, settingName, settingCategory));
        }

        var fallbackRegistry = BuildCustomPolicyRegistryPath(company, IsUserScopedPolicySetting(policy, settingCategory, settingName), policy.Name, settingCategory, settingName);
        return (fallbackRegistry, fallbackRegistry);
    }

    private static (string PolicyPath, string? RegistryPath) ResolveBrowserPolicyPath(
        PolicyRecord policy,
        string settingName,
        string settingCategory)
    {
        if (settingName.StartsWith("Chrome", StringComparison.OrdinalIgnoreCase))
        {
            return ("Computer Configuration\\Administrative Templates\\Google\\Google Chrome", $@"HKLM\Software\Policies\Google\Chrome\{settingName}");
        }

        if (string.Equals(settingCategory, "BrowserConfiguration", StringComparison.OrdinalIgnoreCase))
        {
            return ("Computer Configuration\\Administrative Templates\\Microsoft Edge\\Startup, home page and new tab page", $@"HKLM\Software\Policies\Microsoft\Edge\{settingName}");
        }

        return ("Computer Configuration\\Administrative Templates\\Microsoft Edge", $@"HKLM\Software\Policies\Microsoft\Edge\{settingName}");
    }

    private static (string PolicyPath, string? RegistryPath) ResolveOfficePolicyPath(
        PolicyRecord policy,
        string settingName,
        string settingCategory)
    {
        if (settingName.Contains("Macro", StringComparison.OrdinalIgnoreCase)
            || string.Equals(settingCategory, "OfficeSecurity", StringComparison.OrdinalIgnoreCase))
        {
            return ("User Configuration\\Administrative Templates\\Microsoft Office 2016\\Security Settings\\Trust Center", $@"HKCU\Software\Policies\Microsoft\Office\16.0\Common\Security\{settingName}");
        }

        if (string.Equals(settingCategory, "OfficePrivacy", StringComparison.OrdinalIgnoreCase))
        {
            return ("User Configuration\\Administrative Templates\\Microsoft Office 2016\\Privacy", $@"HKCU\Software\Policies\Microsoft\Office\16.0\Common\Privacy\{settingName}");
        }

        if (settingName.Contains("Update", StringComparison.OrdinalIgnoreCase))
        {
            return ("User Configuration\\Administrative Templates\\Microsoft Office 2016\\Updates", $@"HKCU\Software\Policies\Microsoft\Office\16.0\Common\OfficeUpdate\{settingName}");
        }

        return ("User Configuration\\Administrative Templates\\Microsoft Office 2016\\Common", $@"HKCU\Software\Policies\Microsoft\Office\16.0\Common\{settingName}");
    }

    private static (string PolicyPath, string? RegistryPath) ResolveIntunePolicySettingMetadata(
        PolicyRecord policy,
        string settingName,
        string settingCategory)
    {
        if (policy.Name.Contains("BitLocker", StringComparison.OrdinalIgnoreCase)
            || string.Equals(settingCategory, "DiskEncryption", StringComparison.OrdinalIgnoreCase))
        {
            return ($"Devices\\Windows\\Configuration profiles\\{policy.Name}\\Endpoint security\\Disk encryption", $"./Device/Vendor/MSFT/BitLocker/{settingName}");
        }

        if (policy.Name.Contains("Windows Security Baseline", StringComparison.OrdinalIgnoreCase)
            || string.Equals(settingCategory, "EndpointProtection", StringComparison.OrdinalIgnoreCase)
            || string.Equals(settingCategory, "DeviceHealth", StringComparison.OrdinalIgnoreCase)
            || string.Equals(settingCategory, "Compliance", StringComparison.OrdinalIgnoreCase))
        {
            return ($"Devices\\Windows\\Configuration profiles\\{policy.Name}\\Settings catalog\\Windows Security", $"./Device/Vendor/MSFT/Policy/Config/{settingCategory}/{settingName}");
        }

        if (string.Equals(settingCategory, "AppAssignment", StringComparison.OrdinalIgnoreCase)
            || string.Equals(settingCategory, "ApplicationConfiguration", StringComparison.OrdinalIgnoreCase))
        {
            return ($"Devices\\Windows\\Configuration profiles\\{policy.Name}\\Apps", $"./Device/Vendor/MSFT/EnterpriseAppManagement/{settingName}");
        }

        if (string.Equals(settingCategory, "RemoteSupport", StringComparison.OrdinalIgnoreCase))
        {
            return ($"Devices\\Windows\\Configuration profiles\\{policy.Name}\\Remote help", $"./Device/Vendor/MSFT/RemoteHelp/{settingName}");
        }

        if (string.Equals(settingCategory, "SharedDevice", StringComparison.OrdinalIgnoreCase))
        {
            return ($"Devices\\Windows\\Configuration profiles\\{policy.Name}\\Shared multi-user device", $"./Device/Vendor/MSFT/SharedPC/{settingName}");
        }

        return ($"Devices\\Windows\\Configuration profiles\\{policy.Name}\\Settings catalog", $"./Device/Vendor/MSFT/Policy/Config/{settingCategory}/{settingName}");
    }

    private static string? ResolveRegistryPath(
        PolicyRecord policy,
        string settingName,
        string settingCategory)
    {
        if (string.Equals(policy.Name, "Corporate Desktop Experience", StringComparison.OrdinalIgnoreCase))
        {
            return settingName.Contains("Wallpaper", StringComparison.OrdinalIgnoreCase)
                || settingName.Contains("LockScreen", StringComparison.OrdinalIgnoreCase)
                ? $@"HKCU\Software\Policies\Microsoft\Windows\Personalization\{settingName}"
                : $@"HKCU\Software\Policies\Microsoft\Windows\Explorer\{settingName}";
        }

        if (string.Equals(policy.Name, "Enterprise Browser Controls", StringComparison.OrdinalIgnoreCase))
        {
            return settingName.StartsWith("Chrome", StringComparison.OrdinalIgnoreCase)
                ? $@"HKLM\Software\Policies\Google\Chrome\{settingName}"
                : $@"HKLM\Software\Policies\Microsoft\Edge\{settingName}";
        }

        if (policy.Name.Contains("Office", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(settingCategory, "OfficePrivacy", StringComparison.OrdinalIgnoreCase)
                ? $@"HKCU\Software\Policies\Microsoft\Office\16.0\Common\Privacy\{settingName}"
                : $@"HKCU\Software\Policies\Microsoft\Office\16.0\Common\{settingName}";
        }

        if (string.Equals(policy.Name, "Windows Update Enterprise Ring", StringComparison.OrdinalIgnoreCase))
        {
            return $@"HKLM\Software\Policies\Microsoft\Windows\WindowsUpdate\{settingName}";
        }

        if (string.Equals(policy.Name, "Windows Defender and ASR Baseline", StringComparison.OrdinalIgnoreCase)
            || string.Equals(settingCategory, "EndpointProtection", StringComparison.OrdinalIgnoreCase)
            || string.Equals(settingCategory, "ApplicationControl", StringComparison.OrdinalIgnoreCase))
        {
            return $@"HKLM\Software\Policies\Microsoft\Windows Defender\{settingName}";
        }

        if (string.Equals(policy.Name, "Removable Media and Device Control", StringComparison.OrdinalIgnoreCase)
            || string.Equals(settingCategory, "DeviceControl", StringComparison.OrdinalIgnoreCase))
        {
            return $@"HKLM\Software\Policies\Microsoft\Windows\RemovableStorageDevices\{settingName}";
        }

        if (string.Equals(policy.Name, "Remote Access and VPN Controls", StringComparison.OrdinalIgnoreCase)
            || string.Equals(settingCategory, "RemoteAccess", StringComparison.OrdinalIgnoreCase)
            || string.Equals(settingCategory, "RemoteManagement", StringComparison.OrdinalIgnoreCase))
        {
            return $@"HKLM\Software\Policies\Microsoft\Windows\NetworkConnections\{settingName}";
        }

        return null;
    }

    private static bool IsUserScopedPolicySetting(PolicyRecord policy, string settingCategory, string settingName)
    {
        return settingCategory is "OfficePrivacy" or "OfficeSecurity" or "OfficeConfiguration" or "BrowserConfiguration" or "DriveMappings" or "LogonScripts" or "Printers" or "Shortcuts" or "Messaging" or "Collaboration"
               || string.Equals(policy.Name, "Corporate Desktop Experience", StringComparison.OrdinalIgnoreCase)
               || string.Equals(policy.Name, "User Logon and Drive Mapping", StringComparison.OrdinalIgnoreCase)
               || policy.Name.StartsWith("Department Collaboration - ", StringComparison.OrdinalIgnoreCase)
               || settingName.Contains("Wallpaper", StringComparison.OrdinalIgnoreCase)
               || settingName.Contains("HomeSite", StringComparison.OrdinalIgnoreCase)
               || settingName.Contains("Logon", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildCustomPolicyRegistryPath(
        Company? company,
        bool userScoped,
        string policyName,
        string settingCategory,
        string settingName)
    {
        var rootHive = userScoped ? "HKCU" : "HKLM";
        var vendorNode = string.IsNullOrWhiteSpace(company?.PrimaryDomain)
            ? "SyntheticEnterprise"
            : company.PrimaryDomain.Replace(".", "_", StringComparison.OrdinalIgnoreCase);

        return $@"{rootHive}\Software\Policies\{vendorNode}\{Slug(policyName)}\{Slug(settingCategory)}\{Slug(settingName)}";
    }

    private static string CreateStableGuid(params string[] components)
    {
        var seed = string.Join("|", components.Where(component => !string.IsNullOrWhiteSpace(component)));
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(seed));
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes[..16]).ToString();
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
            string.Equals(group.Name, DepartmentUserGroupName(department), StringComparison.OrdinalIgnoreCase));
    }

    private void AddMembershipIfPresent(List<DirectoryGroupMembership> memberships, DirectoryGroup? group, string? memberObjectId, string memberObjectType)
    {
        if (group is null || string.IsNullOrWhiteSpace(memberObjectId))
        {
            return;
        }

        memberships.Add(CreateMembership(group.Id, memberObjectId, memberObjectType));
    }

    private void AddDepartmentResourceMemberships(
        List<DirectoryGroupMembership> memberships,
        Department department,
        DirectoryGroup? departmentGroup,
        IReadOnlyList<DirectoryGroup> groups,
        string companyId)
    {
        if (departmentGroup is null)
        {
            return;
        }

        var normalized = department.Name.ToLowerInvariant();
        if (normalized.Contains("finance") || normalized.Contains("account") || normalized.Contains("payroll") || normalized.Contains("procurement"))
        {
            AddMembershipIfPresent(memberships, FindGroup(groups, companyId, ErpUsersGroupName()), departmentGroup.Id, "Group");
        }

        if (normalized.Contains("human") || normalized.Contains("people") || normalized == "hr")
        {
            AddMembershipIfPresent(memberships, FindGroup(groups, companyId, HrisUsersGroupName()), departmentGroup.Id, "Group");
        }

        if (normalized.Contains("sales") || normalized.Contains("marketing") || normalized.Contains("commercial") || normalized.Contains("customer"))
        {
            AddMembershipIfPresent(memberships, FindGroup(groups, companyId, CrmUsersGroupName()), departmentGroup.Id, "Group");
        }

        if (normalized.Contains("information technology") || normalized == "it" || normalized.Contains("engineering") || normalized.Contains("security") || normalized.Contains("operations") || normalized.Contains("platform") || normalized.Contains("infrastructure"))
        {
            AddMembershipIfPresent(memberships, FindGroup(groups, companyId, ItsmAgentsGroupName()), departmentGroup.Id, "Group");
        }
    }

    private static IEnumerable<DirectoryGroup> ResolveSharedMailboxDepartmentGroups(
        DirectoryAccount sharedAccount,
        IReadOnlyList<DirectoryGroup> groups,
        IReadOnlyList<Department> departments,
        string companyId)
    {
        var token = (sharedAccount.SamAccountName ?? string.Empty).ToLowerInvariant();
        foreach (var department in departments)
        {
            var departmentName = department.Name.ToLowerInvariant();
            var match =
                (token.Contains("helpdesk") || token.Contains("itops")) && (departmentName.Contains("information technology") || departmentName.Contains("operations")) ||
                token.Contains("payroll") && (departmentName.Contains("finance") || departmentName.Contains("account")) ||
                token.Contains("accountspayable") && departmentName.Contains("finance") ||
                token.Contains("salesops") && (departmentName.Contains("sales") || departmentName.Contains("marketing")) ||
                token.Contains("recruiting") && (departmentName.Contains("human") || departmentName.Contains("people") || departmentName == "hr") ||
                token.Contains("facilities") && (departmentName.Contains("operations") || departmentName.Contains("facilities"));

            if (match)
            {
                var group = FindGroup(groups, companyId, DepartmentUserGroupName(department));
                if (group is not null)
                {
                    yield return group;
                }
            }
        }
    }

    private static string DepartmentUserGroupName(Department department)
        => $"GG {department.Name} Users";

    private static string DepartmentDistributionGroupName(Department department)
        => $"DL {department.Name}";

    private static string DepartmentLeadershipGroupName(Department department)
        => $"DL {department.Name} Leadership";

    private static string DepartmentFileAccessGroupName(Department department)
        => $"ACL FS {department.Name} Modify";

    private static string DepartmentMailboxAccessGroupName(Department department)
        => $"ACL MBX {department.Name} Shared";

    private static string TeamUserGroupName(Department? department, Team team)
        => department is null ? $"GG {team.Name}" : $"GG {department.Name} {team.Name}";

    private static string TeamDistributionGroupName(Department? department, Team team)
        => department is null ? $"DL {team.Name}" : $"DL {department.Name} {team.Name}";

    private static string SharedMailboxAccessGroupName(DirectoryAccount sharedAccount)
        => $"ACL MBX {ResolveMailboxToken(sharedAccount)} Access";

    private static string AllEmployeesSecurityGroupName()
        => "GG All Employees";

    private static string AllEmployeesDistributionGroupName()
        => "DL All Employees";

    private static string AllManagersDistributionGroupName()
        => "DL People Leaders";

    private static string ExternalContractorsGroupName()
        => "GG External Contractors";

    private static string MspOperatorsGroupName()
        => "GG MSP Operations";

    private static string B2BGuestsGroupName()
        => "GG B2B Guests";

    private static string VpnUsersGroupName()
        => "GG VPN Users";

    private static string CorpWifiUsersGroupName()
        => "GG Corp WiFi Users";

    private static string OfficeUsersGroupName()
        => "GG Microsoft 365 Users";

    private static string OfficeAdminsGroupName()
        => "GG Microsoft 365 Admins";

    private static string ErpUsersGroupName()
        => "GG ERP Users";

    private static string ErpAdminsGroupName()
        => "GG ERP Admins";

    private static string CrmUsersGroupName()
        => "GG CRM Users";

    private static string CrmAdminsGroupName()
        => "GG CRM Admins";

    private static string HrisUsersGroupName()
        => "GG HRIS Users";

    private static string HrisAdminsGroupName()
        => "GG HRIS Admins";

    private static string ItsmAgentsGroupName()
        => "GG ITSM Agents";

    private static string ItsmAdminsGroupName()
        => "GG ITSM Admins";

    private static string BrowserPilotUsersGroupName()
        => "GG Browser Pilot Users";

    private static string RemoteSupportOperatorsGroupName()
        => "GG Remote Support Operators";

    private static string ServerRemoteDesktopUsersGroupName()
        => "GG Server Remote Desktop Users";

    private static string ServerRemoteDesktopAdminsGroupName()
        => "GG Server Remote Desktop Admins";

    private static string SqlAdminsGroupName()
        => "GG SQL Administrators";

    private static string BackupOperatorsGroupName()
        => "GG Backup Operators";

    private static string GroupPolicyEditorsGroupName()
        => "GG Group Policy Editors";

    private static string LapsReadersGroupName()
        => "GG LAPS Readers";

    private static string PasswordResetOperatorsGroupName()
        => "GG Password Reset Operators";

    private static string WorkstationJoinOperatorsGroupName()
        => "GG Workstation Join Operators";

    private static string PrintOperatorsGroupName()
        => "GG Print Operators";

    private static string PrivilegedAccessGroupName()
        => "UG Privileged Access";

    private static string Tier0IdentityAdminsGroupName()
        => "GG Tier0 Identity Admins";

    private static string Tier0PawUsersGroupName()
        => "GG Tier0 PAW Users";

    private static string Tier0PawDevicesGroupName()
        => "GG Tier0 PAW Devices";

    private static string Tier1ServerAdminsGroupName()
        => "GG Tier1 Server Admins";

    private static string Tier1WorkstationAdminsGroupName()
        => "GG Tier1 Workstation Admins";

    private static string Tier1PawUsersGroupName()
        => "GG Tier1 PAW Users";

    private static string Tier1PawDevicesGroupName()
        => "GG Tier1 PAW Devices";

    private static string Tier1ManagedWorkstationsGroupName()
        => "GG Tier1 Managed Workstations";

    private static string Tier1ManagedServersGroupName()
        => "GG Tier1 Managed Servers";

    private static string Tier2HelpdeskGroupName()
        => "GG Tier2 Helpdesk";

    private static string Tier2ApplicationSupportGroupName()
        => "GG Tier2 Application Support";

    private static string ResolveMailboxToken(DirectoryAccount sharedAccount)
        => sharedAccount.DisplayName
           ?? sharedAccount.UserPrincipalName?.Split('@')[0].Replace(".", " ", StringComparison.OrdinalIgnoreCase)
               .Replace("-", " ", StringComparison.OrdinalIgnoreCase)
           ?? sharedAccount.SamAccountName
           ?? "Shared Mailbox";

    private ServiceAccountBlueprint BuildServiceAccountBlueprint(Company company, int index)
    {
        var workloadPatterns = new (string Prefix, string Role, bool Privileged, string PasswordProfile)[]
        {
            ("sql", "database", true, "ServiceManaged"),
            ("web", "frontend", false, "ServiceManaged"),
            ("app", "middleware", true, "ServiceManaged"),
            ("erp", "integration", false, "ServiceManaged"),
            ("crm", "sync", false, "ServiceManaged"),
            ("hris", "feed", false, "ServiceManaged"),
            ("backup", "vault", true, "ServiceManaged"),
            ("monitor", "telemetry", false, "ServiceManaged"),
            ("print", "spool", false, "ServiceManaged"),
            ("deploy", "agent", true, "ServiceManaged"),
            ("filesync", "transfer", false, "ServiceManaged"),
            ("sso", "proxy", true, "ServiceManaged")
        };

        var pattern = workloadPatterns[index % workloadPatterns.Length];
        var sequence = (index / workloadPatterns.Length) + 1;
        var commonName = $"svc-{pattern.Prefix}-{pattern.Role}-{sequence:00}";
        var upnLocalPart = $"svc.{pattern.Prefix}.{pattern.Role}.{sequence:00}";
        var roleToken = pattern.Role.Length <= 3 ? pattern.Role : pattern.Role[..3];
        var sam = Truncate($"svc_{pattern.Prefix}_{roleToken}{sequence:00}", 20);

        return new ServiceAccountBlueprint(
            commonName,
            sam,
            upnLocalPart,
            pattern.Privileged,
            pattern.Privileged ? "Tier1" : null,
            pattern.PasswordProfile);
    }

    private sealed record ServiceAccountBlueprint(
        string CommonName,
        string SamAccountName,
        string UserPrincipalNameLocalPart,
        bool Privileged,
        string? AdministrativeTier,
        string PasswordProfile);

    private static bool IsLeadershipTitle(string title)
        => !string.IsNullOrWhiteSpace(title)
           && (title.Contains("Chief", StringComparison.OrdinalIgnoreCase)
               || title.Contains("Vice President", StringComparison.OrdinalIgnoreCase)
               || title.Contains("Director", StringComparison.OrdinalIgnoreCase)
               || title.Contains("Manager", StringComparison.OrdinalIgnoreCase)
               || title.Contains("Head", StringComparison.OrdinalIgnoreCase));

    private static bool LooksLikePilotUser(Person person)
        => IsLeadershipTitle(person.Title ?? string.Empty)
           || (person.Title?.Contains("Engineer", StringComparison.OrdinalIgnoreCase) ?? false)
           || (person.Title?.Contains("Analyst", StringComparison.OrdinalIgnoreCase) ?? false);

    private static bool LooksLikeSupportUser(Person person)
        => (person.Title?.Contains("Support", StringComparison.OrdinalIgnoreCase) ?? false)
           || (person.Title?.Contains("Helpdesk", StringComparison.OrdinalIgnoreCase) ?? false)
           || (person.Title?.Contains("Desktop", StringComparison.OrdinalIgnoreCase) ?? false)
           || (person.Title?.Contains("Service", StringComparison.OrdinalIgnoreCase) ?? false);

    private static bool LooksLikeServerAdmin(string title)
        => title.Contains("Engineer", StringComparison.OrdinalIgnoreCase)
           || title.Contains("Administrator", StringComparison.OrdinalIgnoreCase)
           || title.Contains("Platform", StringComparison.OrdinalIgnoreCase)
           || title.Contains("Infrastructure", StringComparison.OrdinalIgnoreCase);

    private static bool MatchesDepartment(Person person, IReadOnlyList<Department> departments, params string[] keywords)
    {
        if (string.IsNullOrWhiteSpace(person.DepartmentId))
        {
            return false;
        }

        var department = departments.FirstOrDefault(candidate => candidate.Id == person.DepartmentId);
        if (department is null)
        {
            return false;
        }

        return keywords.Any(keyword => department.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase));
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
        var employeeDigits = new string(safeEmployeeId.Where(char.IsDigit).ToArray());
        var suffixSource = string.IsNullOrWhiteSpace(employeeDigits) ? safeEmployeeId : employeeDigits;
        var suffix = suffixSource.Length >= 5 ? suffixSource[^5..] : suffixSource.PadLeft(5, '0');
        var baseValue = $"{Slug(firstName).FirstOrDefault()}{Slug(lastName)}";
        var nameBudget = Math.Max(1, 20 - suffix.Length);
        if (baseValue.Length > nameBudget)
        {
            baseValue = baseValue[..nameBudget];
        }

        return Truncate($"{baseValue}{suffix}", 20);
    }

    private static string BuildSecondaryAccountDisplayName(Person person, string subtype)
    {
        if (string.IsNullOrWhiteSpace(subtype))
        {
            return person.DisplayName;
        }

        return $"{person.DisplayName} {subtype}".Trim();
    }

    private static string? BuildSecondaryEmployeeId(string? employeeId, string prefix)
    {
        if (string.IsNullOrWhiteSpace(employeeId))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(prefix) ? employeeId : $"{prefix}{employeeId}";
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

        var employeeToken = string.IsNullOrWhiteSpace(person.EmployeeId)
            ? Slug(person.FirstName)
            : Slug(person.EmployeeId);
        var uniqueToken = employeeToken.Length >= 6 ? employeeToken[^6..] : employeeToken;
        var lastNameToken = Slug(person.LastName);
        var reservedLength = suffix.Length + 1 + uniqueToken.Length + 1;
        var lastNameBudget = Math.Max(1, 20 - reservedLength);
        if (lastNameToken.Length > lastNameBudget)
        {
            lastNameToken = lastNameToken[..lastNameBudget];
        }

        return Truncate($"{suffix}_{lastNameToken}_{uniqueToken}", 20);
    }

    private static string BuildGuestUpnLocalPart(string firstName, string lastName, string homeTenantDomain)
    {
        var externalToken = homeTenantDomain.Replace(".", "_", StringComparison.OrdinalIgnoreCase);
        return $"{Slug(firstName)}_{Slug(lastName)}_{externalToken}#EXT#";
    }

    private static string BuildUniqueDirectoryAccountUpn(
        string localPart,
        string domain,
        ISet<string> issuedUpns)
    {
        var candidate = $"{localPart}@{domain}";
        if (issuedUpns.Add(candidate))
        {
            return candidate;
        }

        for (var suffix = 2; suffix < 10000; suffix++)
        {
            candidate = $"{localPart}{suffix}@{domain}";
            if (issuedUpns.Add(candidate))
            {
                return candidate;
            }
        }

        return $"{Guid.NewGuid():N}@{domain}";
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
