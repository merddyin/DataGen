using Microsoft.Extensions.DependencyInjection;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Core.Abstractions;
using SyntheticEnterprise.Core.DependencyInjection;

namespace SyntheticEnterprise.Core.Tests;

public sealed class IdentityInfrastructureGenerationTests
{
    [Fact]
    public void WorldGenerator_Populates_Cryptographic_Passwords_And_Nested_Groups()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var result = generator.Generate(
            new GenerationContext
            {
                Scenario = new ScenarioDefinition
                {
                    Name = "Identity Realism Test",
                    Companies = new()
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Identity Realism Co",
                            Industry = "Manufacturing",
                            EmployeeCount = 180,
                            BusinessUnitCount = 2,
                            DepartmentCountPerBusinessUnit = 3,
                            TeamCountPerDepartment = 2,
                            OfficeCount = 2,
                            SharedMailboxCount = 4,
                            ServiceAccountCount = 6,
                            IncludePrivilegedAccounts = true,
                            Countries = new() { "United States" }
                        }
                    }
                }
            },
            new CatalogSet());

        Assert.NotEmpty(result.World.Accounts);
        Assert.All(result.World.Accounts, account =>
        {
            Assert.False(string.IsNullOrWhiteSpace(account.GeneratedPassword));
            Assert.True(account.GeneratedPassword!.Length >= 12);
            Assert.Contains(account.GeneratedPassword, character => char.IsLower(character));
            Assert.Contains(account.GeneratedPassword, character => char.IsUpper(character));
            Assert.Contains(account.GeneratedPassword, character => char.IsDigit(character));
            Assert.Contains(account.GeneratedPassword, character => !char.IsLetterOrDigit(character));
        });

        var distinctPasswords = result.World.Accounts
            .Select(account => account.GeneratedPassword)
            .Where(password => !string.IsNullOrWhiteSpace(password))
            .Distinct(StringComparer.Ordinal)
            .Count();
        var distinctAccountIds = result.World.Accounts
            .Select(account => account.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var distinctPersonUpns = result.World.People
            .Select(person => person.UserPrincipalName)
            .Where(upn => !string.IsNullOrWhiteSpace(upn))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var distinctAccountUpns = result.World.Accounts
            .Select(account => account.UserPrincipalName)
            .Where(upn => !string.IsNullOrWhiteSpace(upn))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        Assert.Equal(result.World.Accounts.Count, distinctPasswords);
        Assert.Equal(result.World.Accounts.Count, distinctAccountIds);
        Assert.Equal(result.World.People.Count, distinctPersonUpns);
        Assert.Equal(result.World.Accounts.Count, distinctAccountUpns);
        Assert.All(result.World.Accounts, account =>
        {
            Assert.NotNull(account.WhenCreated);
            Assert.NotNull(account.WhenModified);
            var whenCreated = account.WhenCreated!.Value;
            var whenModified = account.WhenModified!.Value;
            Assert.True(whenModified >= whenCreated);
            Assert.True(
                !string.Equals(account.UserType, "Guest", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(account.InvitationStatus, "PendingAcceptance", StringComparison.OrdinalIgnoreCase)
                || account.LastLogon is null
                || account.LastLogon >= whenCreated);
            if (!string.Equals(account.UserType, "Guest", StringComparison.OrdinalIgnoreCase)
                || string.Equals(account.InvitationStatus, "Accepted", StringComparison.OrdinalIgnoreCase))
            {
                Assert.NotNull(account.LastLogon);
                Assert.True(account.LastLogon >= whenCreated);
            }

            if (account.PasswordLastSet is not null)
            {
                Assert.True(account.PasswordLastSet >= whenCreated);
                Assert.True(whenModified >= account.PasswordLastSet);
            }
        });
        Assert.Contains(result.World.GroupMemberships, membership => membership.MemberObjectType == "Group");
        Assert.Contains(result.World.OrganizationalUnits, ou => ou.Name == "Computers");
        Assert.Contains(result.World.OrganizationalUnits, ou => ou.Name == "Workstations");
        Assert.Contains(result.World.OrganizationalUnits, ou => ou.Name == "Servers");
        Assert.Contains(result.World.OrganizationalUnits, ou => ou.Name == "Admin Accounts");
        Assert.Contains(result.World.OrganizationalUnits, ou => ou.Name == "Tier 0");
        Assert.Contains(result.World.OrganizationalUnits, ou => ou.Name == "Tier 1");
        Assert.Contains(result.World.OrganizationalUnits, ou => ou.Name == "Tier 2");
        Assert.Contains(result.World.OrganizationalUnits, ou => ou.Name == "Privileged Access Workstations");
        Assert.Contains(result.World.OrganizationalUnits, ou => ou.Name == "Corporate Standard");
        Assert.Contains(result.World.OrganizationalUnits, ou => ou.Name == "Remote Workforce");
        Assert.Contains(result.World.OrganizationalUnits, ou => ou.Name == "Shared Kiosks");
        Assert.Contains(result.World.OrganizationalUnits, ou => ou.Name == "IT Administration");
        Assert.Contains(result.World.OrganizationalUnits, ou => ou.Name == "Identity");
        Assert.Contains(result.World.OrganizationalUnits, ou => ou.Name == "Database");
        Assert.Contains(result.World.OrganizationalUnits, ou => ou.Name == "Web");
        Assert.Contains(result.World.OrganizationalUnits, ou => ou.Name == "Application");
        Assert.Contains(result.World.Groups, group => group.Name == "UG Privileged Access");
        Assert.Contains(result.World.Groups, group => group.Name == "GG Tier0 PAW Users");
        Assert.Contains(result.World.Groups, group => group.Name == "GG Tier1 PAW Users");
        Assert.Contains(result.World.Groups, group => group.Name == "GG Tier0 PAW Devices");
        Assert.Contains(result.World.Groups, group => group.Name == "GG Tier1 PAW Devices");
        Assert.Contains(result.World.Groups, group => group.Name == "GG Tier1 Managed Workstations");
        Assert.Contains(result.World.Groups, group => group.Name == "GG Tier1 Managed Servers");
        Assert.Contains(result.World.Groups, group => !string.IsNullOrWhiteSpace(group.AdministrativeTier));
        Assert.Contains(result.World.Accounts, account => account.AccountType == "Privileged" && !string.IsNullOrWhiteSpace(account.AdministrativeTier));
        Assert.Contains(result.World.GroupMemberships, membership =>
            membership.MemberObjectType == "Account"
            && result.World.Groups.Any(group =>
                group.Id == membership.GroupId
                && (group.Name == "GG Tier0 PAW Users" || group.Name == "GG Tier1 PAW Users")));
        Assert.Contains(result.World.Accounts, account =>
            account.AccountType == "Device"
            && string.Equals(account.IdentityProvider, "HybridDirectory", StringComparison.OrdinalIgnoreCase)
            && account.SamAccountName.EndsWith("$", StringComparison.Ordinal));
        Assert.Contains(result.World.Accounts, account =>
            account.AccountType == "Device"
            && string.Equals(account.IdentityProvider, "EntraID", StringComparison.OrdinalIgnoreCase)
            && !account.SamAccountName.EndsWith("$", StringComparison.Ordinal));
        var peopleById = result.World.People.ToDictionary(person => person.Id, StringComparer.OrdinalIgnoreCase);
        Assert.All(
            result.World.Accounts.Where(account => string.Equals(account.AccountType, "User", StringComparison.OrdinalIgnoreCase)),
            account =>
            {
                Assert.False(string.IsNullOrWhiteSpace(account.PersonId));
                Assert.False(string.IsNullOrWhiteSpace(account.DisplayName));
                Assert.False(string.IsNullOrWhiteSpace(account.EmployeeId));
                var person = peopleById[account.PersonId!];
                Assert.Equal(person.DisplayName, account.DisplayName);
                Assert.Equal(person.EmployeeId, account.EmployeeId);
            });
        Assert.All(
            result.World.Accounts.Where(account => string.Equals(account.AccountType, "Privileged", StringComparison.OrdinalIgnoreCase)),
            account =>
            {
                Assert.False(string.IsNullOrWhiteSpace(account.PersonId));
                Assert.False(string.IsNullOrWhiteSpace(account.DisplayName));
                Assert.False(string.IsNullOrWhiteSpace(account.EmployeeId));
                var person = peopleById[account.PersonId!];
                Assert.NotEqual(person.DisplayName, account.DisplayName);
                Assert.StartsWith("A", account.EmployeeId!, StringComparison.OrdinalIgnoreCase);
            });
        Assert.All(
            result.World.Accounts.Where(account => account.AccountType == "Device"),
            account =>
            {
                Assert.NotNull(account.LastLogon);
                Assert.NotNull(account.WhenCreated);
                Assert.NotNull(account.WhenModified);
                Assert.True(account.LastLogon >= account.WhenCreated!.Value);
                Assert.True(account.WhenModified!.Value >= account.LastLogon);
                Assert.False(string.IsNullOrWhiteSpace(account.DisplayName));
                Assert.False(string.IsNullOrWhiteSpace(account.Domain));
                Assert.Equal(account.UserPrincipalName.Split('@')[1], account.Domain, ignoreCase: true);
                if (string.Equals(account.IdentityProvider, "HybridDirectory", StringComparison.OrdinalIgnoreCase))
                {
                    Assert.EndsWith("$", account.SamAccountName, StringComparison.Ordinal);
                    Assert.StartsWith($"CN={account.DisplayName},", account.DistinguishedName, StringComparison.OrdinalIgnoreCase);
                }
                else if (string.Equals(account.IdentityProvider, "EntraID", StringComparison.OrdinalIgnoreCase))
                {
                    Assert.Equal(account.DistinguishedName, account.DisplayName, ignoreCase: true);
                }
            });
        Assert.DoesNotContain(result.World.GroupMemberships, membership => membership.MemberObjectType == "Device");
        Assert.DoesNotContain(result.World.GroupMemberships, membership => membership.MemberObjectType == "Server");
        var accountlessEndpointIds = result.World.Devices
            .Where(device =>
                string.IsNullOrWhiteSpace(device.DirectoryAccountId)
                && string.IsNullOrWhiteSpace(device.OnPremDirectoryAccountId)
                && string.IsNullOrWhiteSpace(device.CloudDirectoryAccountId))
            .Select(device => device.Id)
            .Concat(result.World.Servers
                .Where(server =>
                    string.IsNullOrWhiteSpace(server.DirectoryAccountId)
                    && string.IsNullOrWhiteSpace(server.OnPremDirectoryAccountId)
                    && string.IsNullOrWhiteSpace(server.CloudDirectoryAccountId))
                .Select(server => server.Id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(result.World.GroupMemberships, membership => accountlessEndpointIds.Contains(membership.MemberObjectId));
        Assert.Contains(result.World.EndpointAdministrativeAssignments, assignment =>
            assignment.EndpointType == "Device" && assignment.AccessRole == "LocalAdministrator");
        Assert.Contains(result.World.EndpointAdministrativeAssignments, assignment =>
            assignment.EndpointType == "Server" && assignment.AccessRole == "LocalAdministrator");
        Assert.Contains(result.World.EndpointPolicyBaselines, baseline =>
            baseline.EndpointType == "Device" && baseline.PolicyCategory == "CredentialManagement");
        Assert.Contains(result.World.EndpointPolicyBaselines, baseline =>
            baseline.EndpointType == "Server" && baseline.PolicyCategory == "SecurityBaseline");
        Assert.Contains(result.World.EndpointLocalGroupMembers, member =>
            member.EndpointType == "Device" && member.LocalGroupName == "Administrators");
        Assert.Contains(result.World.EndpointLocalGroupMembers, member =>
            member.EndpointType == "Server" && member.LocalGroupName == "Remote Desktop Users");
        var falconPackage = Assert.Single(result.World.SoftwarePackages.Where(package =>
            string.Equals(package.Name, "CrowdStrike Falcon", StringComparison.OrdinalIgnoreCase)));
        var deviceFalconInstallations = result.World.DeviceSoftwareInstallations
            .Where(installation => string.Equals(installation.SoftwareId, falconPackage.Id, StringComparison.OrdinalIgnoreCase))
            .Select(installation => installation.DeviceId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var serverFalconInstallations = result.World.ServerSoftwareInstallations
            .Where(installation => string.Equals(installation.SoftwareId, falconPackage.Id, StringComparison.OrdinalIgnoreCase))
            .Select(installation => installation.ServerId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        Assert.Equal(result.World.Devices.Count, deviceFalconInstallations);
        Assert.Equal(result.World.Servers.Count, serverFalconInstallations);
        Assert.Contains(result.World.OrganizationalUnits, ou => ou.Name == "External Users");
        Assert.Contains(result.World.OrganizationalUnits, ou => ou.Name == "Contractors");
        Assert.Contains(result.World.OrganizationalUnits, ou => ou.Name == "Managed Services");
        Assert.Contains(result.World.OrganizationalUnits, ou => ou.Name == "Guests");
        Assert.Contains(result.World.People, person => person.EmploymentType == "Contractor");
        Assert.Contains(result.World.People, person => person.EmploymentType == "ManagedServiceProvider");
        Assert.Contains(result.World.People, person => person.EmploymentType == "Guest");
        var teamsById = result.World.Teams.ToDictionary(team => team.Id, StringComparer.OrdinalIgnoreCase);
        var ceo = Assert.Single(result.World.People.Where(person => person.Title.Contains("Chief Executive Officer", StringComparison.OrdinalIgnoreCase)));
        Assert.All(
            result.World.People.Where(person => !string.Equals(person.Id, ceo.Id, StringComparison.OrdinalIgnoreCase)),
            person =>
            {
                Assert.False(string.IsNullOrWhiteSpace(person.ManagerPersonId));
                Assert.True(peopleById.ContainsKey(person.ManagerPersonId!));
                Assert.True(teamsById.TryGetValue(person.TeamId, out var team));
                Assert.Equal(team!.DepartmentId, person.DepartmentId);

                var manager = peopleById[person.ManagerPersonId!];
                Assert.True(
                    string.Equals(manager.DepartmentId, person.DepartmentId, StringComparison.OrdinalIgnoreCase)
                    || manager.Title.Contains("Vice President", StringComparison.OrdinalIgnoreCase)
                    || manager.Title.Contains("Chief Executive Officer", StringComparison.OrdinalIgnoreCase));
            });
        Assert.Contains(result.World.Accounts, account => account.AccountType == "Contractor" && account.UserType == "Member");
        Assert.Contains(result.World.Accounts, account => account.AccountType == "ManagedServiceProvider" && account.UserType == "Guest");
        Assert.Contains(result.World.Accounts, account => account.AccountType == "Guest" && account.IdentityProvider == "EntraB2B");
        Assert.Contains(result.World.Groups, group => group.Name == "GG External Contractors");
        Assert.Contains(result.World.Groups, group => group.Name == "GG MSP Operations");
        Assert.Contains(result.World.Groups, group => group.Name == "GG B2B Guests");
        Assert.Contains(result.World.Groups, group => group.Name == "DL All Employees");
        Assert.Contains(result.World.Groups, group => group.Name == "DL People Leaders");
        Assert.Contains(result.World.Groups, group => group.Name == "GG ERP Users");
        Assert.Contains(result.World.Groups, group => group.Name == "GG CRM Users");
        Assert.Contains(result.World.Groups, group => group.Name == "GG HRIS Users");
        Assert.Contains(result.World.Groups, group => group.Name == "GG ITSM Agents");
        Assert.Contains(result.World.Groups, group => group.Name == "GG Group Policy Editors");
        Assert.Contains(result.World.Groups, group => group.Name == "GG LAPS Readers");
        Assert.Contains(result.World.Groups, group => group.Name == "GG Password Reset Operators");
        Assert.Contains(result.World.Groups, group => group.Name.StartsWith("ACL FS ", StringComparison.Ordinal));
        Assert.Contains(result.World.Groups, group => group.Name.StartsWith("ACL MBX ", StringComparison.Ordinal));
        Assert.Contains(result.World.Groups, group => group.Name.EndsWith(" Leadership", StringComparison.Ordinal));
        var serviceAccounts = result.World.Accounts.Where(account => account.AccountType == "Service").ToList();
        Assert.NotEmpty(serviceAccounts);
        Assert.Equal(serviceAccounts.Count, serviceAccounts.Select(account => account.SamAccountName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(serviceAccounts, account => account.SamAccountName.StartsWith("svc_sql_", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(serviceAccounts, account => string.Equals(account.SamAccountName, "svc_identityrealismc", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.World.EndpointLocalGroupMembers, member => string.Equals(member.PrincipalType, "BuiltIn", StringComparison.OrdinalIgnoreCase));
        Assert.All(result.World.EndpointLocalGroupMembers, member => Assert.False(string.IsNullOrWhiteSpace(member.PrincipalObjectId)));
        Assert.Contains(result.World.IdentityStores, store =>
            store.StoreType == "ActiveDirectoryDomain"
            && store.DirectoryMode == "HybridDirectory"
            && !string.IsNullOrWhiteSpace(store.NamingContext));
        Assert.Contains(result.World.Containers, container =>
            container.ContainerType == "DirectoryDomain"
            && container.Platform == "ActiveDirectory"
            && !string.IsNullOrWhiteSpace(container.IdentityStoreId));
        Assert.Contains(result.World.Containers, container =>
            container.ContainerType == "OrganizationalUnit"
            && container.Platform == "ActiveDirectory"
            && container.SourceEntityType == nameof(DirectoryOrganizationalUnit)
            && !string.IsNullOrWhiteSpace(container.ParentContainerId));
        Assert.Contains(result.World.Containers, container =>
            container.ContainerType == "OrganizationalUnit"
            && container.BlocksPolicyInheritance);
        Assert.Contains(result.World.Policies, policy =>
            policy.PolicyType == "GroupPolicyObject"
            && policy.Platform == "ActiveDirectory"
            && policy.Name == "Default Domain Policy");
        Assert.Contains(result.World.Policies, policy =>
            policy.PolicyType == "GroupPolicyObject"
            && policy.Name == "Workstation Security Baseline");
        Assert.Contains(result.World.Policies, policy =>
            policy.PolicyType == "GroupPolicyObject"
            && policy.Name == "Legacy Browser Compatibility Staging");
        Assert.Contains(result.World.Policies, policy => policy.Name == "Corporate Desktop Experience");
        Assert.Contains(result.World.Policies, policy => policy.Name == "Enterprise Browser Controls");
        Assert.Contains(result.World.Policies, policy => policy.Name == "User Logon and Drive Mapping");
        Assert.Contains(result.World.Policies, policy => policy.Name == "Office Productivity Controls");
        Assert.Contains(result.World.Policies, policy => policy.Name == "Windows Update Enterprise Ring");
        Assert.Contains(result.World.Policies, policy => policy.Name == "Remote Access and VPN Controls");
        Assert.Contains(result.World.Policies, policy => policy.Name == "Security Audit and Logging Baseline");
        Assert.Contains(result.World.Policies, policy => policy.Name == "Removable Media and Device Control");
        Assert.Contains(result.World.Policies, policy => policy.Name == "Delegated Administration Controls");
        Assert.Contains(result.World.Policies, policy => policy.Name.StartsWith("Desktop Experience - ", StringComparison.Ordinal));
        Assert.Contains(result.World.Policies, policy => policy.Name.StartsWith("Department Collaboration - ", StringComparison.Ordinal));
        Assert.Contains(result.World.Policies, policy => policy.Name == "Windows Account and Lockout Hardening");
        Assert.Contains(result.World.Policies, policy => policy.Name == "Windows User Rights Assignment Baseline");
        Assert.Contains(result.World.Policies, policy => policy.Name == "Windows Security Options Baseline");
        Assert.Contains(result.World.Policies, policy => policy.Name == "Windows Advanced Audit Baseline");
        Assert.Contains(result.World.Policies, policy => policy.Name == "Windows Defender and ASR Baseline");
        Assert.Contains(result.World.Policies, policy => policy.Name == "Windows Network and Firewall Baseline");
        Assert.Contains(result.World.Policies, policy => policy.Name == "Chrome Enterprise Security Baseline");
        Assert.Contains(result.World.Policies, policy => policy.Name == "Chrome Content and Update Controls");
        Assert.Contains(result.World.Policies, policy => policy.Name == "Office Trust Center and Macro Baseline");
        Assert.Contains(result.World.Policies, policy => policy.Name == "Office Privacy and Update Baseline");
        Assert.Contains(result.World.Policies, policy => policy.Name == "Intune Windows Security Baseline");
        Assert.Contains(result.World.Policies, policy => policy.Name == "Conditional Access - Admin MFA");
        Assert.DoesNotContain(result.World.Policies, policy => string.IsNullOrWhiteSpace(policy.PolicyGuid));
        Assert.Contains(result.World.PolicySettings, setting =>
            setting.SettingName == "MinimumPasswordLength"
            && setting.ConfiguredValue == "14"
            && setting.PolicyPath == @"Computer Configuration\Windows Settings\Account Policies\Password Policy");
        Assert.Contains(result.World.PolicySettings, setting =>
            setting.SettingName == "LanManCompatibilityLevel"
            && setting.IsLegacy
            && setting.PolicyPath == @"Computer Configuration\Windows Settings\Security Settings\Local Policies\Security Options");
        Assert.Contains(result.World.PolicySettings, setting => setting.SettingName == "DesktopWallpaperPath" && setting.PolicyPath == @"User Configuration\Administrative Templates\Desktop\Desktop");
        Assert.Contains(result.World.PolicySettings, setting => setting.SettingName == "BrowserPasswordManagerAllowed" && setting.PolicyPath.Contains("Microsoft Edge", StringComparison.Ordinal));
        Assert.Contains(result.World.PolicySettings, setting => setting.SettingName == "PrimaryLogonScript" && setting.PolicyPath == @"User Configuration\Windows Settings\Scripts (Logon/Logoff)");
        Assert.Contains(result.World.PolicySettings, setting => setting.SettingName == "OfficeMacroPolicy" && setting.PolicyPath.Contains(@"Microsoft Office 2016\Security Settings\Trust Center", StringComparison.Ordinal));
        Assert.Contains(result.World.PolicySettings, setting => setting.SettingName == "WindowsUpdateDeferralDays" && setting.PolicyPath.Contains(@"Windows Components\Windows Update", StringComparison.Ordinal));
        Assert.Contains(result.World.PolicySettings, setting => setting.SettingName == "AuditLogonEvents" && setting.PolicyPath.Contains(@"Advanced Audit Policy Configuration\Audit Policies", StringComparison.Ordinal));
        Assert.Contains(result.World.PolicySettings, setting => setting.SettingName == "UsbStorageReadPolicy" && setting.PolicyPath.Contains(@"System\Removable Storage Access", StringComparison.Ordinal));
        Assert.Contains(result.World.PolicySettings, setting => setting.SettingName == "LapsPasswordReadScope" && setting.PolicyPath == @"Computer Configuration\Windows Settings\Security Settings\Local Policies\User Rights Assignment");
        Assert.Contains(result.World.PolicySettings, setting => setting.SettingName == "EnforcePasswordHistory" && setting.PolicyPath == @"Computer Configuration\Windows Settings\Account Policies\Password Policy");
        Assert.Contains(result.World.PolicySettings, setting => setting.SettingName == "AllowLogOnThroughRemoteDesktopServices" && setting.PolicyPath == @"Computer Configuration\Windows Settings\Security Settings\Local Policies\User Rights Assignment");
        Assert.Contains(result.World.PolicySettings, setting => setting.SettingName == "UserAccountControlRunAllAdministratorsInAdminApprovalMode" && setting.PolicyPath == @"Computer Configuration\Windows Settings\Security Settings\Local Policies\Security Options");
        Assert.Contains(result.World.PolicySettings, setting => setting.SettingName == "AuditKerberosAuthenticationService" && setting.PolicyPath.Contains(@"Advanced Audit Policy Configuration\Audit Policies", StringComparison.Ordinal));
        Assert.Contains(result.World.PolicySettings, setting => setting.SettingName == "ExploitGuardAttackSurfaceReductionOfficeChildProcess" && setting.PolicyPath.Contains(@"Attack Surface Reduction", StringComparison.Ordinal));
        Assert.Contains(result.World.PolicySettings, setting => setting.SettingName == "WindowsFirewallDomainProfileState" && setting.PolicyPath.Contains(@"Windows Defender Firewall with Advanced Security", StringComparison.Ordinal));
        Assert.Contains(result.World.PolicySettings, setting => setting.SettingName == "ChromeSafeBrowsingProtectionLevel" && setting.PolicyPath == @"Computer Configuration\Administrative Templates\Google\Google Chrome");
        Assert.Contains(result.World.PolicySettings, setting => setting.SettingName == "ChromeExtensionInstallForcelist" && setting.PolicyPath == @"Computer Configuration\Administrative Templates\Google\Google Chrome");
        Assert.Contains(result.World.PolicySettings, setting => setting.SettingName == "OfficeBlockMacrosFromInternet" && setting.PolicyPath.Contains(@"Microsoft Office 2016\Security Settings\Trust Center", StringComparison.Ordinal));
        Assert.Contains(result.World.PolicySettings, setting => setting.SettingName == "OfficeEnableAutomaticUpdates" && setting.PolicyPath.Contains(@"Microsoft Office 2016\Updates", StringComparison.Ordinal));
        Assert.DoesNotContain(result.World.PolicySettings, setting => string.IsNullOrWhiteSpace(setting.PolicyPath));
        Assert.True(result.World.PolicySettings.Count >= 800);
        Assert.DoesNotContain(result.World.Policies.Where(policy =>
                policy.PolicyType == "GroupPolicyObject"
                && !string.Equals(policy.Status, "Disabled", StringComparison.OrdinalIgnoreCase)), policy =>
            !result.World.PolicyTargetLinks.Any(link =>
                link.PolicyId == policy.Id
                && link.TargetType == "Container"
                && link.AssignmentMode == "Linked"
                && link.LinkEnabled));
        Assert.DoesNotContain(result.World.Policies.Where(policy => policy.Platform == "Intune" || policy.PolicyType == "ConditionalAccessPolicy"), policy =>
            !result.World.PolicyTargetLinks.Any(link => link.PolicyId == policy.Id && link.TargetType == "IdentityStore" && link.AssignmentMode == "Scope"));
        Assert.Contains(result.World.PolicyTargetLinks, link =>
            link.TargetType == "Container"
            && link.AssignmentMode == "Linked"
            && link.LinkEnabled
            && result.World.Containers.Any(container => container.Id == link.TargetId && container.ContainerType == "DirectoryDomain"));
        Assert.Contains(result.World.PolicyTargetLinks, link =>
            link.TargetType == "Group"
            && link.AssignmentMode == "DelegatedAdministration");
        Assert.Contains(result.World.PolicyTargetLinks, link =>
            link.TargetType == "Group"
            && link.AssignmentMode == "SecurityFilterExclude");
        Assert.Contains(result.World.PolicyTargetLinks, link =>
            link.AssignmentMode == "Linked"
            && !link.LinkEnabled);
        Assert.Contains(result.World.PolicyTargetLinks, link =>
            link.AssignmentMode == "WmiFilter"
            && link.FilterType == "WmiQuery"
            && !string.IsNullOrWhiteSpace(link.FilterValue));
        Assert.Contains(result.World.PolicyTargetLinks, link =>
            link.TargetType == "IdentityStore"
            && link.AssignmentMode == "Scope");
        Assert.Contains(result.World.AccessControlEvidence, evidence =>
            evidence.TargetType == "Container"
            && evidence.RightName == "ApplyGroupPolicy"
            && evidence.SourceSystem == "ActiveDirectory");
        Assert.Contains(result.World.AccessControlEvidence, evidence =>
            evidence.TargetType == "Policy"
            && evidence.RightName == "EditSettings"
            && evidence.SourceSystem == "ActiveDirectory");
        Assert.Contains(result.World.AccessControlEvidence, evidence =>
            evidence.TargetType == "Policy"
            && evidence.RightName == "EditPermissions"
            && evidence.SourceSystem == "ActiveDirectory");
        Assert.Contains(result.World.AccessControlEvidence, evidence =>
            evidence.TargetType == "Container"
            && evidence.RightName == "ApplyGroupPolicy"
            && evidence.AccessType == "Deny"
            && evidence.SourceSystem == "ActiveDirectory");
        Assert.Contains(result.World.AccessControlEvidence, evidence =>
            evidence.TargetType == "Container"
            && evidence.RightName == "CreateComputerObject"
            && evidence.SourceSystem == "ActiveDirectory");
        Assert.Contains(result.World.AccessControlEvidence, evidence =>
            evidence.TargetType == "Container"
            && evidence.RightName == "CreateChild"
            && evidence.SourceSystem == "ActiveDirectory");
        Assert.Contains(result.World.AccessControlEvidence, evidence =>
            evidence.TargetType == "Container"
            && evidence.RightName == "ResetPassword"
            && evidence.SourceSystem == "ActiveDirectory");
        Assert.Contains(result.World.AccessControlEvidence, evidence =>
            evidence.TargetType == "Container"
            && evidence.RightName == "ReadLapsPassword"
            && evidence.SourceSystem == "ActiveDirectory");
        Assert.Contains(result.World.AccessControlEvidence, evidence =>
            evidence.TargetType == "Container"
            && evidence.RightName == "RemoteAssist"
            && evidence.SourceSystem == "ActiveDirectory");
        Assert.Contains(result.World.ExternalOrganizations, organization => organization.RelationshipType == "ManagedServiceProvider");
        Assert.DoesNotContain(result.World.ExternalOrganizations, organization =>
            organization.Name.Contains("Partner Collaboration", StringComparison.OrdinalIgnoreCase)
            && organization.Name.Contains("Identity Realism Co", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.World.CrossTenantAccessPolicies, policy => policy.RelationshipType == "ManagedServiceProvider");
        Assert.Contains(result.World.CrossTenantAccessEvents, accessEvent => accessEvent.EventType == "InvitationSent");
        Assert.Contains(result.World.CrossTenantAccessEvents, accessEvent => accessEvent.EventType == "AccessStateEvaluated");
        Assert.All(
            result.World.Accounts.Where(account =>
                string.Equals(account.IdentityProvider, "EntraB2B", StringComparison.OrdinalIgnoreCase)),
            account =>
            {
                Assert.False(string.IsNullOrWhiteSpace(account.InvitedOrganizationId));
                Assert.False(string.IsNullOrWhiteSpace(account.InvitedByAccountId));
                Assert.False(string.IsNullOrWhiteSpace(account.HomeTenantDomain));
                Assert.False(string.IsNullOrWhiteSpace(account.ResourceTenantDomain));
                Assert.False(string.IsNullOrWhiteSpace(account.InvitationStatus));
                Assert.False(string.IsNullOrWhiteSpace(account.GuestLifecycleState));
                Assert.False(string.IsNullOrWhiteSpace(account.CrossTenantAccessPolicy));
                Assert.False(string.IsNullOrWhiteSpace(account.EntitlementPackageName));
                Assert.False(string.IsNullOrWhiteSpace(account.EntitlementAssignmentState));
                Assert.False(string.IsNullOrWhiteSpace(account.AccessReviewStatus));
                Assert.NotNull(account.InvitationSentAt);
                if (string.Equals(account.InvitationStatus, "Accepted", StringComparison.OrdinalIgnoreCase))
                {
                    Assert.NotNull(account.InvitationRedeemedAt);
                    Assert.NotNull(account.LastAccessReviewAt);
                }

                if (account.SponsorLastChangedAt is not null)
                {
                    Assert.False(string.IsNullOrWhiteSpace(account.PreviousInvitedByAccountId));
                }
            });
        Assert.DoesNotContain(result.World.People, person =>
        {
            if (string.IsNullOrWhiteSpace(person.OfficeId))
            {
                return false;
            }

            var office = result.World.Offices.FirstOrDefault(candidate => candidate.Id == person.OfficeId);
            return office is not null && !string.Equals(person.Country, office.Country, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void WorldGenerator_Assigns_Workstations_And_Servers_To_Directory_Ous()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var result = generator.Generate(
            new GenerationContext
            {
                Scenario = new ScenarioDefinition
                {
                    Name = "Infrastructure Directory Test",
                    Companies = new()
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Infrastructure Directory Co",
                            Industry = "Manufacturing",
                            EmployeeCount = 240,
                            BusinessUnitCount = 2,
                            DepartmentCountPerBusinessUnit = 4,
                            TeamCountPerDepartment = 2,
                            OfficeCount = 2,
                            ServerCount = 12,
                            IncludePrivilegedAccounts = true,
                            Countries = new() { "United States" }
                        }
                    }
                }
            },
            new CatalogSet());

        Assert.NotEmpty(result.World.Devices);
        Assert.NotEmpty(result.World.Servers);
        Assert.All(result.World.Devices, device =>
        {
            Assert.False(string.IsNullOrWhiteSpace(device.OuId));
            Assert.False(string.IsNullOrWhiteSpace(device.DistinguishedName));
        });
        Assert.All(result.World.Servers, server =>
        {
            Assert.False(string.IsNullOrWhiteSpace(server.OuId));
            Assert.False(string.IsNullOrWhiteSpace(server.DistinguishedName));
        });
        Assert.Contains(result.World.Devices, device => device.DeviceType == "PrivilegedAccessWorkstation");
        Assert.Contains(result.World.Devices, device => device.DistinguishedName!.Contains("OU=Privileged Access Workstations", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.World.EndpointAdministrativeAssignments, assignment =>
            assignment.EndpointType == "Device"
            && assignment.AssignmentScope == "JustInTimeEligible");
        Assert.Contains(result.World.EndpointAdministrativeAssignments, assignment =>
            assignment.EndpointType == "Server"
            && assignment.ManagementPlane == "PrivilegedAccessManagement");
        Assert.Contains(result.World.EndpointPolicyBaselines, baseline =>
            baseline.EndpointType == "Device"
            && baseline.PolicyName == "Windows LAPS Rotation");
        Assert.Contains(result.World.EndpointPolicyBaselines, baseline =>
            baseline.EndpointType == "Server"
            && baseline.PolicyName == "Server Security Baseline");

        Assert.Contains(result.World.Servers, server => server.Environment == "Production");
        Assert.Contains(result.World.Servers, server => server.Environment == "Staging");
        Assert.Contains(result.World.Servers, server => server.Environment == "Development");
        Assert.Contains(result.World.Servers, server => server.DistinguishedName!.Contains("OU=Production", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.World.Servers, server => server.DistinguishedName!.Contains("OU=Staging", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.World.Servers, server => server.DistinguishedName!.Contains("OU=Development", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WorldGenerator_Uses_Role_Aligned_Network_Assets()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var result = generator.Generate(
            new GenerationContext
            {
                Scenario = new ScenarioDefinition
                {
                    Name = "Network Realism Test",
                    Companies = new()
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Network Realism Co",
                            Industry = "Manufacturing",
                            EmployeeCount = 180,
                            BusinessUnitCount = 2,
                            DepartmentCountPerBusinessUnit = 3,
                            TeamCountPerDepartment = 2,
                            OfficeCount = 2,
                            NetworkAssetCountPerOffice = 6,
                            Countries = new() { "United States" }
                        }
                    }
                }
            },
            new CatalogSet());

        Assert.Contains(result.World.NetworkAssets, asset =>
            asset.AssetType == "Switch"
            && asset.Vendor == "Cisco"
            && asset.Model == "Catalyst 9300"
            && asset.Hostname.StartsWith("SW-", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.World.NetworkAssets, asset =>
            asset.AssetType == "Firewall"
            && asset.Vendor == "Palo Alto"
            && asset.Model == "PA-3410"
            && asset.Hostname.StartsWith("FW-", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.World.NetworkAssets, asset =>
            asset.AssetType == "Access Point"
            && asset.Vendor == "Aruba"
            && asset.Model == "AP-635"
            && asset.Hostname.StartsWith("AP-", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.World.NetworkAssets, asset =>
            asset.AssetType == "Firewall"
            && asset.Vendor == "Cisco");
        Assert.DoesNotContain(result.World.NetworkAssets, asset =>
            asset.AssetType == "Switch"
            && asset.Vendor == "Palo Alto");
    }

    [Fact]
    public void WorldGenerator_Avoids_Location_Code_Collisions_For_Similar_City_Names()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var result = generator.Generate(
            new GenerationContext
            {
                Seed = 4242,
                Scenario = new ScenarioDefinition
                {
                    Name = "Network City Collision Test",
                    Companies = new()
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Network Collision Co",
                            Industry = "Manufacturing",
                            EmployeeCount = 120,
                            BusinessUnitCount = 1,
                            DepartmentCountPerBusinessUnit = 2,
                            TeamCountPerDepartment = 1,
                            OfficeCount = 2,
                            NetworkAssetCountPerOffice = 6,
                            Countries = new() { "Mexico" }
                        }
                    }
                }
            },
            new CatalogSet
            {
                CsvCatalogs = new Dictionary<string, IReadOnlyList<Dictionary<string, string?>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["locality_reference"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("CountryCode", "MX"), ("StateCode", "MEX"), ("StateOrProvince", "Estado de Mexico"), ("City", "Naucalpan"), ("PostalCode", "53000"), ("TimeZone", "America/Mexico_City"), ("Latitude", "19.4753"), ("Longitude", "-99.2378"), ("Population", "776220"), ("Accuracy", "6")),
                        NewRow(("CountryCode", "MX"), ("StateCode", "MEX"), ("StateOrProvince", "Estado de Mexico"), ("City", "Naucalpan de Juarez"), ("PostalCode", "53100"), ("TimeZone", "America/Mexico_City"), ("Latitude", "19.4787"), ("Longitude", "-99.2386"), ("Population", "792211"), ("Accuracy", "6"))
                    },
                    ["countries_reference"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Name", "Mexico"), ("Code", "MX"), ("Continent", "North America"))
                    }
                }
            });

        Assert.Equal(
            result.World.NetworkAssets.Count,
            result.World.NetworkAssets.Select(asset => asset.Hostname).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(result.World.NetworkAssets, asset => asset.Hostname.Contains("-NAUCAL-", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.World.NetworkAssets, asset => asset.Hostname.Contains("-NAUJUA-", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WorldGenerator_Uses_Role_Aware_Server_Hostnames()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var result = generator.Generate(
            new GenerationContext
            {
                Scenario = new ScenarioDefinition
                {
                    Name = "Server Hostname Realism Test",
                    Companies = new()
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Server Hostname Co",
                            Industry = "Manufacturing",
                            EmployeeCount = 180,
                            BusinessUnitCount = 2,
                            DepartmentCountPerBusinessUnit = 3,
                            TeamCountPerDepartment = 2,
                            OfficeCount = 2,
                            ServerCount = 7,
                            Countries = new() { "United States" }
                        }
                    }
                }
            },
            new CatalogSet());

        Assert.Contains(result.World.Servers, server => server.ServerRole == "Domain Controller" && server.Hostname.Contains("-DC-", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.World.Servers, server => server.ServerRole == "File Server" && server.Hostname.Contains("-FS-", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.World.Servers, server => server.ServerRole == "SQL Server" && server.Hostname.Contains("-SQL-", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.World.Servers, server => server.ServerRole == "Web Server" && server.Hostname.Contains("-WEB-", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.World.Servers, server => server.ServerRole == "Application Server" && server.Hostname.Contains("-APP-", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.World.Servers, server => server.ServerRole == "Jump Host");
        Assert.Contains(result.World.Servers, server => server.ServerRole == "Print Server" && server.Hostname.Contains("-PRN-", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.World.Servers, server => server.Hostname.Contains("DOMAIN", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.World.Servers, server => server.Hostname.Contains("FILESE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WorldGenerator_Concentrates_Server_Footprint_At_Headquarters()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var result = generator.Generate(
            new GenerationContext
            {
                Seed = 29,
                Scenario = new ScenarioDefinition
                {
                    Name = "Server Distribution Realism Test",
                    Companies = new()
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Server Distribution Co",
                            Industry = "Manufacturing",
                            EmployeeCount = 220,
                            BusinessUnitCount = 3,
                            DepartmentCountPerBusinessUnit = 3,
                            TeamCountPerDepartment = 2,
                            OfficeCount = 6,
                            ServerCount = 60,
                            Countries = new() { "United States", "Canada", "Mexico" }
                        }
                    }
                }
            },
            new CatalogSet());

        var officesById = result.World.Offices.ToDictionary(office => office.Id, StringComparer.OrdinalIgnoreCase);
        var serverCounts = result.World.Servers
            .Where(server => !string.IsNullOrWhiteSpace(server.OfficeId))
            .GroupBy(server => server.OfficeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        var headquarters = Assert.Single(result.World.Offices, office => office.IsHeadquarters);
        Assert.True(serverCounts.TryGetValue(headquarters.Id, out var headquartersCount));
        Assert.True(headquartersCount > 0);
        Assert.True(headquartersCount > serverCounts.Values.Min());
        Assert.True(serverCounts.Count >= 3);
        Assert.All(
            result.World.Servers.Where(server => !string.IsNullOrWhiteSpace(server.OfficeId)),
            server => Assert.True(officesById.ContainsKey(server.OfficeId)));
    }

    [Fact]
    public void WorldGenerator_Keeps_External_Account_User_Principal_Names_Unique_At_Scale()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var catalogLoader = services.GetRequiredService<ICatalogLoader>();
        var generator = services.GetRequiredService<IWorldGenerator>();
        var auditService = services.GetRequiredService<IWorldQualityAuditService>();
        var result = generator.Generate(
            new GenerationContext
            {
                Seed = 42,
                Scenario = new ScenarioDefinition
                {
                    Name = "Large External Workforce",
                    IndustryProfile = "Manufacturing",
                    GeographyProfile = "North-America",
                    EmployeeSize = new SizeBand { Minimum = 4800, Maximum = 5200 },
                    Identity = new IdentityProfile
                    {
                        IncludeHybridDirectory = true,
                        IncludeM365StyleGroups = true,
                        IncludeAdministrativeTiers = true,
                        IncludeExternalWorkforce = true,
                        IncludeB2BGuests = true,
                        ContractorRatio = 0.10,
                        ManagedServiceProviderRatio = 0.03,
                        GuestUserRatio = 0.08,
                        StaleAccountRate = 0.04
                    },
                    Companies = new()
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Duckburg Scale Test",
                            Industry = "Manufacturing",
                            EmployeeCount = 5000,
                            BusinessUnitCount = 6,
                            DepartmentCountPerBusinessUnit = 4,
                            TeamCountPerDepartment = 3,
                            OfficeCount = 6,
                            SharedMailboxCount = 18,
                            ServiceAccountCount = 30,
                            IncludePrivilegedAccounts = true,
                            Countries = new() { "United States", "Canada", "Mexico" }
                        }
                    }
                }
            },
            catalogLoader.LoadDefault());
        var audit = auditService.Audit(result.World);

        var distinctAccountUpns = result.World.Accounts
            .Select(account => account.UserPrincipalName)
            .Where(upn => !string.IsNullOrWhiteSpace(upn))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        Assert.Equal(result.World.Accounts.Count, distinctAccountUpns);
        Assert.Equal(0, audit.Metrics["duplicate_person_display_names"]);
        Assert.True(audit.Metrics["max_person_display_name_repeat"] <= 1);
        Assert.DoesNotContain(
            result.Warnings,
            warning => warning.Contains("duplicate directory account user principal", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            result.Warnings,
            warning => warning.Contains("duplicate person display names", StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<string, string?> NewRow(params (string Key, string? Value)[] entries)
    {
        var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in entries)
        {
            row[key] = value;
        }

        return row;
    }
}
