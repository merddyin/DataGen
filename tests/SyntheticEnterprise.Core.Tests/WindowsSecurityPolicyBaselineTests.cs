using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Core.Abstractions;
using SyntheticEnterprise.Core.DependencyInjection;
using SyntheticEnterprise.Core.Generation.Policy;

namespace SyntheticEnterprise.Core.Tests;

public sealed class WindowsSecurityPolicyBaselineTests
{
    private const string SecurityTemplatePolicyName = "Windows Security Template Baseline";
    private const string AuditTemplatePolicyName = "Windows Advanced Audit Policy Template";
    private const string DomainControllerAuditPolicyName = "Domain Controller Audit Baseline";

    /// <summary>
    /// Display names Windows itself returns for the six SIDs whose curated resolver form
    /// differs. A real collection never produces these, so neither may the generator.
    /// </summary>
    private static readonly string[] UncuratedWindowsPrincipalForms =
    [
        @"NT AUTHORITY\ANONYMOUS LOGON",
        @"NT AUTHORITY\Authenticated Users",
        @"NT AUTHORITY\SYSTEM",
        @"NT AUTHORITY\LOCAL SERVICE",
        @"NT AUTHORITY\NETWORK SERVICE",
        "CREATOR OWNER"
    ];

    [Fact]
    public void SecurityTemplateBaseline_Emits_Canonical_Keys_With_Exact_Prefixes()
    {
        var world = Generate();
        var settings = SettingsFor(world, SecurityTemplatePolicyName);

        var userRightSettings = settings
            .Where(setting => setting.SettingCategory == "UserRightsAssignment")
            .ToArray();
        Assert.NotEmpty(userRightSettings);
        Assert.All(userRightSettings, setting =>
        {
            Assert.StartsWith("UserRight:", setting.PolicyPath, StringComparison.Ordinal);
            var privilegeConstant = setting.PolicyPath["UserRight:".Length..];
            Assert.Equal(setting.SettingName, privilegeConstant);
            Assert.StartsWith("Se", privilegeConstant, StringComparison.Ordinal);
            Assert.DoesNotContain(":", privilegeConstant, StringComparison.Ordinal);
            Assert.DoesNotContain("\\", privilegeConstant, StringComparison.Ordinal);
            Assert.DoesNotContain("!", privilegeConstant, StringComparison.Ordinal);
            Assert.Equal("SecTemplate", setting.Source);
        });

        var fileAclSettings = settings
            .Where(setting => setting.SettingCategory == "FileSecurity")
            .ToArray();
        Assert.NotEmpty(fileAclSettings);
        Assert.All(fileAclSettings, setting =>
        {
            Assert.StartsWith("FileACL:", setting.PolicyPath, StringComparison.Ordinal);
            Assert.Equal(setting.SettingName, setting.PolicyPath["FileACL:".Length..]);
            Assert.Equal("SecTemplate", setting.Source);
            Assert.Equal("RedDot", setting.Behavior);
        });

        var registryAclSettings = settings
            .Where(setting => setting.SettingCategory == "RegistryKeys")
            .ToArray();
        Assert.NotEmpty(registryAclSettings);
        Assert.All(registryAclSettings, setting =>
        {
            Assert.StartsWith("RegistryACL:", setting.PolicyPath, StringComparison.Ordinal);
            Assert.Equal(setting.SettingName, setting.PolicyPath["RegistryACL:".Length..]);
            Assert.Equal("SecTemplate", setting.Source);
            Assert.Equal("RedDot", setting.Behavior);
        });

        Assert.Contains(settings, setting => setting.PolicyPath == @"FileACL:%SystemRoot%\System32\config");
        Assert.Contains(settings, setting => setting.PolicyPath == @"FileACL:%SystemRoot%\System32\drivers");
        Assert.Contains(settings, setting => setting.PolicyPath == @"FileACL:%SystemDrive%\Program Files");
        Assert.Contains(settings, setting => setting.PolicyPath == @"RegistryACL:MACHINE\SYSTEM\CurrentControlSet\Services");
        Assert.Contains(settings, setting => setting.PolicyPath == @"RegistryACL:MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies");
    }

