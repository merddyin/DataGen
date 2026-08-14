namespace SyntheticEnterprise.Core.Plugins;

using System.Collections;
using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;
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
    private readonly IExternalPluginCatalogProvider _catalogProvider;

    public RestrictedPowerShellExternalPluginHostAdapter(IIdFactory idFactory)
        : this(idFactory, new AuthenticatedExternalPluginCatalogProvider())
    {
    }

    internal RestrictedPowerShellExternalPluginHostAdapter(
        IIdFactory idFactory,
        IExternalPluginCatalogProvider catalogProvider)
    {
        _idFactory = idFactory;
        _catalogProvider = catalogProvider;
    }

    public bool CanExecute(GenerationPluginManifest manifest)
        => manifest.ExecutionMode == PluginExecutionMode.PowerShellScript;

    public ExternalPluginExecutionResult Execute(GenerationPluginManifest manifest, SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs)
    {
        if (!ExternalPluginPathSecurity.TryValidateManifestPaths(manifest, out var pathWarning))
        {
            return Failure(manifest, pathWarning!);
        }

        var executionManifest = ExternalPluginExecutionManifest.Create(manifest, context.GeneratedAt);
        var request = new ExternalPluginRequestMetadata
        {
            Capability = manifest.Capability,
            ScenarioName = context.Scenario.Name,
            Seed = context.Seed,
            GeneratedAt = context.GeneratedAt,
            Metadata = new Dictionary<string, string?>(context.Metadata, StringComparer.OrdinalIgnoreCase),
            PluginSettings = ResolvePluginSettings(context.ExternalPlugins, manifest.Capability)
        };
        CatalogSet pluginCatalogs;
        try
        {
            pluginCatalogs = _catalogProvider.Load(manifest, context.ExternalPlugins);
        }
        catch (PluginInputPayloadLimitExceededException)
        {
            return Failure(
                manifest,
                $"Input payload exceeded the configured limit of {context.ExternalPlugins.MaxInputPayloadBytes} bytes.");
        }
        catch (PluginPathSecurityException ex)
        {
            return Failure(manifest, ex.Message);
        }

        if (!TryValidateInputPayload(world, request, pluginCatalogs, context.ExternalPlugins, out var payloadWarning))
        {
            return new ExternalPluginExecutionResult
            {
                Manifest = manifest,
                Executed = false,
                Warnings = new() { payloadWarning! }
            };
        }

        var scriptWorld = CloneForPlugin(world);
        var scriptRequest = CloneForPlugin(request);
        pluginCatalogs = CloneForPlugin(pluginCatalogs);

        using var runspace = RunspaceFactory.CreateRunspace(CreateSessionState(
            executionManifest,
            scriptWorld,
            scriptRequest,
            pluginCatalogs));
        runspace.Open();

        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        using var verifiedEntryPoint = ExternalPluginPathSecurity.OpenVerifiedEntryPoint(manifest, out var entryPointWarning);
        if (verifiedEntryPoint is null)
        {
            return Failure(manifest, entryPointWarning!);
        }

        string scriptText;
        try
        {
            scriptText = ExternalPluginPathSecurity.ReadVerifiedText(verifiedEntryPoint);
        }
        catch (PluginInputPayloadLimitExceededException)
        {
            return Failure(
                manifest,
                $"Plugin entry point exceeded the approved package-file limit of {ExternalPluginPathSecurity.MaximumEntryPointBytes} bytes.");
        }

        powerShell.AddScript(scriptText, useLocalScope: true);

        using var output = new PSDataCollection<PSObject>();
        using var streamCollector = new PowerShellStreamCollector(powerShell, output, context.ExternalPlugins);
        try
        {
            var asyncResult = powerShell.BeginInvoke<PSObject, PSObject>(input: null, output);
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

                return Failure(manifest, $"Execution timed out after {timeout.TotalSeconds:0} seconds.");
            }

            powerShell.EndInvoke(asyncResult);
        }
        catch (PipelineStoppedException) when (streamCollector.LimitExceeded)
        {
            return Failure(
                manifest,
                $"Plugin output exceeded the configured limit of {context.ExternalPlugins.MaxOutputPayloadBytes} bytes.");
        }
        catch (RuntimeException) when (streamCollector.LimitExceeded)
        {
            return Failure(
                manifest,
                $"Plugin output exceeded the configured limit of {context.ExternalPlugins.MaxOutputPayloadBytes} bytes.");
        }
        catch (RuntimeException ex)
        {
            return Failure(manifest, LimitDiagnostic($"Execution failed in restricted host: {ex.Message}", context.ExternalPlugins));
        }

        if (streamCollector.LimitExceeded)
        {
            return Failure(
                manifest,
                $"Plugin output exceeded the configured limit of {context.ExternalPlugins.MaxOutputPayloadBytes} bytes.");
        }

        var streamDiagnostics = streamCollector.Diagnostics;
        if (streamCollector.Errors.Count > 0)
        {
            return new ExternalPluginExecutionResult
            {
                Manifest = manifest,
                Executed = false,
                Warnings = streamCollector.Errors
                    .Concat(streamDiagnostics)
                    .Take(Math.Max(0, context.ExternalPlugins.MaxWarningCount))
                    .ToList()
            };
        }

        var parsed = ParseOutput(
            manifest,
            streamCollector.Output,
            Math.Max(0, context.ExternalPlugins.MaxGeneratedRecords),
            Math.Max(0, context.ExternalPlugins.MaxWarningCount));
        parsed = parsed with
        {
            Warnings = parsed.Warnings
                .Take(Math.Max(0, context.ExternalPlugins.MaxWarningCount))
                .ToList(),
            RecordCount = parsed.RecordCount + streamCollector.DroppedRecordCount,
            WarningCount = parsed.WarningCount + streamCollector.DroppedWarningCount,
            RecordCountIsLowerBound = parsed.RecordCountIsLowerBound || streamCollector.DroppedRecordCountIsLowerBound,
            WarningCountIsLowerBound = parsed.WarningCountIsLowerBound || streamCollector.DroppedWarningCountIsLowerBound
        };
        var boundedRecords = parsed.Records.ToList();
        var boundedWarnings = parsed.Warnings
            .Select(warning => LimitDiagnostic(warning, context.ExternalPlugins))
            .Concat(streamDiagnostics)
            .Take(Math.Max(0, context.ExternalPlugins.MaxWarningCount))
            .ToList();
        AddTruncationWarnings(parsed, boundedRecords.Count, boundedWarnings, context.ExternalPlugins);

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

        return new ExternalPluginExecutionResult
        {
            Manifest = manifest,
            Executed = true,
            Records = boundedRecords,
            Warnings = boundedWarnings
        };
    }

    private static void AddTruncationWarnings(
        ParsedPluginOutput parsed,
        int retainedRecordCount,
        List<string> warnings,
        ExternalPluginExecutionSettings settings)
    {
        var warningLimit = Math.Max(0, settings.MaxWarningCount);
        if (parsed.RecordCount > retainedRecordCount && warningLimit > 0)
        {
            var qualifier = parsed.RecordCountIsLowerBound ? "at least " : string.Empty;
            if (warnings.Count >= warningLimit)
            {
                warnings.RemoveAt(warnings.Count - 1);
            }

            warnings.Add($"Generated records were truncated from {qualifier}{parsed.RecordCount} to {retainedRecordCount}.");
        }

        var retainedPluginWarningCount = Math.Min(parsed.Warnings.Count, warningLimit);
        if (parsed.WarningCount > retainedPluginWarningCount && warnings.Count < warningLimit)
        {
            var qualifier = parsed.WarningCountIsLowerBound ? "at least " : string.Empty;
            warnings.Add($"Plugin warnings were truncated from {qualifier}{parsed.WarningCount} to {retainedPluginWarningCount}.");
        }
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

    private ParsedPluginOutput ParseOutput(
        GenerationPluginManifest manifest,
        IReadOnlyCollection<PSObject> output,
        int maxRecords,
        int maxWarnings)
    {
        var records = new List<PluginGeneratedRecord>();
        var warnings = new List<string>();
        var recordCount = 0;
        var warningCount = 0;
        var recordCountIsLowerBound = false;
        var warningCountIsLowerBound = false;

        foreach (var item in output)
        {
            var baseObject = item.BaseObject;
            if (TryGetProperty(baseObject, "Warnings", out var warningValues))
            {
                foreach (var warning in EnumerateObjects(warningValues)
                             .Select(value => value?.ToString() ?? string.Empty)
                             .Where(value => !string.IsNullOrWhiteSpace(value)))
                {
                    warningCount++;
                    if (warnings.Count < maxWarnings)
                    {
                        warnings.Add(warning);
                    }
                    else
                    {
                        warningCountIsLowerBound = true;
                        break;
                    }
                }
            }

            if (TryGetProperty(baseObject, "Records", out var rawRecords))
            {
                foreach (var record in EnumerateObjects(rawRecords))
                {
                    recordCount++;
                    if (records.Count >= maxRecords)
                    {
                        recordCountIsLowerBound = true;
                        break;
                    }

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
                recordCount++;
                if (records.Count < maxRecords)
                {
                    records.Add(directRecord);
                }
            }
        }

        return new ParsedPluginOutput(
            records,
            warnings,
            recordCount,
            warningCount,
            recordCountIsLowerBound,
            warningCountIsLowerBound);
    }

    private static T CloneForPlugin<T>(T value)
    {
        if (value is null)
        {
            return value!;
        }

        return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, JsonOptions), JsonOptions)!;
    }

    private static ExternalPluginExecutionResult Failure(GenerationPluginManifest manifest, string warning)
        => new()
        {
            Manifest = manifest,
            Executed = false,
            Warnings = new() { warning }
        };

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
        try
        {
            using var payloadStream = new BoundedPluginPayloadStream(Stream.Null, Math.Max(1024, settings.MaxInputPayloadBytes));
            JsonSerializer.Serialize(payloadStream, world, JsonOptions);
            JsonSerializer.Serialize(payloadStream, request, JsonOptions);
            JsonSerializer.Serialize(payloadStream, catalogs, JsonOptions);
            warning = null;
            return true;
        }
        catch (PluginInputPayloadLimitExceededException)
        {
            warning = $"Input payload exceeded the configured limit of {settings.MaxInputPayloadBytes} bytes.";
            return false;
        }

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
        boundedRecords = records;
        boundedWarnings = warnings;
        var maxBytes = Math.Max(1024, settings.MaxOutputPayloadBytes);
        if (!TryGetSerializedSize(
                new ExternalPluginExecutionResult
                {
                    Manifest = manifest,
                    Executed = true,
                    Records = new(),
                    Warnings = new()
                },
                maxBytes,
                out var totalBytes))
        {
            failureWarning = $"Plugin output exceeded the configured limit of {settings.MaxOutputPayloadBytes} bytes.";
            boundedRecords = new();
            boundedWarnings = new();
            return false;
        }

        var recordSizes = boundedRecords
            .Select(record => GetSerializedSizeOrOverflow(record, maxBytes))
            .ToList();
        var warningSizes = boundedWarnings
            .Select(warning => GetSerializedSizeOrOverflow(warning, maxBytes))
            .ToList();
        totalBytes += GetArrayContentSize(recordSizes) + GetArrayContentSize(warningSizes);

        while (boundedWarnings.Count > 0 && totalBytes > maxBytes)
        {
            var last = warningSizes.Count - 1;
            totalBytes -= warningSizes[last] + (warningSizes.Count > 1 ? 1 : 0);
            warningSizes.RemoveAt(last);
            boundedWarnings.RemoveAt(last);
        }

        while (boundedRecords.Count > 0 && totalBytes > maxBytes)
        {
            var last = recordSizes.Count - 1;
            totalBytes -= recordSizes[last] + (recordSizes.Count > 1 ? 1 : 0);
            recordSizes.RemoveAt(last);
            boundedRecords.RemoveAt(last);
        }

        if (totalBytes <= maxBytes)
        {
            failureWarning = null;
            return true;
        }

        failureWarning = $"Plugin output exceeded the configured limit of {settings.MaxOutputPayloadBytes} bytes.";
        boundedRecords = new();
        boundedWarnings = new();
        return false;
    }

    private static long GetArrayContentSize(IReadOnlyCollection<long> itemSizes)
        => itemSizes.Sum() + Math.Max(0, itemSizes.Count - 1);

    private static long GetSerializedSizeOrOverflow<T>(T value, long maxBytes)
        => TryGetSerializedSize(value, maxBytes, out var bytes) ? bytes : maxBytes + 1;

    private static bool TryGetSerializedSize<T>(T value, long maxBytes, out long bytes)
    {
        using var stream = new BoundedPluginPayloadStream(Stream.Null, maxBytes);
        try
        {
            JsonSerializer.Serialize(stream, value, JsonOptions);
            bytes = stream.BytesWritten;
            return true;
        }
        catch (PluginInputPayloadLimitExceededException)
        {
            bytes = maxBytes + 1;
            return false;
        }
    }

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

    private static IEnumerable<object> EnumerateObjects(object? candidate)
    {
        if (candidate is null)
        {
            yield break;
        }

        if (candidate is string)
        {
            yield return candidate;
            yield break;
        }

        if (candidate is IDictionary)
        {
            yield return candidate;
            yield break;
        }

        if (candidate is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                if (item is not null)
                {
                    yield return item;
                }
            }

            yield break;
        }

        yield return candidate;
    }

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

    private sealed record ParsedPluginOutput(
        IReadOnlyList<PluginGeneratedRecord> Records,
        IReadOnlyList<string> Warnings,
        int RecordCount,
        int WarningCount,
        bool RecordCountIsLowerBound,
        bool WarningCountIsLowerBound);

    private sealed class PowerShellStreamCollector : IDisposable
    {
        private readonly PowerShell _powerShell;
        private readonly PSDataCollection<PSObject> _outputBuffer;
        private readonly ExternalPluginExecutionSettings _settings;
        private readonly long _maxBytes;
        private readonly int _maxRetainedRecords;
        private readonly object _sync = new();
        private readonly List<PSObject> _output = new();
        private readonly List<string> _diagnostics = new();
        private readonly List<string> _errors = new();
        private long _consumedBytes;
        private int _stopQueued;

        public PowerShellStreamCollector(
            PowerShell powerShell,
            PSDataCollection<PSObject> outputBuffer,
            ExternalPluginExecutionSettings settings)
        {
            _powerShell = powerShell;
            _outputBuffer = outputBuffer;
            _settings = settings;
            _maxBytes = Math.Max(1024, settings.MaxOutputPayloadBytes);
            _maxRetainedRecords = Math.Max(0, settings.MaxGeneratedRecords);

            outputBuffer.DataAdding += OnOutputAdding;
            outputBuffer.DataAdded += OnOutputAdded;
            powerShell.Streams.Error.DataAdding += OnErrorAdding;
            powerShell.Streams.Error.DataAdded += OnErrorAdded;
            powerShell.Streams.Warning.DataAdding += OnWarningAdding;
            powerShell.Streams.Warning.DataAdded += OnWarningAdded;
            powerShell.Streams.Verbose.DataAdding += OnVerboseAdding;
            powerShell.Streams.Verbose.DataAdded += OnVerboseAdded;
            powerShell.Streams.Debug.DataAdding += OnDebugAdding;
            powerShell.Streams.Debug.DataAdded += OnDebugAdded;
            powerShell.Streams.Information.DataAdding += OnInformationAdding;
            powerShell.Streams.Information.DataAdded += OnInformationAdded;
        }

        public bool LimitExceeded { get; private set; }
        public IReadOnlyList<PSObject> Output => _output;
        public IReadOnlyList<string> Diagnostics => _diagnostics;
        public IReadOnlyList<string> Errors => _errors;
        public int DroppedRecordCount { get; private set; }
        public int DroppedWarningCount { get; private set; }
        public bool DroppedRecordCountIsLowerBound { get; private set; }
        public bool DroppedWarningCountIsLowerBound { get; private set; }
        private int RetainedRecordCount { get; set; }
        private int RetainedOutputWarningCount { get; set; }

        private void OnOutputAdding(object? sender, DataAddingEventArgs eventArgs)
        {
            if (eventArgs.ItemAdded is not PSObject item)
            {
                return;
            }

            lock (_sync)
            {
                var candidate = item.BaseObject;
                if (TryGetProperty(candidate, "Records", out var rawRecords))
                {
                    CaptureBoundedEnvelope(rawRecords, candidate);
                }
                else if (TryGetProperty(candidate, "RecordType", out _))
                {
                    CaptureDirectRecord(item);
                }
                else
                {
                    TryConsume(candidate);
                }
            }
        }

        private void CaptureDirectRecord(PSObject item)
        {
            if (!TryConsume(item.BaseObject))
            {
                return;
            }

            if (RetainedRecordCount < _maxRetainedRecords)
            {
                RetainedRecordCount++;
                _output.Add(item);
                return;
            }

            DroppedRecordCount++;
        }

        private void CaptureBoundedEnvelope(object? rawRecords, object candidate)
        {
            var retainedRecords = new List<object>();
            foreach (var record in EnumerateObjects(rawRecords))
            {
                if (RetainedRecordCount >= _maxRetainedRecords)
                {
                    DroppedRecordCount++;
                    DroppedRecordCountIsLowerBound = true;
                    break;
                }

                if (!TryConsume(record))
                {
                    return;
                }

                retainedRecords.Add(record);
                RetainedRecordCount++;
            }

            var retainedWarnings = new List<string>();
            if (TryGetProperty(candidate, "Warnings", out var rawWarnings))
            {
                var maxWarnings = Math.Max(0, _settings.MaxWarningCount);
                foreach (var warning in EnumerateObjects(rawWarnings)
                             .Select(value => value?.ToString() ?? string.Empty)
                             .Where(value => !string.IsNullOrWhiteSpace(value)))
                {
                    if (RetainedOutputWarningCount >= maxWarnings)
                    {
                        DroppedWarningCount++;
                        DroppedWarningCountIsLowerBound = true;
                        break;
                    }

                    if (!TryConsume(warning))
                    {
                        return;
                    }

                    retainedWarnings.Add(warning);
                    RetainedOutputWarningCount++;
                }
            }

            if (retainedRecords.Count > 0 || retainedWarnings.Count > 0)
            {
                _output.Add(PSObject.AsPSObject(new Hashtable
                {
                    ["Records"] = retainedRecords,
                    ["Warnings"] = retainedWarnings
                }));
            }
        }

        private void OnOutputAdded(object? sender, DataAddedEventArgs eventArgs)
            => DrainOne(_outputBuffer);

        private void OnErrorAdding(object? sender, DataAddingEventArgs eventArgs)
            => AddDiagnostic($"[error] {eventArgs.ItemAdded}", _errors, Math.Max(0, _settings.MaxWarningCount));

        private void OnErrorAdded(object? sender, DataAddedEventArgs eventArgs)
            => DrainOne(_powerShell.Streams.Error);

        private void OnWarningAdding(object? sender, DataAddingEventArgs eventArgs)
            => AddDiagnostic($"[warning] {((WarningRecord)eventArgs.ItemAdded).Message}", _diagnostics, Math.Max(0, _settings.MaxDiagnosticEntries));

        private void OnWarningAdded(object? sender, DataAddedEventArgs eventArgs)
            => DrainOne(_powerShell.Streams.Warning);

        private void OnVerboseAdding(object? sender, DataAddingEventArgs eventArgs)
            => AddDiagnostic($"[verbose] {((VerboseRecord)eventArgs.ItemAdded).Message}", _diagnostics, Math.Max(0, _settings.MaxDiagnosticEntries));

        private void OnVerboseAdded(object? sender, DataAddedEventArgs eventArgs)
            => DrainOne(_powerShell.Streams.Verbose);

        private void OnDebugAdding(object? sender, DataAddingEventArgs eventArgs)
            => AddDiagnostic($"[debug] {((DebugRecord)eventArgs.ItemAdded).Message}", _diagnostics, Math.Max(0, _settings.MaxDiagnosticEntries));

        private void OnDebugAdded(object? sender, DataAddedEventArgs eventArgs)
            => DrainOne(_powerShell.Streams.Debug);

        private void OnInformationAdding(object? sender, DataAddingEventArgs eventArgs)
            => AddDiagnostic($"[info] {((InformationRecord)eventArgs.ItemAdded).MessageData}", _diagnostics, Math.Max(0, _settings.MaxDiagnosticEntries));

        private void OnInformationAdded(object? sender, DataAddedEventArgs eventArgs)
            => DrainOne(_powerShell.Streams.Information);

        private void AddDiagnostic(string message, List<string> target, int maxEntries)
        {
            if (!TryConsume(message))
            {
                return;
            }

            lock (_sync)
            {
                if (target.Count < maxEntries)
                {
                    target.Add(LimitDiagnostic(message, _settings));
                }
            }
        }

        private bool TryConsume<T>(T value)
        {
            lock (_sync)
            {
                if (LimitExceeded)
                {
                    return false;
                }

                try
                {
                    var estimator = new PowerShellObjectSizeEstimator(_maxBytes - _consumedBytes);
                    if (!estimator.TryMeasure(value))
                    {
                        LimitExceeded = true;
                        QueueStop();
                        return false;
                    }

                    _consumedBytes += estimator.Bytes;
                    return true;
                }
                catch
                {
                    LimitExceeded = true;
                    QueueStop();
                    return false;
                }
            }
        }

        private void QueueStop()
        {
            if (Interlocked.Exchange(ref _stopQueued, 1) != 0)
            {
                return;
            }

            // The script runs in-process and may allocate internally before emitting. This collector
            // bounds only the host-side buffering of objects and diagnostic records as they arrive.
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    _powerShell.Stop();
                }
                catch
                {
                }
            });
        }

        private static void DrainOne<T>(PSDataCollection<T> collection)
        {
            if (collection.Count > 0)
            {
                collection.ReadAll();
            }
        }

        public void Dispose()
        {
            _outputBuffer.DataAdding -= OnOutputAdding;
            _outputBuffer.DataAdded -= OnOutputAdded;
            _powerShell.Streams.Error.DataAdding -= OnErrorAdding;
            _powerShell.Streams.Error.DataAdded -= OnErrorAdded;
            _powerShell.Streams.Warning.DataAdding -= OnWarningAdding;
            _powerShell.Streams.Warning.DataAdded -= OnWarningAdded;
            _powerShell.Streams.Verbose.DataAdding -= OnVerboseAdding;
            _powerShell.Streams.Verbose.DataAdded -= OnVerboseAdded;
            _powerShell.Streams.Debug.DataAdding -= OnDebugAdding;
            _powerShell.Streams.Debug.DataAdded -= OnDebugAdded;
            _powerShell.Streams.Information.DataAdding -= OnInformationAdding;
            _powerShell.Streams.Information.DataAdded -= OnInformationAdded;
        }

        private sealed class PowerShellObjectSizeEstimator
        {
            private readonly long _maxBytes;
            private readonly HashSet<object> _visited = new(ReferenceEqualityComparer.Instance);

            public PowerShellObjectSizeEstimator(long maxBytes)
            {
                _maxBytes = Math.Max(0, maxBytes);
            }

            public long Bytes { get; private set; }

            public bool TryMeasure(object? value)
            {
                if (value is null)
                {
                    return TryAdd(4);
                }

                var type = value.GetType();
                if (!type.IsValueType && !_visited.Add(value))
                {
                    return TryAdd(16);
                }

                if (value is string text)
                {
                    return TryAdd(Encoding.UTF8.GetByteCount(text) + 2L);
                }

                if (value is PSObject psObject)
                {
                    var baseObject = psObject.BaseObject;
                    if (baseObject is not null
                        && !ReferenceEquals(baseObject, psObject)
                        && baseObject is not PSCustomObject)
                    {
                        return TryMeasure(baseObject);
                    }

                    if (!TryAdd(2))
                    {
                        return false;
                    }

                    foreach (var property in psObject.Properties)
                    {
                        if (!TryMeasure(property.Name) || !TryMeasure(property.Value) || !TryAdd(1))
                        {
                            return false;
                        }
                    }

                    return true;
                }

                if (value is IDictionary dictionary)
                {
                    if (!TryAdd(2))
                    {
                        return false;
                    }

                    foreach (DictionaryEntry entry in dictionary)
                    {
                        if (!TryMeasure(entry.Key?.ToString()) || !TryMeasure(entry.Value) || !TryAdd(1))
                        {
                            return false;
                        }
                    }

                    return true;
                }

                if (value is IEnumerable enumerable)
                {
                    if (!TryAdd(2))
                    {
                        return false;
                    }

                    foreach (var item in enumerable)
                    {
                        if (!TryMeasure(item) || !TryAdd(1))
                        {
                            return false;
                        }
                    }

                    return true;
                }

                return TryAdd(Encoding.UTF8.GetByteCount(value.ToString() ?? string.Empty));
            }

            private bool TryAdd(long bytes)
            {
                if (bytes < 0 || bytes > _maxBytes - Bytes)
                {
                    return false;
                }

                Bytes += bytes;
                return true;
            }
        }
    }
}
