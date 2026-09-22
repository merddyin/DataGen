namespace SyntheticEnterprise.Core.Generation.Policy;

using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Core.Abstractions;
using SyntheticEnterprise.Core.Generation;
using SyntheticEnterprise.Core.Generation.Identity;

/// <summary>
/// Emits the effective Windows security configuration a single machine carries, as
/// <c>secedit /export</c> and <c>auditpol /get</c> report it on that machine: one
/// <c>LocalSecurityPolicy</c> object per endpoint, attached to the endpoint through
/// <see cref="PolicyRecord.SourceEntityType"/> and <see cref="PolicyRecord.SourceEntityId"/>,
/// carrying the same canonical <c>UserRight:</c>, <c>Audit:</c>, <c>FileACL:</c> and
/// <c>RegistryACL:</c> keys the company's security template and audit template carry.
/// </summary>
/// <remarks>
/// <para>
/// Every value is built from the template's own value for the same key, so the relationship
/// between a machine and the template assigned to it is exact rather than incidental: a
/// superset is a superset of that template's tokens, not of a set that merely resembles it.
/// The relationship is named in <see cref="PolicySettingRecord.SourceReference"/> using
/// <see cref="WindowsSecurityPolicyCatalog.ConfigurationProfiles"/>, which describes the
/// shape of the emitted data and nothing about how any consumer might rate the machine.
/// </para>
/// <para>
/// Real estates contain machines that match their template, machines that hold privileges
/// the template never granted, and machines auditing less than they were told to. Those are
/// the facts this generator emits. It does not model a whole estate: it is an opt-in,
/// bounded sample governed by
/// <see cref="InfrastructureProfile.EffectiveSecurityConfigurationEndpointCount"/>, and when
/// a company holds fewer endpoints than requested the real population is kept and fewer
/// profiles are covered rather than endpoints being invented.
/// </para>
/// </remarks>
internal static class EffectiveSecurityConfigurationGenerator
{
    private const string DeviceSourceEntityType = "ManagedDevice";
    private const string ServerSourceEntityType = "ServerAsset";
    private const string SettingValueType = "String";
    private const string ExpansionPrincipalHashDomain = "EffectiveSecurityConfiguration.ExpansionPrincipal";

    /// <summary>
    /// Principals used to widen a rights assignment when a company generated no group that
    /// is absent from the assignment already. They are well-known identities, which the
    /// token vocabulary permits without a corresponding generated object.
    /// </summary>
    private static readonly string[] WellKnownExpansionPrincipals =
    [
        WindowsSecurityPolicyCatalog.Tier1Principals.PowerUsers,
        WindowsSecurityPolicyCatalog.Tier1Principals.BackupOperators,
        WindowsSecurityPolicyCatalog.Tier1Principals.RemoteDesktopUsers,
        WindowsSecurityPolicyCatalog.Tier1Principals.PerformanceLogUsers,
        WindowsSecurityPolicyCatalog.Tier1Principals.Users
    ];

    /// <summary>
    /// Principals used to widen a <c>SeDeny*</c> assignment. Real security templates deny
    /// logon to exactly these identities, and all four sit outside the collector's curated
    /// table, so they carry the live Windows translation.
    /// </summary>
    private static readonly string[] DenyExpansionPrincipals =
    [
        WindowsSecurityPolicyCatalog.Tier2Principals.LocalAccount,
        WindowsSecurityPolicyCatalog.Tier2Principals.LocalAccountAndMemberOfAdministratorsGroup,
        WindowsSecurityPolicyCatalog.Tier2Principals.Service,
        WindowsSecurityPolicyCatalog.Tier1Principals.Guests
    ];

    /// <summary>Descriptors a diverging object ACL is drawn from, in template order.</summary>
    private static readonly string[] AlternateSecurityDescriptors =
    [
        WindowsSecurityPolicyCatalog.SecurityDescriptors.AdministratorOwnedDirectory,
        WindowsSecurityPolicyCatalog.SecurityDescriptors.InheritedSystemDirectory,
        WindowsSecurityPolicyCatalog.SecurityDescriptors.ProtectedSystemDirectory
    ];

