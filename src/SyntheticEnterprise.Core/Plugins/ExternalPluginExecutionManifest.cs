using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using SyntheticEnterprise.Contracts.Plugins;

namespace SyntheticEnterprise.Core.Plugins;

internal static class ExternalPluginExecutionManifest
{
    public static GenerationPluginManifest Create(GenerationPluginManifest manifest, DateTimeOffset generatedAt)
        => new()
        {
            Capability = manifest.Capability,
            DisplayName = manifest.DisplayName,
            Description = manifest.Description,
            PluginKind = manifest.PluginKind,
            ExecutionMode = manifest.ExecutionMode,
            SourcePath = manifest.SourcePath,
            EntryPoint = manifest.EntryPoint,
            LocalDataPaths = manifest.LocalDataPaths.ToList(),
            Dependencies = manifest.Dependencies.ToList(),
            Parameters = manifest.Parameters
                .Select(parameter => new PluginParameterDescriptor
                {
                    Name = parameter.Name,
                    TypeName = parameter.TypeName,
                    HelpText = parameter.HelpText,
                    Required = parameter.Required,
                    DefaultValue = CloneDefaultValue(parameter.DefaultValue)
                })
                .ToList(),
            Security = new PluginSecurityProfile
            {
                DataOnly = manifest.Security.DataOnly,
                RequestedCapabilities = manifest.Security.RequestedCapabilities.ToList()
            },
            Provenance = new PluginProvenance
            {
                ContentHash = manifest.Provenance.ContentHash,
                EntryPointHash = manifest.Provenance.EntryPointHash,
                LocalDataHashes = new Dictionary<string, string>(manifest.Provenance.LocalDataHashes, StringComparer.OrdinalIgnoreCase),
                DiscoveredAtUtc = generatedAt.ToUniversalTime().ToString("O")
            },
            Metadata = new Dictionary<string, string?>(manifest.Metadata, StringComparer.OrdinalIgnoreCase)
        };

    private static object? CloneDefaultValue(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case JsonElement element:
                return element.Clone();
            case JsonNode node:
                return node.DeepClone();
            case string or ValueType:
                return value;
            case Array array:
                var arrayClone = Array.CreateInstance(array.GetType().GetElementType()!, array.Length);
                for (var index = 0; index < array.Length; index++)
                {
                    arrayClone.SetValue(CloneDefaultValue(array.GetValue(index)), index);
                }

                return arrayClone;
            case IDictionary dictionary:
                return CloneDictionary(dictionary);
            case IList list:
                return CloneList(list);
            default:
                var serialized = JsonSerializer.Serialize(value, value.GetType());
                return JsonSerializer.Deserialize(serialized, value.GetType())
                       ?? throw new InvalidOperationException(
                           $"Plugin parameter default of type '{value.GetType().FullName}' could not be detached.");
        }
    }

    private static IDictionary CloneDictionary(IDictionary source)
    {
        IDictionary? clone = null;
        var sourceType = source.GetType();
        var comparer = sourceType.GetProperty("Comparer")?.GetValue(source);
        if (comparer is not null)
        {
            try
            {
                clone = Activator.CreateInstance(sourceType, comparer) as IDictionary;
            }
            catch (MissingMethodException)
            {
            }
        }

        clone ??= Activator.CreateInstance(sourceType) as IDictionary;
        if (clone is null)
        {
            throw new InvalidOperationException(
                $"Plugin parameter dictionary default of type '{sourceType.FullName}' could not be detached.");
        }

        foreach (DictionaryEntry entry in source)
        {
            var key = CloneDefaultValue(entry.Key)
                      ?? throw new InvalidOperationException("Plugin parameter dictionary defaults cannot contain null keys.");
            clone.Add(key, CloneDefaultValue(entry.Value));
        }

        return clone;
    }

    private static IList CloneList(IList source)
    {
        if (Activator.CreateInstance(source.GetType()) is not IList clone)
        {
            throw new InvalidOperationException(
                $"Plugin parameter list default of type '{source.GetType().FullName}' could not be detached.");
        }

        foreach (var item in source)
        {
            clone.Add(CloneDefaultValue(item));
        }

        return clone;
    }
}
