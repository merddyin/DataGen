namespace SyntheticEnterprise.Core.Scenarios;

using System.Text.Json;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Core.Abstractions;

public sealed class JsonScenarioLoader : IScenarioLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

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

        return JsonSerializer.Deserialize<ScenarioDefinition>(json, Options)
            ?? throw new InvalidOperationException("Scenario JSON could not be parsed.");
    }
}