    private static readonly char[] PrincipalSeparators = [',', ';'];

    public static void Apply(
        SyntheticEnterpriseWorld world,
        Company company,
        IIdFactory idFactory,
        InfrastructureProfile configuration)
    {
        var requestedCount = Math.Max(0, configuration.EffectiveSecurityConfigurationEndpointCount);
        if (requestedCount == 0)
        {
            return;
        }

        var template = ReadTemplate(world, company);
        if (template.Rows.Count == 0)
        {
            return;
        }

        var endpoints = SelectEndpoints(world, company, requestedCount);
        if (endpoints.Length == 0)
        {
            return;
        }

        var netBiosName = WindowsSecurityPolicyCatalog.BuildNetBiosName(BasicIdentityGenerator.BuildRootDomain(company));
        var generatedPrincipals = SelectGeneratedGroupPrincipals(world, company.Id, netBiosName);
        var targets = SelectDriftTargets(template);
        var deduplicationIndex = new PolicySettingDeduplicationIndex(world);
        var profileNames = WindowsSecurityPolicyCatalog.ConfigurationProfileNames;

        for (var index = 0; index < endpoints.Length; index++)
        {
            var endpoint = endpoints[index];
            var profile = profileNames[index % profileNames.Count];
            var policy = PolicyEmitter.EnsurePolicy(
                world,
                idFactory,
                company.Id,
                $"{endpoint.Hostname} Local Security Policy",
                WindowsSecurityPolicyCatalog.LocalSecurityPolicyPolicyType,
                WindowsSecurityPolicyCatalog.WindowsPlatform,
                WindowsSecurityPolicyCatalog.EffectiveConfigurationPolicyCategory,
                $"Effective local security configuration reported by {endpoint.Hostname}. Configuration profile relative to {WindowsSecurityPolicyCatalog.SecurityTemplateBaselinePolicyName}: {profile}.",
                identityStoreId: null,
                cloudTenantId: null,
                sourceEntityType: endpoint.SourceEntityType,
                sourceEntityId: endpoint.Id);

            foreach (var row in template.Rows)
            {
                PolicyEmitter.AddResolvedPolicySetting(
                    world,
                    idFactory,
                    company.Id,
                    policy.Id,
                    row.SettingName,
                    row.SettingCategory,
                    SettingValueType,
                    ResolveEndpointValue(row, profile, targets, endpoint, generatedPrincipals),
                    row.PolicyPath,
                    registryPath: null,
                    isLegacy: false,
                    isConflicting: false,
                    sourceReference: profile,
                    environmentRole: "Source",
                    deduplicationIndex: deduplicationIndex);
            }
        }
    }

