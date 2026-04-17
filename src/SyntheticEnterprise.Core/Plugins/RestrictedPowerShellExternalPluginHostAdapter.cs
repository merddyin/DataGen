namespace SyntheticEnterprise.Core.Plugins;

using System.Collections;
using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text.Json;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Contracts.Plugins;
using SyntheticEnterprise.Core.Abstractions;

public sealed class RestrictedPowerShellExternalPluginHostAdapter : IExternalPluginHostAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly IIdFactory _idFactory;

    public RestrictedPowerShellExternalPluginHostAdapter(IIdFactory idFactory)
    {
        _idFactory = idFactory;
    }

    public bool CanExecute(GenerationPluginManifest manifest)
        => manifest.ExecutionMode == PluginExecutionMode.PowerShellScript;

    public ExternalPluginExecutionResult Execute(GenerationPluginManifest manifest, SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs)
    {
        var request = new ExternalPluginRequestMetadata
        {
            Capability = manifest.Capability,
            ScenarioName = context.Scenario.Name,
            Seed = context.Seed,
            GeneratedAt = context.GeneratedAt,
            Metadata = new Dictionary<string, string?>(context.Metadata, StringComparer.OrdinalIgnoreCase),
            PluginSettings = ResolvePluginSettings(context.ExternalPlugins, manifest.Capability)
        };
        var scriptWorld = CloneForPlugin(world);
        var scriptRequest = CloneForPlugin(request);
        var pluginCatalogs = CloneForPlugin(ExternalPluginCatalogLoader.LoadPluginCatalogs(manifest));

        if (!TryValidateInputPayload(scriptWorld, scriptRequest, pluginCatalogs, context.ExternalPlugins, out var payloadWarning))
        {
            return new ExternalPluginExecutionResult
            {
                Manifest = manifest,
                Executed = false,
                Warnings = new() { payloadWarning! }
            };
        }

        using var runspace = RunspaceFactory.CreateRunspace(CreateSessionState(
            manifest,
            scriptWorld,
            scriptRequest,
            pluginCatalogs));
        runspace.Open();

        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell.AddScript(File.ReadAllText(manifest.EntryPoint!), useLocalScope: true);

        List<PSObject> output;
        try
        {
            var asyncResult = powerShell.BeginInvoke();
            var timeout = TimeSpan.FromSeconds(Math.Max(1, context.ExternalPlugins.ExecutionTimeoutSeconds));
            if (!asyncResult.AsyncWaitHandle.WaitOne(timeout))
            {
                try
                {
                    powerShell.Stop();
                }
                catch
                {
                }

                return new ExternalPluginExecutionResult
                {
                    Manifest = manifest,
                    Executed = false,
                    Warnings = new()
                    {
                        $"Execution timed out after {timeout.TotalSeconds:0} seconds."
                    }
                };
            }

            output = powerShell.EndInvoke(asyncResult).ToList();
        }
        catch (RuntimeException ex)
        {
            return new ExternalPluginExecutionResult
            {
                Manifest = manifest,
                Executed = false,
                Warnings = new()
                {
                    LimitDiagnostic($"Execution failed in restricted host: {ex.Message}", context.ExternalPlugins)
                }
            };
        }

        var streamDiagnostics = CollectDiagnostics(powerShell, context.ExternalPlugins);
        if (powerShell.Streams.Error.Count > 0)
        {
            return new ExternalPluginExecutionResult
            {
                Manifest = manifest,
                Executed = false,
                Warnings = powerShell.Streams.Error.Select(error => LimitDiagnostic($"[error] {error}", context.ExternalPlugins))
                    .Concat(streamDiagnostics)
                    .Take(Math.Max(0, context.ExternalPlugins.MaxWarningCount))
                    .ToList()
            };
        }

        var parsed = ParseOutput(manifest, output);
        var boundedRecords = parsed.Records.Take(Math.Max(0, context.ExternalPlugins.MaxGeneratedRecords)).ToList();
        var boundedWarnings = parsed.Warnings
            .Select(warning => LimitDiagnostic(warning, context.ExternalPlugins))
            .Concat(streamDiagnostics)
            .Take(Math.Max(0, context.ExternalPlugins.MaxWarningCount))
            .ToList();

        if (!TryFitOutputPayload(manifest, boundedRecords, boundedWarnings, context.ExternalPlugins, out var payloadBoundRecords, out var payloadBoundWarnings, out var outputWarning))
        {
            return new ExternalPluginExecutionResult
            {
                Manifest = manifest,
                Executed = false,
                Warnings = new() { outputWarning! }
            };
        }

        boundedRecords = payloadBoundRecords;
        boundedWarnings = payloadBoundWarnings;

        if (parsed.Records.Count > boundedRecords.Count)
        {
            boundedWarnings.Add($"Generated records were truncated from {parsed.Records.Count} to {boundedRecords.Count}.");
        }

        if (parsed.Warnings.Count > boundedWarnings.Count)
        {
            boundedWarnings.Add($"Plugin warnings were truncated from {parsed.Warnings.Count} to {boundedWarnings.Count}.");
        }

        return new ExternalPluginExecutionResult
        {
            Manifest = manifest,
            Executed = true,
            Records = boundedRecords,
            Warnings = boundedWarnings
        };
    }

    private InitialSessionState CreateSessionState(GenerationPluginManifest manifest, SyntheticEnterpriseWorld world, object request, CatalogSet pluginCatalogs)
    {
        var state = InitialSessionState.CreateDefault2();
        state.Commands.Clear();
        state.Providers.Clear();
        state.LanguageMode = PSLanguageMode.ConstrainedLanguage;
        state.ThrowOnRunspaceOpenError = true;

        AddAllowedCommand(state, "Write-Output", typeof(Microsoft.PowerShell.Commands.WriteOutputCommand));
        AddAllowedCommand(state, "Select-Object", typeof(Microsoft.PowerShell.Commands.SelectObjectCommand));
        AddAllowedCommand(state, "Where-Object", typeof(Microsoft.PowerShell.Commands.WhereObjectCommand));
        AddAllowedCommand(state, "ForEach-Object", typeof(Microsoft.PowerShell.Commands.ForEachObjectCommand));
        AddAllowedCommand(state, "Sort-Object", typeof(Microsoft.PowerShell.Commands.SortObjectCommand));
        AddAllowedCommand(state, "Group-Object", typeof(Microsoft.PowerShell.Commands.GroupObjectCommand));
        AddAllowedCommand(state, "Measure-Object", typeof(Microsoft.PowerShell.Commands.MeasureObjectCommand));
        AddAllowedCommand(state, "Write-Warning", typeof(Microsoft.PowerShell.Commands.WriteWarningCommand));
        AddAllowedCommand(state, "Write-Verbose", typeof(Microsoft.PowerShell.Commands.WriteVerboseCommand));
        AddAllowedCommand(state, "Write-Debug", typeof(Microsoft.PowerShell.Commands.WriteDebugCommand));
        AddAllowedCommand(state, "Write-Information", typeof(Microsoft.PowerShell.Commands.WriteInformationCommand));
        state.Commands.Add(new SessionStateFunctionEntry("New-PluginRecord", """
            param(
                [string]$RecordType,
                [string]$AssociatedEntityType,
                [string]$AssociatedEntityId,
                [hashtable]$Properties,
                $Payload
            )

            @{
                RecordType = $RecordType
                AssociatedEntityType = $AssociatedEntityType
                AssociatedEntityId = $AssociatedEntityId
                Properties = if ($null -eq $Properties) { @{} } else { $Properties }
                Payload = $Payload
            }
            """));
        state.Commands.Add(new SessionStateFunctionEntry("New-PluginResult", """
            param(
                [object[]]$Records,
                [string[]]$Warnings
            )

            @{
                Records = if ($null -eq $Records) { @() } else { $Records }
                Warnings = if ($null -eq $Warnings) { @() } else { $Warnings }
            }
            """));

        state.Variables.Add(new SessionStateVariableEntry(
            "InputWorld",
            world,
            "Read-only world snapshot",
            ScopedItemOptions.ReadOnly | ScopedItemOptions.AllScope));
        state.Variables.Add(new SessionStateVariableEntry(
            "PluginRequest",
            request,
            "Plugin request metadata",
            ScopedItemOptions.ReadOnly | ScopedItemOptions.AllScope));
        state.Variables.Add(new SessionStateVariableEntry(
            "PluginCatalogs",
            pluginCatalogs,
            "Read-only plugin catalogs",
            ScopedItemOptions.ReadOnly | ScopedItemOptions.AllScope));
        state.Variables.Add(new SessionStateVariableEntry(
            "PluginManifest",
            manifest,
            "Plugin manifest",
            ScopedItemOptions.ReadOnly | ScopedItemOptions.AllScope));

        return state;
    }

    private static void AddAllowedCommand(InitialSessionState state, string name, Type implementingType)
    {
        state.Commands.Add(new SessionStateCmdletEntry(name, implementingType, string.Empty));
    }

    private ParsedPluginOutput ParseOutput(GenerationPluginManifest manifest, IReadOnlyCollection<PSObject> output)
    {
        var records = new List<PluginGeneratedRecord>();
        var warnings = new List<string>();

        foreach (var item in output)
        {
            var baseObject = item.BaseObject;
            if (TryGetProperty(baseObject, "Warnings", out var warningValues))
            {
                warnings.AddRange(ToStringList(warningValues));
            }

            if (TryGetProperty(baseObject, "Records", out var rawRecords))
            {
                foreach (var record in ToObjectList(rawRecords))
                {
                    var parsed = ParseRecord(manifest, record);
                    if (parsed is not null)
                    {
                        records.Add(parsed);
                    }
                }

                continue;
            }

            var directRecord = ParseRecord(manifest, baseObject);
            if (directRecord is not null)
            {
                records.Add(directRecord);
            }
        }

        return new ParsedPluginOutput(records, warnings);
    }

    private static T CloneForPlugin<T>(T value)
    {
        if (value is null)
        {
            return value!;
        }

        return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, JsonOptions), JsonOptions)!;
    }

    private static IEnumerable<string> CollectDiagnostics(PowerShell powerShell, ExternalPluginExecutionSettings settings)
    {
        var diagnostics = new List<string>();
        diagnostics.AddRange(powerShell.Streams.Warning.Select(record => LimitDiagnostic($"[warning] {record.Message}", settings)));
        diagnostics.AddRange(powerShell.Streams.Verbose.Select(record => LimitDiagnostic($"[verbose] {record.Message}", settings)));
        diagnostics.AddRange(powerShell.Streams.Debug.Select(record => LimitDiagnostic($"[debug] {record.Message}", settings)));
        diagnostics.AddRange(powerShell.Streams.Information.Select(record => LimitDiagnostic($"[info] {record.MessageData}", settings)));
        return diagnostics.Take(Math.Max(0, settings.MaxDiagnosticEntries)).ToList();
    }

    private static string LimitDiagnostic(string message, ExternalPluginExecutionSettings settings)
    {
        var maxCharacters = Math.Max(32, settings.MaxDiagnosticCharacters);
        if (string.IsNullOrWhiteSpace(message) || message.Length <= maxCharacters)
        {
            return message;
        }

        return $"{message[..maxCharacters]}...(truncated)";
    }

    private static Dictionary<string, string?> ResolvePluginSettings(ExternalPluginExecutionSettings settings, string capability)
        => settings.CapabilityConfigurations
            .FirstOrDefault(configuration => string.Equals(configuration.Capability, capability, StringComparison.OrdinalIgnoreCase))
            ?.Settings is { } configurationSettings
            ? new Dictionary<string, string?>(configurationSettings, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    private static bool TryValidateInputPayload(
        SyntheticEnterpriseWorld world,
        ExternalPluginRequestMetadata request,
        CatalogSet catalogs,
        ExternalPluginExecutionSettings settings,
        out string? warning)
    {
        var inputPayloadBytes =
            JsonSerializer.SerializeToUtf8Bytes(world, JsonOptions).Length +
            JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions).Length +
            JsonSerializer.SerializeToUtf8Bytes(catalogs, JsonOptions).Length;
        if (inputPayloadBytes <= Math.Max(1024, settings.MaxInputPayloadBytes))
        {
            warning = null;
            return true;
        }

        warning = $"Input payload exceeded the configured limit of {settings.MaxInputPayloadBytes} bytes.";
        return false;
    }

    private static bool TryFitOutputPayload(
        GenerationPluginManifest manifest,
        List<PluginGeneratedRecord> records,
        List<string> warnings,
        ExternalPluginExecutionSettings settings,
        out List<PluginGeneratedRecord> boundedRecords,
        out List<string> boundedWarnings,
        out string? failureWarning)
    {
        boundedRecords = records.ToList();
        boundedWarnings = warnings.ToList();
        var maxBytes = Math.Max(1024, settings.MaxOutputPayloadBytes);

        while (boundedWarnings.Count > 0 && SerializeResponse(manifest, boundedRecords, boundedWarnings).Length > maxBytes)
        {
            boundedWarnings.RemoveAt(boundedWarnings.Count - 1);
        }

        while (boundedRecords.Count > 0 && SerializeResponse(manifest, boundedRecords, boundedWarnings).Length > maxBytes)
        {
            boundedRecords.RemoveAt(boundedRecords.Count - 1);
        }

        if (SerializeResponse(manifest, boundedRecords, boundedWarnings).Length <= maxBytes)
        {
            failureWarning = null;
            return true;
        }

        failureWarning = $"Plugin output exceeded the configured limit of {settings.MaxOutputPayloadBytes} bytes.";
        boundedRecords = new();
        boundedWarnings = new();
        return false;
    }

    private static byte[] SerializeResponse(GenerationPluginManifest manifest, IReadOnlyList<PluginGeneratedRecord> records, IReadOnlyList<string> warnings)
        => JsonSerializer.SerializeToUtf8Bytes(
            new ExternalPluginExecutionResult
            {
                Manifest = manifest,
                Executed = true,
                Records = records.ToList(),
                Warnings = warnings.ToList()
            },
            JsonOptions);

    private PluginGeneratedRecord? ParseRecord(GenerationPluginManifest manifest, object? candidate)
    {
        if (candidate is null)
        {
            return null;
        }

        if (!TryGetProperty(candidate, "RecordType", out var recordTypeValue))
        {
            return null;
        }

        var properties = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (TryGetProperty(candidate, "Properties", out var rawProperties) && rawProperties is not null)
        {
            foreach (var entry in ToDictionary(rawProperties))
            {
                properties[entry.Key] = entry.Value;
            }
        }

        string? jsonPayload = null;
        if (TryGetProperty(candidate, "Payload", out var payload) && payload is not null)
        {
            jsonPayload = JsonSerializer.Serialize(payload, JsonOptions);
        }
        else if (TryGetProperty(candidate, "JsonPayload", out var providedJson) && providedJson is not null)
        {
            jsonPayload = providedJson.ToString();
        }

        return new PluginGeneratedRecord
        {
            Id = _idFactory.Next("PLUG"),
            PluginCapability = manifest.Capability,
            RecordType = recordTypeValue?.ToString() ?? "PluginRecord",
            AssociatedEntityType = TryGetProperty(candidate, "AssociatedEntityType", out var entityType) ? entityType?.ToString() : null,
            AssociatedEntityId = TryGetProperty(candidate, "AssociatedEntityId", out var entityId) ? entityId?.ToString() : null,
            Properties = properties,
            JsonPayload = jsonPayload
        };
    }

    private static bool TryGetProperty(object candidate, string propertyName, out object? value)
    {
        if (candidate is PSObject psObject)
        {
            var property = psObject.Properties[propertyName];
            if (property is not null)
            {
                value = property.Value;
                return true;
            }
        }

        if (candidate is IDictionary dictionary && dictionary.Contains(propertyName))
        {
            value = dictionary[propertyName];
            return true;
        }

        var reflectedProperty = candidate.GetType().GetProperty(propertyName);
        if (reflectedProperty is not null)
        {
            value = reflectedProperty.GetValue(candidate);
            return true;
        }

        value = null;
        return false;
    }

    private static IReadOnlyList<object> ToObjectList(object? candidate)
    {
        if (candidate is null)
        {
            return Array.Empty<object>();
        }

        if (candidate is string)
        {
            return new[] { candidate };
        }

        if (candidate is IDictionary)
        {
            return new[] { candidate };
        }

        if (candidate is IEnumerable enumerable)
        {
            return enumerable.Cast<object>().ToList();
        }

        return new[] { candidate };
    }

    private static IReadOnlyList<string> ToStringList(object? candidate)
        => ToObjectList(candidate).Select(item => item?.ToString() ?? string.Empty).Where(item => !string.IsNullOrWhiteSpace(item)).ToList();

    private static IReadOnlyDictionary<string, string?> ToDictionary(object candidate)
    {
        if (candidate is IDictionary dictionary)
        {
            return dictionary.Keys.Cast<object>()
                .ToDictionary(key => key.ToString() ?? string.Empty, key => dictionary[key]?.ToString(), StringComparer.OrdinalIgnoreCase);
        }

        if (candidate is PSObject psObject)
        {
            return psObject.Properties.ToDictionary(property => property.Name, property => property.Value?.ToString(), StringComparer.OrdinalIgnoreCase);
        }

        return candidate.GetType().GetProperties()
            .ToDictionary(property => property.Name, property => property.GetValue(candidate)?.ToString(), StringComparer.OrdinalIgnoreCase);
    }

    private sealed record ParsedPluginOutput(IReadOnlyList<PluginGeneratedRecord> Records, IReadOnlyList<string> Warnings);
}
