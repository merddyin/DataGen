namespace SyntheticEnterprise.Core.Generation.Policy;

using System.Security.Cryptography;
using System.Text;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Core.Abstractions;

/// <summary>
/// The single implementation of how a <see cref="PolicyRecord"/> and a
/// <see cref="PolicySettingRecord"/> are shaped, deduplicated and given their derived
/// <c>Source</c> and <c>Behavior</c> values.
/// </summary>
/// <remarks>
/// <para>
/// This logic began inside <c>BasicIdentityGenerator</c>, which is still its only caller
/// for directory-authored policy objects. It was lifted here when a second generator —
/// <see cref="EffectiveSecurityConfigurationGenerator"/>, which runs in the Infrastructure
/// layer — needed the same records. Copying it would have let the two sides derive
/// <c>Source</c> differently or guard duplicates differently, which is exactly the class
/// of silent divergence the canonical policy families exist to expose, so the identity
/// generator now delegates here rather than keeping a private copy.
/// </para>
/// <para>
/// Resolving a setting's <c>PolicyPath</c> from the policy object's shape stayed behind in
/// <c>BasicIdentityGenerator</c>: it is a directory-authoring concern with a large
/// per-policy-family decision tree, and a caller that already knows the canonical key
/// simply passes it in.
/// </para>
/// </remarks>
internal static class PolicyEmitter
{
    /// <summary>
    /// Returns the company's existing policy object of the given name, type, platform and
    /// environment role, creating it when absent.
    /// </summary>
    internal static PolicyRecord EnsurePolicy(
        SyntheticEnterpriseWorld world,
        IIdFactory idFactory,
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
        string environmentRole = "Source",
        string status = "Enabled")
    {
        var existing = world.Policies.FirstOrDefault(policy =>
            policy.CompanyId == companyId
            && string.Equals(policy.Name, name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(policy.PolicyType, policyType, StringComparison.OrdinalIgnoreCase)
            && string.Equals(policy.Platform, platform, StringComparison.OrdinalIgnoreCase)
            && string.Equals(policy.EnvironmentRole, environmentRole, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        var policy = new PolicyRecord
        {
            Id = idFactory.Next("POL"),
            CompanyId = companyId,
            PolicyGuid = CreateStableGuid(companyId, name, policyType, platform, category),
            Name = name,
            PolicyType = policyType,
            Platform = platform,
            Category = category,
            Environment = "Production",
            EnvironmentRole = environmentRole,
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

    /// <summary>
    /// Adds a policy setting whose <paramref name="policyPath"/> has already been
    /// resolved, deriving <c>Source</c> and <c>Behavior</c> from it and dropping the row
    /// when the company, policy, setting name and environment role already carry one.
    /// </summary>
    /// <param name="deduplicationIndex">
    /// Optional index over the settings already present. Callers that add a small number
    /// of rows omit it and the guard scans <see cref="SyntheticEnterpriseWorld.PolicySettings"/>
    /// directly; a caller adding a per-endpoint family supplies one so the guard stays
    /// constant-time as the collection grows. The guard itself is identical either way.
    /// </param>
    internal static void AddResolvedPolicySetting(
        SyntheticEnterpriseWorld world,
        IIdFactory idFactory,
        string companyId,
        string policyId,
        string settingName,
        string settingCategory,
        string valueType,
        string configuredValue,
        string policyPath,
        string? registryPath = null,
        bool isLegacy = false,
        bool isConflicting = false,
        string? sourceReference = null,
        string environmentRole = "Source",
        PolicySettingDeduplicationIndex? deduplicationIndex = null)
    {
        if (deduplicationIndex is not null)
        {
            if (!deduplicationIndex.TryAdd(companyId, policyId, settingName, environmentRole))
            {
                return;
            }
        }
        else if (world.PolicySettings.Any(setting =>
                     setting.CompanyId == companyId
                     && string.Equals(setting.PolicyId, policyId, StringComparison.OrdinalIgnoreCase)
                     && string.Equals(setting.SettingName, settingName, StringComparison.OrdinalIgnoreCase)
                     && string.Equals(setting.EnvironmentRole, environmentRole, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        world.PolicySettings.Add(new PolicySettingRecord
        {
            Id = idFactory.Next("PST"),
            CompanyId = companyId,
            PolicyId = policyId,
            SettingName = settingName,
            SettingCategory = settingCategory,
            PolicyPath = policyPath,
            RegistryPath = registryPath,
            ValueType = valueType,
            ConfiguredValue = configuredValue,
            Source = ResolveSource(world, policyId, settingCategory, policyPath, registryPath),
            Behavior = ResolveBehavior(settingCategory, policyPath, registryPath),
            EnvironmentRole = environmentRole,
            IsLegacy = isLegacy,
            IsConflicting = isConflicting,
            SourceReference = sourceReference
        });
    }

    /// <summary>
    /// Derives the collection source a setting would have been gathered from. The value
    /// belongs to a closed set every consumer of the normalized export understands, so it
    /// is never supplied by a caller.
    /// </summary>
    internal static string ResolveSource(
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

        // An audit row collected from a Group Policy report export is evidence from the
        // report, not from a backup's audit.csv, and carries the combined value spelled the
        // way a report reader renders it. Recording it as AuditCsv would put a spelling and
        // a provenance on the same row that contradict each other.
        if (string.Equals(settingCategory, "AuditPolicyReport", StringComparison.OrdinalIgnoreCase))
        {
            return "GPO";
        }

        if (policyPath.Contains("Security Settings", StringComparison.OrdinalIgnoreCase)
            || policyPath.Contains("Account Policies", StringComparison.OrdinalIgnoreCase)
            || string.Equals(settingCategory, "UserRightsAssignment", StringComparison.OrdinalIgnoreCase)
            || string.Equals(settingCategory, "FileSecurity", StringComparison.OrdinalIgnoreCase)
            || string.Equals(settingCategory, "RegistryKeys", StringComparison.OrdinalIgnoreCase))
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

    /// <summary>
    /// Derives how the setting presents in a Group Policy editor: administrative-template
    /// settings write the registry and show blue, security-settings extensions show red.
    /// </summary>
    internal static string ResolveBehavior(
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
            || string.Equals(settingCategory, "AuditPolicy", StringComparison.OrdinalIgnoreCase)
            || string.Equals(settingCategory, "AuditPolicyReport", StringComparison.OrdinalIgnoreCase)
            || string.Equals(settingCategory, "FileSecurity", StringComparison.OrdinalIgnoreCase)
            || string.Equals(settingCategory, "RegistryKeys", StringComparison.OrdinalIgnoreCase))
        {
            return "RedDot";
        }

        return "Unknown";
    }

    /// <summary>
    /// Produces a version-5-shaped GUID from the supplied components, so a policy object
    /// carries the same identifier across runs of the same scenario.
    /// </summary>
    internal static string CreateStableGuid(params string[] components)
    {
        var seed = string.Join("|", components.Where(component => !string.IsNullOrWhiteSpace(component)));
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(seed));
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes[..16]).ToString();
    }
}

/// <summary>
/// Constant-time form of the policy-setting duplicate guard, seeded from the settings a
/// world already holds. It exists only so a generator emitting a family of rows per
/// endpoint does not turn the guard into a quadratic scan; the key it compares is the same
/// company, policy, setting name and environment role the linear guard compares.
/// </summary>
internal sealed class PolicySettingDeduplicationIndex
{
    private readonly HashSet<string> _keys;

    internal PolicySettingDeduplicationIndex(SyntheticEnterpriseWorld world)
    {
        _keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var setting in world.PolicySettings)
        {
            _keys.Add(BuildKey(setting.CompanyId, setting.PolicyId, setting.SettingName, setting.EnvironmentRole));
        }
    }

    /// <summary>
    /// Records the key and reports whether it was absent, meaning the caller may add the
    /// row.
    /// </summary>
    internal bool TryAdd(string companyId, string policyId, string settingName, string environmentRole)
        => _keys.Add(BuildKey(companyId, policyId, settingName, environmentRole));

    /// <summary>
    /// Mirrors the linear guard's comparison exactly: the company identifier is compared
    /// ordinally and the remaining three parts case-insensitively.
    /// </summary>
    private static string BuildKey(string companyId, string policyId, string settingName, string environmentRole)
        => string.Join(
            '',
            companyId,
            policyId.ToUpperInvariant(),
            settingName.ToUpperInvariant(),
            environmentRole.ToUpperInvariant());
}
