namespace SyntheticEnterprise.Core.Scenarios;

using System.Text.Json;
using SyntheticEnterprise.Contracts.Scenarios;

/// <summary>
/// Reports scenario options that DataGen no longer reads.
/// </summary>
/// <remarks>
/// The inspection runs against the raw scenario document because a retired option maps to no CLR
/// member: <c>UnmappedMemberHandling</c> defaults to <c>Skip</c>, so the property is gone by the
/// time a <see cref="ScenarioEnvelope"/> exists. Only options named in
/// <see cref="ScenarioOptionLifecycleRegistry"/> are reported; unknown properties are left alone so
/// forward-compatible fields and third-party extensions keep working.
/// </remarks>
public static class RetiredScenarioOptionInspector
{
    /// <summary>The kebab-case validation code carried by every retired-option message.</summary>
    public const string ValidationCode = "retired-scenario-option";

    public static IReadOnlyList<ScenarioValidationMessage> Inspect(string? scenarioJson)
        => Inspect(scenarioJson, ScenarioOptionLifecycleRegistry.Default);

    public static IReadOnlyList<ScenarioValidationMessage> Inspect(
        string? scenarioJson,
        ScenarioOptionLifecycleRegistry registry)
    {
        if (string.IsNullOrWhiteSpace(scenarioJson))
        {
            return Array.Empty<ScenarioValidationMessage>();
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(scenarioJson);
        }
        catch (JsonException)
        {
            // Malformed input is reported by deserialization itself; this inspection adds nothing.
            return Array.Empty<ScenarioValidationMessage>();
        }

        using (document)
        {
            var retirements = new List<ScenarioOptionRetirement>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Collect(document.RootElement, "$", registry, retirements, seen);

            return retirements
                .Select(retirement => new ScenarioValidationMessage(
                    ValidationCode,
                    ScenarioValidationSeverity.Error,
                    retirement.Path,
                    retirement.Describe()))
                .ToList();
        }
    }

    private static void Collect(
        JsonElement element,
        string path,
        ScenarioOptionLifecycleRegistry registry,
        List<ScenarioOptionRetirement> retirements,
        HashSet<string> seen)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var childPath = $"{path}.{property.Name}";

                    if (registry.TryGetRetirement(childPath, out var retirement) && seen.Add(retirement.Path))
                    {
                        retirements.Add(retirement);
                    }

                    Collect(property.Value, childPath, registry, retirements, seen);
                }

                break;

            case JsonValueKind.Array:
                // Collection members share one path, matching the "$.companies[].employeeCount"
                // form the rest of scenario validation already uses.
                foreach (var item in element.EnumerateArray())
                {
                    Collect(item, $"{path}[]", registry, retirements, seen);
                }

                break;
        }
    }
}
