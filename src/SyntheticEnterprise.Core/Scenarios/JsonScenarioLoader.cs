namespace SyntheticEnterprise.Core.Scenarios;

using System.Text.Json;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Core.Abstractions;
using SyntheticEnterprise.Core.Plugins;

public sealed class JsonScenarioLoader : IScenarioLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly IScenarioDefaultsResolver _resolver;
    private readonly IScenarioPluginProfileHydrator _pluginProfileHydrator;

    public JsonScenarioLoader()
        : this(new ScenarioDefaultsResolver(), new ScenarioPluginProfileHydrator(
            new ExternalPluginScenarioBindingService(),
            new ExternalPluginCapabilityResolver(
                new GenerationPluginRegistry(
                    Array.Empty<IWorldGenerationPlugin>(),
                    new FileSystemExternalGenerationPluginCatalog(
                        new GenerationPluginManifestValidator(new DataOnlyGenerationPluginSecurityPolicy()),
                        new DataOnlyGenerationPluginSecurityPolicy(),
                        new AllowListExternalPluginTrustPolicy())))))
    {
    }

    public JsonScenarioLoader(
        IScenarioDefaultsResolver resolver,
        IScenarioPluginProfileHydrator pluginProfileHydrator)
    {
        _resolver = resolver;
        _pluginProfileHydrator = pluginProfileHydrator;
    }

    public ScenarioDefinition LoadFromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Scenario path is required.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Scenario file not found.", path);
        }

        return LoadFromJson(File.ReadAllText(path));
    }

    public ScenarioDefinition LoadFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("Scenario JSON is required.", nameof(json));
        }

        var scenario = _resolver.Resolve(json);
        return _pluginProfileHydrator.Hydrate(scenario).Scenario;
    }
}