    [Fact]
    public void AuditPolicyTemplate_Emits_Canonical_Keys_That_Repeat_The_Subcategory_Word()
    {
        var world = Generate();
        var settings = SettingsFor(world, AuditTemplatePolicyName);

        Assert.NotEmpty(settings);
        Assert.All(settings, setting =>
        {
            Assert.Equal("AuditPolicy", setting.SettingCategory);
            Assert.StartsWith("Audit:", setting.PolicyPath, StringComparison.Ordinal);
            var subcategory = setting.PolicyPath["Audit:".Length..];
            Assert.Equal(setting.SettingName, subcategory);
            Assert.StartsWith("Audit ", subcategory, StringComparison.Ordinal);
            Assert.DoesNotContain(",", subcategory, StringComparison.Ordinal);
            Assert.Contains(subcategory, WindowsSecurityPolicyCatalog.AuditSubcategoryNames);
        });

        Assert.Contains(settings, setting => setting.PolicyPath == "Audit:Audit Credential Validation");
        Assert.Contains(settings, setting => setting.PolicyPath == "Audit:Audit PNP Activity");
        Assert.Contains(settings, setting => setting.PolicyPath == "Audit:Audit MPSSVC Rule-Level Policy Change");
        Assert.Contains(settings, setting => setting.PolicyPath == "Audit:Audit Other Logon/Logoff Events");
    }

    [Fact]
    public void SecurityTemplateBaseline_Uses_Only_Resolver_Mirrored_Principal_Forms()
    {
        var world = Generate();
        var company = Assert.Single(world.Companies);
        var settings = SettingsFor(world, SecurityTemplatePolicyName);

        var tier1 = WindowsSecurityPolicyCatalog.Tier1PrincipalTable
            .Select(entry => entry.DisplayName)
            .ToHashSet(StringComparer.Ordinal);
        var tier2 = WindowsSecurityPolicyCatalog.Tier2PrincipalTable
            .Select(entry => entry.DisplayName)
            .ToHashSet(StringComparer.Ordinal);
        var activeDirectoryStore = Assert.Single(
            world.IdentityStores,
            store => store.CompanyId == company.Id
                && store.StoreType == "ActiveDirectoryDomain"
                && store.EnvironmentRole == "Source");
        var netBiosName = WindowsSecurityPolicyCatalog.BuildNetBiosName(activeDirectoryStore.PrimaryDomain);
        Assert.False(string.IsNullOrWhiteSpace(netBiosName));
        var generatedGroupPrincipals = world.Groups
            .Where(group => group.CompanyId == company.Id)
            .Select(group => WindowsSecurityPolicyCatalog.QualifyDomainPrincipal(netBiosName, group.Name))
            .ToHashSet(StringComparer.Ordinal);
        var generatedServiceAccountPrincipals = world.Accounts
            .Where(account => account.CompanyId == company.Id
                && account.AccountType == "Service"
                && account.Domain == activeDirectoryStore.PrimaryDomain)
            .Select(account => WindowsSecurityPolicyCatalog.QualifyDomainPrincipal(netBiosName, account.SamAccountName))
            .ToHashSet(StringComparer.Ordinal);

        var tokens = settings
            .Where(setting => setting.SettingCategory == "UserRightsAssignment")
            .SelectMany(setting => setting.ConfiguredValue.Split(',', StringSplitOptions.RemoveEmptyEntries))
            .ToArray();

        Assert.NotEmpty(tokens);
        Assert.All(tokens, token =>
        {
            Assert.Equal(token, token.Trim());
            Assert.False(Regex.IsMatch(token, @"^S-\d-\d"), $"Bare SID emitted as a principal: {token}");
            Assert.True(
                tier1.Contains(token)
                    || tier2.Contains(token)
                    || generatedGroupPrincipals.Contains(token)
                    || generatedServiceAccountPrincipals.Contains(token),
                $"Principal '{token}' is not a resolver-mirrored form or a generated domain principal.");
        });

        var distinctTokens = tokens.ToHashSet(StringComparer.Ordinal);
        Assert.Contains(@"BUILTIN\Administrators", distinctTokens);
        Assert.Contains(@"BUILTIN\Users", distinctTokens);
        Assert.Contains(@"BUILTIN\Guests", distinctTokens);
        Assert.Contains(@"BUILTIN\Backup Operators", distinctTokens);
        Assert.Contains(@"BUILTIN\Remote Desktop Users", distinctTokens);
        Assert.Contains("Authenticated Users", distinctTokens);
        Assert.Contains("Local Service", distinctTokens);
        Assert.Contains("Network Service", distinctTokens);
        Assert.Contains(@"NT AUTHORITY\SERVICE", distinctTokens);
        Assert.Contains(@"NT AUTHORITY\ENTERPRISE DOMAIN CONTROLLERS", distinctTokens);
        Assert.Contains(@"NT AUTHORITY\Local account", distinctTokens);
        Assert.Contains(@"NT AUTHORITY\Local account and member of Administrators group", distinctTokens);
        Assert.Contains(distinctTokens, token => generatedGroupPrincipals.Contains(token));
        Assert.Contains(distinctTokens, token => generatedServiceAccountPrincipals.Contains(token));

        // Log on as a service belongs to the identities that run services, so every
        // principal on that right must be a generated service account.
        var serviceLogonSetting = Assert.Single(settings, setting => setting.SettingName == "SeServiceLogonRight");
        var serviceLogonTokens = serviceLogonSetting.ConfiguredValue.Split(',', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, serviceLogonTokens.Length);
        Assert.All(serviceLogonTokens, token => Assert.Contains(token, generatedServiceAccountPrincipals));

        foreach (var uncuratedForm in UncuratedWindowsPrincipalForms)
        {
            Assert.DoesNotContain(uncuratedForm, distinctTokens);
        }
    }

