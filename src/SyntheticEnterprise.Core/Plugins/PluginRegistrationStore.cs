namespace SyntheticEnterprise.Core.Plugins;

using System.Text.Json;
using SyntheticEnterprise.Contracts.Plugins;

public interface IGenerationPluginRegistrationStore
{
    string StoragePath { get; }
    IReadOnlyList<GenerationPluginRegistration> GetAll();
    void SaveAll(IReadOnlyList<GenerationPluginRegistration> registrations);
}

public interface IGenerationPluginRegistrationService
{
    IReadOnlyList<GenerationPluginRegistration> GetAll();
    GenerationPluginRegistrationResult Register(IEnumerable<string> rootPaths, bool allowAssemblyPlugins);
    int Unregister(IEnumerable<string>? capabilities, IEnumerable<string>? rootPaths);
    ExternalPluginExecutionSettings ApplyRegistrations(ExternalPluginExecutionSettings settings, bool includeAllRegisteredCapabilities);
}

internal sealed class PluginRegistrationEnvelope
{
    public List<GenerationPluginRegistration> Registrations { get; init; } = new();
}

public sealed class JsonGenerationPluginRegistrationStore : IGenerationPluginRegistrationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public JsonGenerationPluginRegistrationStore()
    {
        var overridePath = Environment.GetEnvironmentVariable("SYNTHETIC_ENTERPRISE_PLUGIN_REGISTRY_PATH");
        StoragePath = string.IsNullOrWhiteSpace(overridePath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SyntheticEnterprise", "plugin-registrations.json")
            : Path.GetFullPath(overridePath);
    }

    public string StoragePath { get; }

    public IReadOnlyList<GenerationPluginRegistration> GetAll()
    {
        if (!File.Exists(StoragePath))
        {
            return Array.Empty<GenerationPluginRegistration>();
        }

        try
        {
            var envelope = JsonSerializer.Deserialize<PluginRegistrationEnvelope>(File.ReadAllText(StoragePath), JsonOptions);
            return envelope?.Registrations
                ?.OrderBy(item => item.Capability, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.RootPath, StringComparer.OrdinalIgnoreCase)
                .ToList()
                ?? new List<GenerationPluginRegistration>();
        }
        catch
        {
            return Array.Empty<GenerationPluginRegistration>();
        }
    }

    public void SaveAll(IReadOnlyList<GenerationPluginRegistration> registrations)
    {
        var directory = Path.GetDirectoryName(StoragePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var envelope = new PluginRegistrationEnvelope
        {
            Registrations = registrations
                .OrderBy(item => item.Capability, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.RootPath, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

        File.WriteAllText(StoragePath, JsonSerializer.Serialize(envelope, JsonOptions));
    }
}

public sealed class GenerationPluginRegistrationService : IGenerationPluginRegistrationService
{
    private readonly IExternalGenerationPluginCatalog _catalog;
    private readonly IGenerationPluginRegistrationStore _store;

    public GenerationPluginRegistrationService(IExternalGenerationPluginCatalog catalog, IGenerationPluginRegistrationStore store)
    {
        _catalog = catalog;
        _store = store;
    }

    public IReadOnlyList<GenerationPluginRegistration> GetAll()
        => _store.GetAll();

    public GenerationPluginRegistrationResult Register(IEnumerable<string> rootPaths, bool allowAssemblyPlugins)
    {
        var inspectionSettings = new ExternalPluginExecutionSettings
        {
            Enabled = true,
            PluginRootPaths = rootPaths.Where(path => !string.IsNullOrWhiteSpace(path)).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            AllowAssemblyPlugins = allowAssemblyPlugins,
            RequireAssemblyHashApproval = false
        };

        var inspected = _catalog.Inspect(inspectionSettings.PluginRootPaths, inspectionSettings);
        var selected = inspected
            .Where(record => record.Parsed && record.Valid && record.SecurityAllowed)
            .Where(record => record.ExecutionMode != PluginExecutionMode.DotNetAssembly || allowAssemblyPlugins)
            .Where(record => !string.IsNullOrWhiteSpace(record.ContentHash))
            .ToList();

        var messages = inspected
            .Where(record => !selected.Contains(record))
            .SelectMany(record =>
            {
                var reasons = new List<string>();
                reasons.AddRange(record.ValidationMessages);
                reasons.AddRange(record.SecurityMessages);
                if (record.ExecutionMode == PluginExecutionMode.DotNetAssembly && !allowAssemblyPlugins)
                {
                    reasons.Add("Registration skipped because AllowAssemblyPlugins was not specified.");
                }

                if (reasons.Count == 0)
                {
                    reasons.Add("Registration skipped because the plugin is not eligible for approval.");
                }

                return reasons.Select(reason => $"{record.Capability}: {reason}");
            })
            .ToList();

        var registrations = _store.GetAll().ToList();
        foreach (var plugin in selected)
        {
            registrations.RemoveAll(existing =>
                string.Equals(existing.Capability, plugin.Capability, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.RootPath, Path.GetDirectoryName(plugin.SourcePath) is { Length: > 0 } ? Path.GetFullPath(Path.GetDirectoryName(plugin.SourcePath)!) : plugin.SourcePath, StringComparison.OrdinalIgnoreCase));

            registrations.Add(new GenerationPluginRegistration
            {
                Capability = plugin.Capability,
                DisplayName = plugin.DisplayName,
                RootPath = ResolvePluginRootPath(plugin),
                SourcePath = plugin.SourcePath,
                ContentHash = plugin.ContentHash!,
                ExecutionMode = plugin.ExecutionMode,
                PluginKind = plugin.PluginKind,
                AllowAssemblyPlugins = plugin.ExecutionMode == PluginExecutionMode.DotNetAssembly
            });
        }

        registrations = registrations
            .GroupBy(item => $"{item.Capability}|{item.RootPath}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.RegisteredAtUtc, StringComparer.OrdinalIgnoreCase).First())
            .ToList();

        _store.SaveAll(registrations);

        return new GenerationPluginRegistrationResult
        {
            Registered = registrations
                .Where(item => selected.Any(selectedPlugin =>
                    string.Equals(selectedPlugin.Capability, item.Capability, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(ResolvePluginRootPath(selectedPlugin), item.RootPath, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(item => item.Capability, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Messages = messages
        };
    }

    public int Unregister(IEnumerable<string>? capabilities, IEnumerable<string>? rootPaths)
    {
        var capabilitySet = capabilities?.Where(value => !string.IsNullOrWhiteSpace(value)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rootSet = rootPaths?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var registrations = _store.GetAll().ToList();
        var originalCount = registrations.Count;
        registrations.RemoveAll(registration =>
            (capabilitySet is null || capabilitySet.Count == 0 || capabilitySet.Contains(registration.Capability))
            && (rootSet is null || rootSet.Count == 0 || rootSet.Contains(registration.RootPath)));

        _store.SaveAll(registrations);
        return originalCount - registrations.Count;
    }

    public ExternalPluginExecutionSettings ApplyRegistrations(ExternalPluginExecutionSettings settings, bool includeAllRegisteredCapabilities)
    {
        var registrations = _store.GetAll()
            .Where(item => item.Enabled)
            .ToList();

        var requestedCapabilities = settings.EnabledCapabilities.Count == 0 && includeAllRegisteredCapabilities
            ? registrations.Select(item => item.Capability).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : settings.EnabledCapabilities.ToList();
        var capabilitySet = requestedCapabilities.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = registrations
            .Where(item => capabilitySet.Count == 0 || capabilitySet.Contains(item.Capability))
            .ToList();

        var mergedRoots = settings.PluginRootPaths
            .Concat(selected.Select(item => item.RootPath))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var mergedCapabilities = requestedCapabilities
            .Concat(selected.Select(item => item.Capability))
            .Where(capability => !string.IsNullOrWhiteSpace(capability))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var mergedHashes = settings.AllowedContentHashes
            .Concat(selected.Select(item => item.ContentHash))
            .Where(hash => !string.IsNullOrWhiteSpace(hash))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ExternalPluginExecutionSettings
        {
            Enabled = settings.Enabled || selected.Count > 0,
            PluginRootPaths = mergedRoots,
            EnabledCapabilities = mergedCapabilities,
            CapabilityConfigurations = settings.CapabilityConfigurations.ToList(),
            AllowAssemblyPlugins = settings.AllowAssemblyPlugins || selected.Any(item => item.AllowAssemblyPlugins),
            ExecutionTimeoutSeconds = settings.ExecutionTimeoutSeconds,
            MaxGeneratedRecords = settings.MaxGeneratedRecords,
            MaxWarningCount = settings.MaxWarningCount,
            MaxDiagnosticEntries = settings.MaxDiagnosticEntries,
            MaxDiagnosticCharacters = settings.MaxDiagnosticCharacters,
            MaxInputPayloadBytes = settings.MaxInputPayloadBytes,
            MaxOutputPayloadBytes = settings.MaxOutputPayloadBytes,
            RequireContentHashAllowList = settings.RequireContentHashAllowList || selected.Count > 0,
            RequireAssemblyHashApproval = settings.RequireAssemblyHashApproval,
            AllowedContentHashes = mergedHashes
        };
    }

    private static string ResolvePluginRootPath(GenerationPluginInspectionRecord plugin)
    {
        var sourcePath = Path.GetFullPath(plugin.SourcePath);
        return Path.GetDirectoryName(sourcePath) ?? sourcePath;
    }
}