    /// <summary>
    /// Produces the value the endpoint reports for one template key: the template's own
    /// value unless this key is the one the endpoint's configuration profile diverges on.
    /// </summary>
    private static string ResolveEndpointValue(
        TemplateRow row,
        string profile,
        DriftTargets targets,
        EndpointSelection endpoint,
        IReadOnlyList<string> generatedPrincipals)
    {
        switch (profile)
        {
            case WindowsSecurityPolicyCatalog.ConfigurationProfiles.ExpandedPrincipals
                when Matches(row, targets.ExpandEmptyGrantRight) || Matches(row, targets.ExpandPopulatedGrantRight):
            {
                var tokens = SplitPrincipals(row.Value);
                var addition = SelectAbsentPrincipal(tokens, generatedPrincipals, WellKnownExpansionPrincipals, endpoint.Id, row.PolicyPath);
                return addition.Length == 0 ? row.Value : JoinPrincipals([.. tokens, addition]);
            }

            case WindowsSecurityPolicyCatalog.ConfigurationProfiles.ReducedPrincipals
                when Matches(row, targets.ReduceGrantRight):
            {
                var tokens = SplitPrincipals(row.Value);
                return tokens.Length < 2 ? row.Value : JoinPrincipals(tokens[..^1]);
            }

            case WindowsSecurityPolicyCatalog.ConfigurationProfiles.DivergentPrincipals
                when Matches(row, targets.DivergeGrantRight):
            {
                var tokens = SplitPrincipals(row.Value);
                if (tokens.Length < 2)
                {
                    return row.Value;
                }

                var retained = tokens[..^1];
                var addition = SelectAbsentPrincipal(tokens, generatedPrincipals, WellKnownExpansionPrincipals, endpoint.Id, row.PolicyPath);
                return addition.Length == 0 ? row.Value : JoinPrincipals([.. retained, addition]);
            }

            case WindowsSecurityPolicyCatalog.ConfigurationProfiles.ExpandedDenyPrincipals
                when Matches(row, targets.ExpandDenyRight):
            {
                var tokens = SplitPrincipals(row.Value);
                var addition = SelectAbsentPrincipal(tokens, DenyExpansionPrincipals, generatedPrincipals, endpoint.Id, row.PolicyPath);
                return addition.Length == 0 ? row.Value : JoinPrincipals([.. tokens, addition]);
            }

            case WindowsSecurityPolicyCatalog.ConfigurationProfiles.ReducedAuditCoverage
                when Matches(row, targets.ReduceAuditSubcategory):
                return WindowsSecurityPolicyCatalog.AuditValues.Success;

            case WindowsSecurityPolicyCatalog.ConfigurationProfiles.ExpandedAuditCoverage
                when Matches(row, targets.ExpandAuditSubcategory):
                return WindowsSecurityPolicyCatalog.AuditValues.SuccessAndFailure;

            case WindowsSecurityPolicyCatalog.ConfigurationProfiles.DivergentAcl
                when Matches(row, targets.DivergeSecurityDescriptor):
                return AlternateSecurityDescriptors.FirstOrDefault(descriptor =>
                    !string.Equals(descriptor, row.Value, StringComparison.Ordinal)) ?? row.Value;

            default:
                return row.Value;
        }
    }

    private static bool Matches(TemplateRow row, string? targetPolicyPath)
        => targetPolicyPath is not null
            && string.Equals(row.PolicyPath, targetPolicyPath, StringComparison.Ordinal);

    /// <summary>
    /// Chooses which template key each configuration profile diverges on, entirely from the
    /// template's own content, so a template whose rights are assigned differently still
    /// yields the stated set relationships.
    /// </summary>
    private static DriftTargets SelectDriftTargets(SecurityTemplate template)
    {
        var grantRights = template.Rows
            .Where(row => row.SettingCategory == WindowsSecurityPolicyCatalog.UserRightsAssignmentCategory
                && !row.SettingName.StartsWith("SeDeny", StringComparison.Ordinal))
            .ToArray();
        var denyRights = template.Rows
            .Where(row => row.SettingCategory == WindowsSecurityPolicyCatalog.UserRightsAssignmentCategory
                && row.SettingName.StartsWith("SeDeny", StringComparison.Ordinal))
            .ToArray();
        var auditRows = template.Rows
            .Where(row => row.SettingCategory == WindowsSecurityPolicyCatalog.AuditPolicyCategory)
            .ToArray();
        var descriptorRows = template.Rows
            .Where(row => row.SettingCategory == WindowsSecurityPolicyCatalog.FileSecurityCategory
                || row.SettingCategory == WindowsSecurityPolicyCatalog.RegistryKeysCategory)
            .ToArray();

        // A right the template grants to nobody gives the sharpest excess-privilege
        // contrast: an endpoint holding it holds a privilege the template granted to no one.
        var expandEmpty = grantRights.FirstOrDefault(row =>
                row.SettingName == WindowsSecurityPolicyCatalog.GrantRights.Tcb && SplitPrincipals(row.Value).Length == 0)
            ?? grantRights.FirstOrDefault(row => SplitPrincipals(row.Value).Length == 0);
        var expandPopulated = grantRights.FirstOrDefault(row => SplitPrincipals(row.Value).Length > 0);
        var reduce = grantRights.FirstOrDefault(row => SplitPrincipals(row.Value).Length >= 2);
        var diverge = grantRights.FirstOrDefault(row =>
                SplitPrincipals(row.Value).Length >= 2 && row.PolicyPath != reduce?.PolicyPath)
            ?? reduce;

        return new DriftTargets
        {
            ExpandEmptyGrantRight = expandEmpty?.PolicyPath,
            ExpandPopulatedGrantRight = expandPopulated?.PolicyPath,
            ReduceGrantRight = reduce?.PolicyPath,
            DivergeGrantRight = diverge?.PolicyPath,
            ExpandDenyRight = denyRights.FirstOrDefault()?.PolicyPath,
            ReduceAuditSubcategory = auditRows
                .FirstOrDefault(row => row.Value == WindowsSecurityPolicyCatalog.AuditValues.SuccessAndFailure)?.PolicyPath,
            ExpandAuditSubcategory = auditRows
                .FirstOrDefault(row => row.Value == WindowsSecurityPolicyCatalog.AuditValues.Success)?.PolicyPath,
            DivergeSecurityDescriptor = descriptorRows.FirstOrDefault()?.PolicyPath
        };
    }