    [Fact]
    public void SecurityTemplateBaseline_Carries_Deny_And_Required_Grant_Rights()
    {
        var world = Generate();
        var settings = SettingsFor(world, SecurityTemplatePolicyName);
        var settingNames = settings
            .Select(setting => setting.SettingName)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(settingNames, name => name.StartsWith("SeDeny", StringComparison.Ordinal));
        foreach (var denyRight in WindowsSecurityPolicyCatalog.DenyRightConstants)
        {
            Assert.Contains(denyRight, settingNames);
        }

        foreach (var grantRight in WindowsSecurityPolicyCatalog.RequiredGrantRightConstants)
        {
            Assert.Contains(grantRight, settingNames);
        }

        var denySettings = settings
            .Where(setting => setting.SettingName.StartsWith("SeDeny", StringComparison.Ordinal))
            .ToArray();
        Assert.All(denySettings, setting => Assert.False(string.IsNullOrWhiteSpace(setting.ConfiguredValue)));

        var backupSetting = Assert.Single(settings, setting => setting.SettingName == "SeBackupPrivilege");
        Assert.Equal("UserRight:SeBackupPrivilege", backupSetting.PolicyPath);
        Assert.Contains(@"BUILTIN\Administrators", backupSetting.ConfiguredValue.Split(','));
    }

