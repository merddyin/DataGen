namespace SyntheticEnterprise.Core.Plugins;

using SyntheticEnterprise.Contracts.Plugins;

public interface IExternalPluginScenarioBindingService
{
    ExternalPluginExecutionSettings Bind(ExternalPluginScenarioProfile? scenarioProfile, ExternalPluginExecutionOverrides? overrides = null);
}

public sealed class ExternalPluginScenarioBindingService : IExternalPluginScenarioBindingService
{
    public ExternalPluginExecutionSettings Bind(ExternalPluginScenarioProfile? scenarioProfile, ExternalPluginExecutionOverrides? overrides = null)
    {
        scenarioProfile ??= new ExternalPluginScenarioProfile();
        overrides ??= new ExternalPluginExecutionOverrides();

        var pluginRootPaths = MergeDistinct(
            scenarioProfile.PluginRootPaths,
            overrides.PluginRootPaths);
        var enabledCapabilities = MergeDistinct(
            scenarioProfile.EnabledCapabilities,
            overrides.EnabledCapabilities);
        var allowedContentHashes = MergeDistinct(
            scenarioProfile.AllowedContentHashes,
            overrides.AllowedContentHashes);

        return new ExternalPluginExecutionSettings
        {
            Enabled = pluginRootPaths.Count > 0 && enabledCapabilities.Count > 0,
            PluginRootPaths = pluginRootPaths,
            EnabledCapabilities = enabledCapabilities,
            CapabilityConfigurations = MergeCapabilityConfigurations(
                scenarioProfile.CapabilityConfigurations,
                overrides.CapabilityConfigurations),
            AllowAssemblyPlugins = overrides.AllowAssemblyPlugins ?? scenarioProfile.AllowAssemblyPlugins ?? false,
            ExecutionTimeoutSeconds = overrides.ExecutionTimeoutSeconds ?? scenarioProfile.ExecutionTimeoutSeconds ?? 10,
            MaxGeneratedRecords = overrides.MaxGeneratedRecords ?? scenarioProfile.MaxGeneratedRecords ?? 5000,
            MaxWarningCount = overrides.MaxWarningCount ?? scenarioProfile.MaxWarningCount ?? 100,
            MaxDiagnosticEntries = overrides.MaxDiagnosticEntries ?? scenarioProfile.MaxDiagnosticEntries ?? 32,
            MaxDiagnosticCharacters = overrides.MaxDiagnosticCharacters ?? scenarioProfile.MaxDiagnosticCharacters ?? 4096,
            MaxInputPayloadBytes = overrides.MaxInputPayloadBytes ?? scenarioProfile.MaxInputPayloadBytes ?? 2 * 1024 * 1024,
            MaxOutputPayloadBytes = overrides.MaxOutputPayloadBytes ?? scenarioProfile.MaxOutputPayloadBytes ?? 2 * 1024 * 1024,
            RequireContentHashAllowList = overrides.RequireContentHashAllowList ?? scenarioProfile.RequireContentHashAllowList ?? false,
            RequireAssemblyHashApproval = overrides.RequireAssemblyHashApproval ?? scenarioProfile.RequireAssemblyHashApproval ?? true,
            AllowedContentHashes = allowedContentHashes
        };
    }

    private static List<string> MergeDistinct(IEnumerable<string>? baseValues, IEnumerable<string>? overrideValues)
        => (baseValues ?? Array.Empty<string>())
            .Concat(overrideValues ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static List<ExternalPluginCapabilityConfiguration> MergeCapabilityConfigurations(
        IEnumerable<ExternalPluginCapabilityConfiguration>? baseValues,
        IEnumerable<ExternalPluginCapabilityConfiguration>? overrideValues)
    {
        var merged = new Dictionary<string, Dictionary<string, string?>>(StringComparer.OrdinalIgnoreCase);

        foreach (var configuration in (baseValues ?? Array.Empty<ExternalPluginCapabilityConfiguration>())
                     .Concat(overrideValues ?? Array.Empty<ExternalPluginCapabilityConfiguration>()))
        {
            if (string.IsNullOrWhiteSpace(configuration.Capability))
            {
                continue;
            }

            if (!merged.TryGetValue(configuration.Capability, out var settings))
            {
                settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                merged[configuration.Capability] = settings;
            }

            foreach (var kvp in configuration.Settings)
            {
                settings[kvp.Key] = kvp.Value;
            }
        }

        return merged
            .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Select(entry => new ExternalPluginCapabilityConfiguration
            {
                Capability = entry.Key,
                Settings = new Dictionary<string, string?>(entry.Value, StringComparer.OrdinalIgnoreCase)
            })
            .ToList();
    }
}
