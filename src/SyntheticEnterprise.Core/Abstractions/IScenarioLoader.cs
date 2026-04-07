namespace SyntheticEnterprise.Core.Abstractions;

using SyntheticEnterprise.Contracts.Configuration;

public interface IScenarioLoader
{
    ScenarioDefinition LoadFromPath(string path);
    ScenarioDefinition LoadFromJson(string json);
}