    /// <summary>
    /// Picks a principal absent from <paramref name="existingTokens"/>, preferring the first
    /// candidate list and falling back to the second. The starting position is derived from
    /// the endpoint and the key, so two endpoints widening the same right pick different
    /// principals while either one picks the same principal on every run.
    /// </summary>
    private static string SelectAbsentPrincipal(
        IReadOnlyCollection<string> existingTokens,
        IReadOnlyList<string> preferredCandidates,
        IReadOnlyList<string> fallbackCandidates,
        string endpointId,
        string policyPath)
    {
        var present = new HashSet<string>(existingTokens, StringComparer.OrdinalIgnoreCase);
        foreach (var candidates in new[] { preferredCandidates, fallbackCandidates })
        {
            if (candidates.Count == 0)
            {
                continue;
            }

            var start = StableHash.GetIndex(ExpansionPrincipalHashDomain, candidates.Count, endpointId, policyPath);
            for (var offset = 0; offset < candidates.Count; offset++)
            {
                var candidate = candidates[(start + offset) % candidates.Count];
                if (!string.IsNullOrWhiteSpace(candidate) && !present.Contains(candidate))
                {
                    return candidate;
                }
            }
        }

        return "";
    }

    /// <summary>
    /// Renders the company's generated groups as the domain-qualified principals a
    /// collection reports, so a widened assignment names an object the identity layer
    /// actually created rather than an invented name.
    /// </summary>
    /// <remarks>
    /// Only security groups are eligible, because a privilege-rights assignment names a
    /// security principal. Administrative-tier groups are preferred where the company has
    /// any: a machine holding a privilege its template never granted has almost always
    /// acquired it by an administrative or operator group being added locally, and naming a
    /// resource-access group there would be a shape real collections do not produce.
    /// </remarks>
    private static string[] SelectGeneratedGroupPrincipals(
        SyntheticEnterpriseWorld world,
        string companyId,
        string netBiosName)
    {
        if (string.IsNullOrWhiteSpace(netBiosName))
        {
            return [];
        }

        var securityGroups = world.Groups
            .Where(group => group.CompanyId == companyId
                && !string.IsNullOrWhiteSpace(group.Name)
                && string.Equals(group.GroupType, "Security", StringComparison.OrdinalIgnoreCase)
                && !group.MailEnabled)
            .ToArray();
        var administrativeGroups = securityGroups
            .Where(group => !string.IsNullOrWhiteSpace(group.AdministrativeTier))
            .ToArray();

        return (administrativeGroups.Length > 0 ? administrativeGroups : securityGroups)
            .Select(group => group.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => WindowsSecurityPolicyCatalog.QualifyDomainPrincipal(netBiosName, name))
            .Where(principal => principal.Length > 0)
            .ToArray();
    }

