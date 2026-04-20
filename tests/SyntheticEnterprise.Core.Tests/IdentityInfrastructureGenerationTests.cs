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
        Assert.Equal(result.World.People.Count, distinctPersonUpns);
        Assert.Equal(result.World.Accounts.Count, distinctAccountUpns);
        Assert.Contains(result.World.GroupMemberships, membership => membership.MemberObjectType == "Group");
        Assert.Contains(result.World.OrganizationalUnits, ou => ou.Name == "Computers");
        Assert.Contains(result.World.OrganizationalUnits, ou => ou.Name == "Workstations");
        Assert.Contains(result.World.OrganizationalUnits, ou => ou.Name == "Servers");
        Assert.Contains(result.World.OrganizationalUnits, ou => ou.Name == "Admin Accounts");
        Assert.Contains(result.World.OrganizationalUnits, ou => ou.Name == "Tier 0");
        Assert.Contains(result.World.OrganizationalUnits, ou => ou.Name == "Tier 1");
        Assert.Contains(result.World.OrganizationalUnits, ou => ou.Name == "Tier 2");
        Assert.Contains(result.World.OrganizationalUnits, ou => ou.Name == "Privileged Access Workstations");
        Assert.Contains(result.World.Groups, group => group.Name == "SG-PrivilegedAccess");
        Assert.Contains(result.World.Groups, group => group.Name == "SG-Tier0-PAW-Users");
        Assert.Contains(result.World.Groups, group => group.Name == "SG-Tier1-PAW-Users");
        Assert.Contains(result.World.Groups, group => group.Name == "SG-Tier0-PAW-Devices");
        Assert.Contains(result.World.Groups, group => group.Name == "SG-Tier1-PAW-Devices");
        Assert.Contains(result.World.Groups, group => group.Name == "SG-Tier1-ManagedWorkstations");
        Assert.Contains(result.World.Groups, group => group.Name == "SG-Tier1-ManagedServers");
        Assert.Contains(result.World.Groups, group => !string.IsNullOrWhiteSpace(group.AdministrativeTier));
        Assert.Contains(result.World.Accounts, account => account.AccountType == "Privileged" && !string.IsNullOrWhiteSpace(account.AdministrativeTier));
        Assert.Contains(result.World.GroupMemberships, membership =>
            membership.MemberObjectType == "Account"
            && result.World.Groups.Any(group =>
                group.Id == membership.GroupId
                && (group.Name == "SG-Tier0-PAW-Users" || group.Name == "SG-Tier1-PAW-Users")));
        Assert.Contains(result.World.GroupMemberships, membership => membership.MemberObjectType == "Device");
        Assert.Contains(result.World.GroupMemberships, membership => membership.MemberObjectType == "Server");
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
        Assert.Contains(result.World.OrganizationalUnits, ou => ou.Name == "External Users");
        Assert.Contains(result.World.OrganizationalUnits, ou => ou.Name == "Contractors");
        Assert.Contains(result.World.OrganizationalUnits, ou => ou.Name == "Managed Services");
        Assert.Contains(result.World.OrganizationalUnits, ou => ou.Name == "Guests");
        Assert.Contains(result.World.People, person => person.EmploymentType == "Contractor");
        Assert.Contains(result.World.People, person => person.EmploymentType == "ManagedServiceProvider");
        Assert.Contains(result.World.People, person => person.EmploymentType == "Guest");
        Assert.Contains(result.World.Accounts, account => account.AccountType == "Contractor" && account.UserType == "Member");
        Assert.Contains(result.World.Accounts, account => account.AccountType == "ManagedServiceProvider" && account.UserType == "Guest");
        Assert.Contains(result.World.Accounts, account => account.AccountType == "Guest" && account.IdentityProvider == "EntraB2B");
        Assert.Contains(result.World.Groups, group => group.Name == "SG-ExternalContractors");
        Assert.Contains(result.World.Groups, group => group.Name == "SG-MSP-Operators");
        Assert.Contains(result.World.Groups, group => group.Name == "SG-B2BGuests");
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
        Assert.Contains(result.World.PolicySettings, setting =>
            setting.SettingName == "MinimumPasswordLength"
            && setting.ConfiguredValue == "14");
        Assert.Contains(result.World.PolicySettings, setting =>
            setting.SettingName == "LanManCompatibilityLevel"
            && setting.IsLegacy);
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
        Assert.Contains(result.World.ExternalOrganizations, organization => organization.RelationshipType == "ManagedServiceProvider");
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
        Assert.Contains(result.World.Servers, server => server.ServerRole == "Jump Host" && server.Hostname.Contains("-JMP-", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.World.Servers, server => server.ServerRole == "Print Server" && server.Hostname.Contains("-PRN-", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.World.Servers, server => server.Hostname.Contains("DOMAIN", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.World.Servers, server => server.Hostname.Contains("FILESE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WorldGenerator_Keeps_External_Account_User_Principal_Names_Unique_At_Scale()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
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
            new CatalogSet());

        var distinctAccountUpns = result.World.Accounts
            .Select(account => account.UserPrincipalName)
            .Where(upn => !string.IsNullOrWhiteSpace(upn))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        Assert.Equal(result.World.Accounts.Count, distinctAccountUpns);
        Assert.DoesNotContain(
            result.Warnings,
            warning => warning.Contains("duplicate directory account user principal", StringComparison.OrdinalIgnoreCase));
    }
}
