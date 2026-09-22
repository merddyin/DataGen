using Microsoft.Extensions.DependencyInjection;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Core.Abstractions;
using SyntheticEnterprise.Core.DependencyInjection;

namespace SyntheticEnterprise.Core.Tests;

/// <summary>
/// Covers the per-endpoint effective security configuration. Every assertion is about what
/// was emitted — which token sets contain which, which value spelling a collection tool
/// produces, and whether the endpoint linkage is intact — never about how a downstream
/// comparison might later rate any of it.
/// </summary>
public sealed class EffectiveSecurityConfigurationTests
{
    private const string SecurityTemplatePolicyName = "Windows Security Template Baseline";
    private const string AuditTemplatePolicyName = "Windows Advanced Audit Policy Template";
    private const string EndpointPolicyType = "LocalSecurityPolicy";
    private const int EndpointCount = 12;

    /// <summary>
    /// Every configuration profile a generated world must contain, in the order endpoints
    /// are assigned to them.
    /// </summary>
    private static readonly string[] ExpectedProfiles =
    [
        "Aligned",
        "ExpandedPrincipals",
        "ReducedPrincipals",
        "DivergentPrincipals",
        "ExpandedDenyPrincipals",
        "ReducedAuditCoverage",
        "ExpandedAuditCoverage",
        "DivergentAcl"
    ];

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
    public void Option_Is_Off_By_Default()
    {
        var world = Generate(endpointCount: 0);

        Assert.Equal(0, new InfrastructureProfile().EffectiveSecurityConfigurationEndpointCount);
        Assert.DoesNotContain(world.Policies, policy => policy.PolicyType == EndpointPolicyType);
        Assert.Contains(world.Policies, policy => policy.Name == SecurityTemplatePolicyName);
    }