    /// <summary>
    /// Selects the endpoints that report an effective configuration, keeping servers and
    /// workstations both represented in the same proportion the management-observation
    /// sample uses. Only Windows endpoints are selected: a local security policy is a
    /// Windows object, and a macOS workstation has none to report.
    /// </summary>
    private static EndpointSelection[] SelectEndpoints(
        SyntheticEnterpriseWorld world,
        Company company,
        int requestedCount)
    {
        var devices = world.Devices
            .Where(device => device.CompanyId == company.Id
                && device.OperatingSystem.StartsWith("Windows", StringComparison.OrdinalIgnoreCase))
            .OrderBy(device => device.Id, StringComparer.Ordinal)
            .Select(device => new EndpointSelection(device.Id, device.Hostname, DeviceSourceEntityType))
            .ToArray();
        var servers = world.Servers
            .Where(server => server.CompanyId == company.Id
                && server.OperatingSystem.StartsWith("Windows", StringComparison.OrdinalIgnoreCase))
            .OrderBy(server => server.Id, StringComparer.Ordinal)
            .Select(server => new EndpointSelection(server.Id, server.Hostname, ServerSourceEntityType))
            .ToArray();

        var serverTarget = Math.Min(servers.Length, Math.Max(1, requestedCount / 3));
        var deviceTarget = Math.Min(devices.Length, requestedCount - serverTarget);
        serverTarget = Math.Min(servers.Length, requestedCount - deviceTarget);

        return [.. devices.Take(deviceTarget), .. servers.Take(serverTarget)];
    }

    /// <summary>
    /// Reads the company's security template and audit template back out of the world, in
    /// the order they were emitted, so every endpoint value is derived from the template
    /// the endpoint is compared against rather than from a second copy of the vocabulary.
    /// </summary>
    private static SecurityTemplate ReadTemplate(SyntheticEnterpriseWorld world, Company company)
    {
        var policyIds = world.Policies
            .Where(policy => policy.CompanyId == company.Id
                && string.Equals(policy.PolicyType, WindowsSecurityPolicyCatalog.GroupPolicyObjectPolicyType, StringComparison.OrdinalIgnoreCase)
                && (string.Equals(policy.Name, WindowsSecurityPolicyCatalog.SecurityTemplateBaselinePolicyName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(policy.Name, WindowsSecurityPolicyCatalog.AdvancedAuditPolicyTemplatePolicyName, StringComparison.OrdinalIgnoreCase)))
            .Select(policy => policy.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (policyIds.Count == 0)
        {
            return new SecurityTemplate([]);
        }

        var rows = world.PolicySettings
            .Where(setting => setting.CompanyId == company.Id
                && policyIds.Contains(setting.PolicyId)
                && setting.PolicyPath.Length > 0)
            .Select(setting => new TemplateRow(
                setting.SettingName,
                setting.SettingCategory,
                setting.PolicyPath,
                setting.ConfiguredValue))
            .ToArray();

        return new SecurityTemplate(rows);
    }

    private static string[] SplitPrincipals(string value)
        => value.Split(PrincipalSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string JoinPrincipals(IEnumerable<string> principals)
        => string.Join(WindowsSecurityPolicyCatalog.PrincipalDelimiter, principals);

    private sealed record SecurityTemplate(IReadOnlyList<TemplateRow> Rows);

    private sealed record TemplateRow(string SettingName, string SettingCategory, string PolicyPath, string Value);

    private sealed record EndpointSelection(string Id, string Hostname, string SourceEntityType);

    private sealed record DriftTargets
    {
        internal string? ExpandEmptyGrantRight { get; init; }
        internal string? ExpandPopulatedGrantRight { get; init; }
        internal string? ReduceGrantRight { get; init; }
        internal string? DivergeGrantRight { get; init; }
        internal string? ExpandDenyRight { get; init; }
        internal string? ReduceAuditSubcategory { get; init; }
        internal string? ExpandAuditSubcategory { get; init; }
        internal string? DivergeSecurityDescriptor { get; init; }
    }
}