    [Fact]
    public void AuditPolicyTemplate_Spells_The_Combined_Value_The_Way_AuditCsv_Does()
    {
        var world = Generate();
        var settings = SettingsFor(world, AuditTemplatePolicyName);

        string[] allowedValues =
        [
            "No Auditing",
            "Success",
            "Failure",
            "Success and Failure"
        ];

        Assert.All(settings, setting =>
        {
            Assert.Equal("AuditCsv", setting.Source);
            Assert.Contains(setting.ConfiguredValue, allowedValues);
            Assert.DoesNotContain(",", setting.ConfiguredValue, StringComparison.Ordinal);
        });

        foreach (var expectedValue in allowedValues)
        {
            Assert.Contains(settings, setting => setting.ConfiguredValue == expectedValue);
        }

        var combinedSettings = settings
            .Where(setting => setting.ConfiguredValue.Contains("Success", StringComparison.Ordinal)
                && setting.ConfiguredValue.Contains("Failure", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(combinedSettings);
        Assert.All(combinedSettings, setting => Assert.Equal("Success and Failure", setting.ConfiguredValue));
    }

    [Fact]
    public void SecurityTemplateBaseline_Emits_Sddl_With_Real_Ace_Structure()
    {
        var world = Generate();
        var aclSettings = SettingsFor(world, SecurityTemplatePolicyName)
            .Where(setting => setting.SettingCategory is "FileSecurity" or "RegistryKeys")
            .ToArray();

        Assert.Equal(5, aclSettings.Length);
        Assert.All(aclSettings, setting =>
        {
            var sddl = setting.ConfiguredValue;
            Assert.StartsWith("O:", sddl, StringComparison.Ordinal);
            Assert.Contains("G:", sddl, StringComparison.Ordinal);
            Assert.Contains("D:", sddl, StringComparison.Ordinal);

            var aces = Regex.Matches(sddl, @"\(([^)]*)\)")
                .Select(match => match.Groups[1].Value)
                .ToArray();
            Assert.True(aces.Length >= 4, $"Expected a populated DACL, found {aces.Length} ACEs in '{sddl}'.");
            Assert.All(aces, ace => Assert.Equal(6, ace.Split(';').Length));
            Assert.Contains(aces, ace => ace.StartsWith("D;", StringComparison.Ordinal));
            Assert.Contains(aces, ace => ace.StartsWith("A;", StringComparison.Ordinal));
        });

        var allAceFlags = aclSettings
            .SelectMany(setting => Regex.Matches(setting.ConfiguredValue, @"\(([^)]*)\)"))
            .Select(match => match.Groups[1].Value.Split(';')[1])
            .ToArray();
        Assert.Contains(allAceFlags, flags => flags.Contains("ID", StringComparison.Ordinal));
        Assert.Contains(allAceFlags, flags => !flags.Contains("ID", StringComparison.Ordinal));
        Assert.Contains(aclSettings, setting => setting.ConfiguredValue.Contains("D:AI(", StringComparison.Ordinal));
        Assert.Contains(aclSettings, setting => setting.ConfiguredValue.Contains("D:PAI(", StringComparison.Ordinal));
    }

    [Fact]
    public void CanonicalBaselines_Are_Linked_To_Workstation_And_Server_Containers()
    {
        var world = Generate();
        var company = Assert.Single(world.Companies);

        foreach (var policyName in new[] { SecurityTemplatePolicyName, AuditTemplatePolicyName })
        {
            var policy = Assert.Single(world.Policies, candidate => candidate.Name == policyName);
            Assert.Equal("GroupPolicyObject", policy.PolicyType);
            Assert.Equal("ActiveDirectory", policy.Platform);
            Assert.Equal("Source", policy.EnvironmentRole);

            var linkedContainerIds = world.PolicyTargetLinks
                .Where(link => link.PolicyId == policy.Id && link.TargetType == "Container")
                .Select(link => link.TargetId)
                .ToArray();
            var linkedContainerNames = world.Containers
                .Where(container => container.CompanyId == company.Id && linkedContainerIds.Contains(container.Id))
                .Select(container => container.Name)
                .ToArray();

            Assert.Contains("Workstations", linkedContainerNames);
            Assert.Contains("Servers", linkedContainerNames);
        }
    }

    [Fact]
    public void FriendlyNamedBaselines_Are_Unchanged_By_The_Canonical_Baselines()
    {
        var world = Generate();

        var userRightsSetting = Assert.Single(
            SettingsFor(world, "Windows User Rights Assignment Baseline"),
            setting => setting.SettingName == "BackUpFilesAndDirectories");
        Assert.Equal("Administrators", userRightsSetting.ConfiguredValue);
        Assert.Equal(
            @"Computer Configuration\Windows Settings\Security Settings\Local Policies\User Rights Assignment",
            userRightsSetting.PolicyPath);

        var auditSetting = Assert.Single(
            SettingsFor(world, "Windows Advanced Audit Baseline"),
            setting => setting.SettingName == "AuditCredentialValidation");
        Assert.Equal("Success,Failure", auditSetting.ConfiguredValue);
    }

    [Fact]
    public void CanonicalBaselines_Are_Deterministic_For_A_Given_Seed()
    {
        static string[] CanonicalRows(SyntheticEnterpriseWorld world)
        {
            var policyIds = world.Policies
                .Where(policy => policy.Name is SecurityTemplatePolicyName or AuditTemplatePolicyName)
                .Select(policy => policy.Id)
                .ToHashSet(StringComparer.Ordinal);
            return world.PolicySettings
                .Where(setting => policyIds.Contains(setting.PolicyId))
                .Select(setting => string.Join(
                    '|',
                    setting.PolicyId,
                    setting.SettingName,
                    setting.SettingCategory,
                    setting.PolicyPath,
                    setting.ConfiguredValue,
                    setting.Source,
                    setting.SourceReference))
                .ToArray();
        }

        Assert.Equal(CanonicalRows(Generate()), CanonicalRows(Generate()));
    }

    [Fact]
    public void Audit_Combined_Value_Spelling_Matches_The_Collection_Method_That_Produced_It()
    {
        var world = Generate();

        var backupRows = SettingsFor(world, AuditTemplatePolicyName);
        var reportRows = SettingsFor(world, DomainControllerAuditPolicyName);
        Assert.NotEmpty(backupRows);
        Assert.NotEmpty(reportRows);

        // A backup's audit.csv writes the words; a report export carries the numeric code a
        // reader renders with a comma. Emitting either spelling on the other's rows would put
        // a value and a provenance on one row that contradict each other.
        Assert.Contains(backupRows, setting =>
            setting.ConfiguredValue == WindowsSecurityPolicyCatalog.AuditValues.SuccessAndFailure);
        Assert.DoesNotContain(backupRows, setting =>
            setting.ConfiguredValue == WindowsSecurityPolicyCatalog.AuditValues.SuccessCommaFailure);

        Assert.Contains(reportRows, setting =>
            setting.ConfiguredValue == WindowsSecurityPolicyCatalog.AuditValues.SuccessCommaFailure);
        Assert.DoesNotContain(reportRows, setting =>
            setting.ConfiguredValue == WindowsSecurityPolicyCatalog.AuditValues.SuccessAndFailure);
    }

    [Fact]
    public void Audit_Rows_Record_The_Source_That_Matches_Their_Value_Spelling()
    {
        var world = Generate();

        Assert.All(SettingsFor(world, AuditTemplatePolicyName), setting =>
            Assert.Equal("AuditCsv", setting.Source));
        Assert.All(SettingsFor(world, DomainControllerAuditPolicyName), setting =>
            Assert.Equal("GPO", setting.Source));
    }

    [Fact]
    public void Both_Audit_Baselines_Use_The_Same_Canonical_Key_Space()
    {
        var world = Generate();

        // The two baselines describe different policy objects, so they carry different
        // subcategories -- but a subcategory they share must key identically, or the
        // collection route a row came from would change the key rather than only the value.
        var reportKeys = SettingsFor(world, DomainControllerAuditPolicyName)
            .ToDictionary(setting => setting.SettingName, setting => setting.PolicyPath);
        var backupKeys = SettingsFor(world, AuditTemplatePolicyName)
            .ToDictionary(setting => setting.SettingName, setting => setting.PolicyPath);

        var shared = reportKeys.Keys.Intersect(backupKeys.Keys).ToArray();
        Assert.NotEmpty(shared);
        Assert.All(shared, name => Assert.Equal(backupKeys[name], reportKeys[name]));
        Assert.All(reportKeys.Values, path => Assert.StartsWith("Audit:", path, StringComparison.Ordinal));
    }

    private static PolicySettingRecord[] SettingsFor(SyntheticEnterpriseWorld world, string policyName)
    {
        var policy = Assert.Single(world.Policies, candidate => candidate.Name == policyName);
        return world.PolicySettings
            .Where(setting => setting.PolicyId == policy.Id)
            .ToArray();
    }

    private static SyntheticEnterpriseWorld Generate()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        return generator.Generate(
            new GenerationContext
            {
                Seed = 907,
                Scenario = new ScenarioDefinition
                {
                    Name = "Windows Security Policy Baseline Test",
                    Companies = new()
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Windows Policy Baseline Co",
                            Industry = "Manufacturing",
                            EmployeeCount = 120,
                            BusinessUnitCount = 2,
                            DepartmentCountPerBusinessUnit = 2,
                            TeamCountPerDepartment = 2,
                            OfficeCount = 1,
                            ServiceAccountCount = 4,
                            IncludePrivilegedAccounts = true,
                            Countries = new() { "United States" }
                        }
                    }
                }
            },
            new CatalogSet()).World;
    }
}
