namespace SyntheticEnterprise.Core.Scenarios;

using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Plugins;

public interface IFirstPartyPackPathResolver
{
    IReadOnlyList<string> ResolvePackRootPaths();
}

public interface IScenarioPackProfileResolver
{
    ExternalPluginScenarioProfile Resolve(ScenarioDefinition scenario);
}

public sealed class FirstPartyPackPathResolver : IFirstPartyPackPathResolver
{
    public IReadOnlyList<string> ResolvePackRootPaths()
    {
        var searchRoots = new[]
        {
            AppContext.BaseDirectory,
            Path.GetDirectoryName(typeof(FirstPartyPackPathResolver).Assembly.Location),
            Environment.CurrentDirectory
        }
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Select(path => Path.GetFullPath(path!))
        .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var root in searchRoots)
        {
            var current = new DirectoryInfo(root);
            while (current is not null)
            {
                var rootFromCurrent = Path.Combine(current.FullName, "packs", "first-party");
                if (Directory.Exists(rootFromCurrent))
                {
                    return new[] { rootFromCurrent };
                }

                if (File.Exists(Path.Combine(current.FullName, "DataGen.slnx")))
                {
                    var rootFromSolution = Path.Combine(current.FullName, "packs", "first-party");
                    if (Directory.Exists(rootFromSolution))
                    {
                        return new[] { rootFromSolution };
                    }
                }

                current = current.Parent;
            }
        }

        return Array.Empty<string>();
    }
}

public sealed class ScenarioPackProfileResolver : IScenarioPackProfileResolver
{
    private readonly IFirstPartyPackPathResolver _packPathResolver;

    public ScenarioPackProfileResolver(IFirstPartyPackPathResolver packPathResolver)
    {
        _packPathResolver = packPathResolver;
    }

    public ExternalPluginScenarioProfile Resolve(ScenarioDefinition scenario)
    {
        var externalProfile = scenario.ExternalPlugins;
        var packProfile = scenario.Packs;

        if ((!packProfile.IncludeBundledPacks && packProfile.PackRootPaths.Count == 0)
            && packProfile.EnabledPacks.Count == 0)
        {
            return externalProfile;
        }

        var pluginRootPaths = new HashSet<string>(externalProfile.PluginRootPaths, StringComparer.OrdinalIgnoreCase);
        foreach (var path in packProfile.PackRootPaths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            pluginRootPaths.Add(path);
        }

        if (packProfile.IncludeBundledPacks)
        {
            foreach (var path in _packPathResolver.ResolvePackRootPaths())
            {
                pluginRootPaths.Add(path);
            }
        }

        var enabledCapabilities = new HashSet<string>(externalProfile.EnabledCapabilities, StringComparer.OrdinalIgnoreCase);
        var capabilityConfigurations = externalProfile.CapabilityConfigurations
            .Select(configuration => new ExternalPluginCapabilityConfiguration
            {
                Capability = configuration.Capability,
                Settings = new Dictionary<string, string?>(configuration.Settings, StringComparer.OrdinalIgnoreCase)
            })
            .ToDictionary(configuration => configuration.Capability, StringComparer.OrdinalIgnoreCase);

        foreach (var pack in packProfile.EnabledPacks.Where(pack => pack.Enabled && !string.IsNullOrWhiteSpace(pack.PackId)))
        {
            enabledCapabilities.Add(pack.PackId);

            if (!capabilityConfigurations.TryGetValue(pack.PackId, out var configuration))
            {
                configuration = new ExternalPluginCapabilityConfiguration
                {
                    Capability = pack.PackId,
                    Settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                };
                capabilityConfigurations[pack.PackId] = configuration;
            }

            foreach (var setting in pack.Settings)
            {
                configuration.Settings[setting.Key] = setting.Value;
            }
        }

        return new ExternalPluginScenarioProfile
        {
            PluginRootPaths = pluginRootPaths.ToList(),
            EnabledCapabilities = enabledCapabilities.ToList(),
            CapabilityConfigurations = capabilityConfigurations.Values.ToList(),
            AllowAssemblyPlugins = externalProfile.AllowAssemblyPlugins,
            ExecutionTimeoutSeconds = externalProfile.ExecutionTimeoutSeconds,
            MaxGeneratedRecords = externalProfile.MaxGeneratedRecords,
            MaxWarningCount = externalProfile.MaxWarningCount,
            MaxDiagnosticEntries = externalProfile.MaxDiagnosticEntries,
            MaxDiagnosticCharacters = externalProfile.MaxDiagnosticCharacters,
            MaxInputPayloadBytes = externalProfile.MaxInputPayloadBytes,
            MaxOutputPayloadBytes = externalProfile.MaxOutputPayloadBytes,
            RequireContentHashAllowList = externalProfile.RequireContentHashAllowList,
            RequireAssemblyHashApproval = externalProfile.RequireAssemblyHashApproval,
            AllowedContentHashes = externalProfile.AllowedContentHashes.ToList()
        };
    }
}