    [Fact]
    public void Each_Endpoint_Policy_Is_Attached_To_A_Real_Windows_Endpoint()
    {
        var world = Generate();
        var policies = EndpointPolicies(world);

        Assert.Equal(EndpointCount, policies.Length);
        Assert.All(policies, policy =>
        {
            Assert.Equal("Windows", policy.Platform);
            Assert.Equal("EffectiveConfiguration", policy.Category);
            Assert.Equal("Source", policy.EnvironmentRole);
            Assert.False(string.IsNullOrWhiteSpace(policy.SourceEntityId));

            if (policy.SourceEntityType == "ManagedDevice")
            {
                var device = Assert.Single(world.Devices, candidate => candidate.Id == policy.SourceEntityId);
                Assert.Equal(policy.CompanyId, device.CompanyId);
                Assert.StartsWith("Windows", device.OperatingSystem, StringComparison.OrdinalIgnoreCase);
                Assert.Contains(device.Hostname, policy.Name, StringComparison.Ordinal);
            }
            else
            {
                Assert.Equal("ServerAsset", policy.SourceEntityType);
                var server = Assert.Single(world.Servers, candidate => candidate.Id == policy.SourceEntityId);
                Assert.Equal(policy.CompanyId, server.CompanyId);
                Assert.StartsWith("Windows", server.OperatingSystem, StringComparison.OrdinalIgnoreCase);
                Assert.Contains(server.Hostname, policy.Name, StringComparison.Ordinal);
            }
        });

        // Both cohorts report, and no endpoint reports twice.
        Assert.Contains(policies, policy => policy.SourceEntityType == "ManagedDevice");
        Assert.Contains(policies, policy => policy.SourceEntityType == "ServerAsset");
        Assert.Equal(
            policies.Length,
            policies.Select(policy => policy.SourceEntityId).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Every_Configuration_Profile_Appears_In_A_Generated_World()
    {
        var world = Generate();

        var observed = EndpointPolicies(world)
            .Select(policy => ProfileOf(world, policy))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(profile => profile, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ExpectedProfiles.OrderBy(profile => profile, StringComparer.Ordinal).ToArray(), observed);
    }

    [Fact]
    public void Endpoint_Rows_Carry_The_Template_Keys_And_Their_Collection_Sources()
    {
        var world = Generate();
        var template = TemplateValues(world);

        foreach (var policy in EndpointPolicies(world))
        {
            var settings = SettingsOf(world, policy.Id);
            Assert.Equal(
                template.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray(),
                settings.Select(setting => setting.PolicyPath).OrderBy(key => key, StringComparer.Ordinal).ToArray());

            Assert.All(settings, setting =>
            {
                Assert.Equal(setting.SettingName, StripPrefix(setting.PolicyPath));
                Assert.Equal(
                    setting.SettingCategory == "AuditPolicy" ? "AuditCsv" : "SecTemplate",
                    setting.Source);
                Assert.Equal("RedDot", setting.Behavior);
                Assert.Equal("Source", setting.EnvironmentRole);
            });
        }
    }

    [Fact]
    public void Expanded_Principals_Endpoint_Holds_A_Strict_Superset_Of_The_Template_Grant()
    {
        var world = Generate();
        var template = TemplateValues(world);
        var settings = SettingsForProfile(world, "ExpandedPrincipals");

        var expanded = settings
            .Where(setting => setting.SettingCategory == "UserRightsAssignment"
                && !setting.SettingName.StartsWith("SeDeny", StringComparison.Ordinal)
                && !IsSameTokenSet(template[setting.PolicyPath], setting.ConfiguredValue))
            .ToArray();
        Assert.NotEmpty(expanded);
        Assert.All(expanded, setting =>
        {
            var templateTokens = Tokens(template[setting.PolicyPath]);
            var endpointTokens = Tokens(setting.ConfiguredValue);
            Assert.ProperSubset(endpointTokens, templateTokens);
        });

        // A right the template grants to nobody is the sharpest form of the same fact: the
        // endpoint holds a privilege the template granted to no one at all.
        var grantedToNobody = expanded.Single(setting => Tokens(template[setting.PolicyPath]).Count == 0);
        Assert.Equal("UserRight:SeTcbPrivilege", grantedToNobody.PolicyPath);
        Assert.NotEmpty(Tokens(grantedToNobody.ConfiguredValue));

        // Nothing outside the rights family moved.
        Assert.All(
            settings.Where(setting => setting.SettingCategory != "UserRightsAssignment"),
            setting => Assert.Equal(template[setting.PolicyPath], setting.ConfiguredValue));
    }

    [Fact]
    public void Reduced_Principals_Endpoint_Holds_A_Strict_Non_Empty_Subset()
    {
        var world = Generate();
        var template = TemplateValues(world);
        var settings = SettingsForProfile(world, "ReducedPrincipals");

        var reduced = settings
            .Where(setting => !IsSameTokenSet(template[setting.PolicyPath], setting.ConfiguredValue))
            .ToArray();
        var setting = Assert.Single(reduced);
        Assert.Equal("UserRightsAssignment", setting.SettingCategory);
        Assert.StartsWith("UserRight:Se", setting.PolicyPath, StringComparison.Ordinal);
        Assert.DoesNotContain("SeDeny", setting.PolicyPath, StringComparison.Ordinal);

        var templateTokens = Tokens(template[setting.PolicyPath]);
        var endpointTokens = Tokens(setting.ConfiguredValue);
        Assert.ProperSubset(templateTokens, endpointTokens);
        Assert.NotEmpty(endpointTokens);
    }

    [Fact]
    public void Divergent_Principals_Endpoint_Is_Neither_Superset_Nor_Subset()
    {
        var world = Generate();
        var template = TemplateValues(world);
        var settings = SettingsForProfile(world, "DivergentPrincipals");

        var setting = Assert.Single(
            settings,
            candidate => !IsSameTokenSet(template[candidate.PolicyPath], candidate.ConfiguredValue));
        Assert.Equal("UserRightsAssignment", setting.SettingCategory);

        var templateTokens = Tokens(template[setting.PolicyPath]);
        var endpointTokens = Tokens(setting.ConfiguredValue);
        Assert.False(endpointTokens.IsSupersetOf(templateTokens));
        Assert.False(endpointTokens.IsSubsetOf(templateTokens));
        Assert.True(endpointTokens.Overlaps(templateTokens));
    }

    [Fact]
    public void Expanded_Deny_Principals_Endpoint_Holds_A_Strict_Superset_Of_The_Template_Deny()
    {
        var world = Generate();
        var template = TemplateValues(world);
        var settings = SettingsForProfile(world, "ExpandedDenyPrincipals");

        var setting = Assert.Single(
            settings,
            candidate => !IsSameTokenSet(template[candidate.PolicyPath], candidate.ConfiguredValue));
        Assert.StartsWith("UserRight:SeDeny", setting.PolicyPath, StringComparison.Ordinal);
        Assert.ProperSubset(Tokens(setting.ConfiguredValue), Tokens(template[setting.PolicyPath]));
    }

    [Fact]
    public void Audit_Coverage_Profiles_Use_The_Value_Spelling_Auditpol_And_Audit_Csv_Emit()
    {
        var world = Generate();
        var template = TemplateValues(world);

        var reduced = Assert.Single(
            SettingsForProfile(world, "ReducedAuditCoverage"),
            setting => !IsSameTokenSet(template[setting.PolicyPath], setting.ConfiguredValue));
        Assert.StartsWith("Audit:Audit ", reduced.PolicyPath, StringComparison.Ordinal);
        Assert.Equal("Success and Failure", template[reduced.PolicyPath]);
        Assert.Equal("Success", reduced.ConfiguredValue);

        var expanded = Assert.Single(
            SettingsForProfile(world, "ExpandedAuditCoverage"),
            setting => !IsSameTokenSet(template[setting.PolicyPath], setting.ConfiguredValue));
        Assert.StartsWith("Audit:Audit ", expanded.PolicyPath, StringComparison.Ordinal);
        Assert.Equal("Success", template[expanded.PolicyPath]);
        Assert.Equal("Success and Failure", expanded.ConfiguredValue);

        // These rows model auditpol and a Group Policy audit backup, both of which spell
        // the combined value with "and". The comma form belongs to a gpreport.xml export
        // and must not leak into a row whose source is AuditCsv.
        var auditRows = EndpointPolicies(world)
            .SelectMany(policy => SettingsOf(world, policy.Id))
            .Where(setting => setting.SettingCategory == "AuditPolicy")
            .ToArray();
        Assert.NotEmpty(auditRows);
        Assert.All(auditRows, setting =>
        {
            Assert.Equal("AuditCsv", setting.Source);
            Assert.Contains(
                setting.ConfiguredValue,
                new[] { "No Auditing", "Success", "Failure", "Success and Failure" },
                StringComparer.Ordinal);
        });
    }

    [Fact]
    public void Divergent_Acl_Endpoint_Differs_On_One_Descriptor_While_An_Aligned_Endpoint_Matches()
    {
        var world = Generate();
        var template = TemplateValues(world);

        var descriptorRows = SettingsForProfile(world, "DivergentAcl")
            .Where(setting => setting.SettingCategory is "FileSecurity" or "RegistryKeys")
            .ToArray();
        Assert.NotEmpty(descriptorRows);

        var differing = descriptorRows
            .Where(setting => !string.Equals(template[setting.PolicyPath], setting.ConfiguredValue, StringComparison.Ordinal))
            .ToArray();
        var divergent = Assert.Single(differing);
        Assert.StartsWith("FileACL:", divergent.PolicyPath, StringComparison.Ordinal);
        Assert.StartsWith("O:", divergent.ConfiguredValue, StringComparison.Ordinal);
        Assert.Contains("D;", divergent.ConfiguredValue, StringComparison.Ordinal);

        // Every other descriptor on the same machine still matches, and an aligned machine
        // matches on all of them, so an identical-value pair is always present.
        Assert.All(
            descriptorRows.Except(differing),
            setting => Assert.Equal(template[setting.PolicyPath], setting.ConfiguredValue));
        var alignedDescriptors = SettingsForProfile(world, "Aligned")
            .Where(setting => setting.SettingCategory is "FileSecurity" or "RegistryKeys")
            .ToArray();
        Assert.NotEmpty(alignedDescriptors);
        Assert.All(alignedDescriptors, setting => Assert.Equal(template[setting.PolicyPath], setting.ConfiguredValue));
    }

    [Fact]
    public void Aligned_Endpoint_Reports_The_Template_Unchanged()
    {
        var world = Generate();
        var template = TemplateValues(world);

        Assert.All(
            SettingsForProfile(world, "Aligned"),
            setting => Assert.Equal(template[setting.PolicyPath], setting.ConfiguredValue));
    }

    [Fact]
    public void Emitted_Principals_Use_The_Collector_Resolver_Forms()
    {
        var world = Generate();
        var netBiosPrefixes = world.Companies
            .Select(company => NetBiosName(company.PrimaryDomain))
            .Where(prefix => prefix.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        // A domain-qualified principal names either a generated group or a generated
        // account: the template grants the service and batch logon rights to the service
        // accounts that actually run services and scheduled tasks.
        var generatedObjectNames = world.Groups
            .Select(group => group.Name)
            .Concat(world.Accounts.Select(account => account.SamAccountName))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.Ordinal);

        var principals = EndpointPolicies(world)
            .SelectMany(policy => SettingsOf(world, policy.Id))
            .Where(setting => setting.SettingCategory == "UserRightsAssignment")
            .SelectMany(setting => Tokens(setting.ConfiguredValue))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(principals);
        Assert.All(principals, principal =>
        {
            Assert.DoesNotContain(principal, UncuratedWindowsPrincipalForms, StringComparer.Ordinal);
            Assert.False(principal.StartsWith("S-1-", StringComparison.Ordinal), $"bare SID emitted: {principal}");

            var separator = principal.IndexOf('\\');
            if (separator <= 0)
            {
                return;
            }

            var authority = principal[..separator];
            if (authority is "BUILTIN" or "NT AUTHORITY")
            {
                return;
            }

            Assert.Contains(authority, netBiosPrefixes);
            Assert.Contains(principal[(separator + 1)..], generatedObjectNames);
        });
    }

    [Fact]
    public void Generation_Is_Byte_Identical_For_The_Same_Seed_And_Generation_Time()
    {
        var generatedAt = DateTimeOffset.Parse("2026-04-01T09:00:00Z");
        var first = Describe(Generate(generatedAt: generatedAt));
        var second = Describe(Generate(generatedAt: generatedAt));

        Assert.Equal(first, second);
    }

    [Fact]
    public void Fewer_Endpoints_Than_Requested_Keeps_The_Real_Population()
    {
        var world = Generate(endpointCount: 250);
        var policies = EndpointPolicies(world);
        var windowsEndpointCount =
            world.Devices.Count(device => device.OperatingSystem.StartsWith("Windows", StringComparison.OrdinalIgnoreCase))
            + world.Servers.Count(server => server.OperatingSystem.StartsWith("Windows", StringComparison.OrdinalIgnoreCase));

        Assert.True(policies.Length < 250);
        Assert.Equal(Math.Min(250, windowsEndpointCount), policies.Length);
        Assert.All(policies, policy => Assert.False(string.IsNullOrWhiteSpace(policy.SourceEntityId)));
    }

    /// <summary>
    /// The NetBIOS form of a DNS domain name: first label, alphanumerics only, upper-cased
    /// and truncated to the 15-character NetBIOS limit.
    /// </summary>
    private static string NetBiosName(string dnsDomainName)
    {
        var firstLabel = dnsDomainName.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        var normalized = new string(firstLabel.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return normalized.Length > 15 ? normalized[..15] : normalized;
    }

    private static PolicyRecord[] EndpointPolicies(SyntheticEnterpriseWorld world)
        => world.Policies
            .Where(policy => policy.PolicyType == EndpointPolicyType)
            .ToArray();

    private static PolicySettingRecord[] SettingsOf(SyntheticEnterpriseWorld world, string policyId)
        => world.PolicySettings
            .Where(setting => setting.PolicyId == policyId)
            .ToArray();

    private static string ProfileOf(SyntheticEnterpriseWorld world, PolicyRecord policy)
        => SettingsOf(world, policy.Id)
            .Select(setting => setting.SourceReference ?? "")
            .Distinct(StringComparer.Ordinal)
            .Single();

    private static PolicySettingRecord[] SettingsForProfile(SyntheticEnterpriseWorld world, string profile)
    {
        var policy = EndpointPolicies(world).First(candidate => ProfileOf(world, candidate) == profile);
        return SettingsOf(world, policy.Id);
    }

    /// <summary>Canonical key to configured value across both template policy objects.</summary>
    private static Dictionary<string, string> TemplateValues(SyntheticEnterpriseWorld world)
    {
        var policyIds = world.Policies
            .Where(policy => policy.Name is SecurityTemplatePolicyName or AuditTemplatePolicyName)
            .Select(policy => policy.Id)
            .ToHashSet(StringComparer.Ordinal);
        return world.PolicySettings
            .Where(setting => policyIds.Contains(setting.PolicyId))
            .ToDictionary(setting => setting.PolicyPath, setting => setting.ConfiguredValue, StringComparer.Ordinal);
    }

    private static HashSet<string> Tokens(string value)
        => value
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool IsSameTokenSet(string left, string right)
        => Tokens(left).SetEquals(Tokens(right));

    private static string StripPrefix(string policyPath)
    {
        var separator = policyPath.IndexOf(':');
        return separator < 0 ? policyPath : policyPath[(separator + 1)..];
    }

    private static string[] Describe(SyntheticEnterpriseWorld world)
        => EndpointPolicies(world)
            .SelectMany(policy => SettingsOf(world, policy.Id)
                .Select(setting => string.Join(
                    '|',
                    policy.Name,
                    policy.PolicyGuid,
                    policy.SourceEntityType,
                    policy.SourceEntityId,
                    setting.PolicyPath,
                    setting.ConfiguredValue,
                    setting.Source,
                    setting.SourceReference)))
            .ToArray();

    private static SyntheticEnterpriseWorld Generate(
        int endpointCount = EndpointCount,
        DateTimeOffset? generatedAt = null)
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var context = new GenerationContext
        {
            Seed = 907,
            Scenario = new ScenarioDefinition
            {
                Name = "Effective Security Configuration Test",
                Infrastructure = new InfrastructureProfile
                {
                    EffectiveSecurityConfigurationEndpointCount = endpointCount
                },
                Companies = new()
                {
                    new ScenarioCompanyDefinition
                    {
                        Name = "Effective Config Co",
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
        };
        if (generatedAt.HasValue)
        {
            context = context with { GeneratedAt = generatedAt.Value };
        }

        return generator.Generate(context, new CatalogSet()).World;
    }
}
