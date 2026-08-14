namespace SyntheticEnterprise.Core.Plugins;

using System.Diagnostics;
using System.Text.Json;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Contracts.Plugins;

public sealed class OutOfProcessAssemblyExternalPluginHostAdapter : IExternalPluginHostAdapter
{
    private const string HostAssemblyName = "SyntheticEnterprise.PluginHost.dll";
    private const string HostExecutableName = "SyntheticEnterprise.PluginHost.exe";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly IExternalPluginTemporaryDirectoryManager _temporaryDirectoryManager;
    private readonly IExternalPluginAssemblyStager _assemblyStager;
    private readonly IExternalPluginCatalogProvider _catalogProvider;

    public OutOfProcessAssemblyExternalPluginHostAdapter()
        : this(
            new FileSystemExternalPluginTemporaryDirectoryManager(),
            new FileSystemExternalPluginAssemblyStager(),
            new AuthenticatedExternalPluginCatalogProvider())
    {
    }

    internal OutOfProcessAssemblyExternalPluginHostAdapter(
        IExternalPluginTemporaryDirectoryManager temporaryDirectoryManager)
        : this(
            temporaryDirectoryManager,
            new FileSystemExternalPluginAssemblyStager(),
            new AuthenticatedExternalPluginCatalogProvider())
    {
    }

    internal OutOfProcessAssemblyExternalPluginHostAdapter(
        IExternalPluginTemporaryDirectoryManager temporaryDirectoryManager,
        IExternalPluginAssemblyStager assemblyStager)
        : this(
            temporaryDirectoryManager,
            assemblyStager,
            new AuthenticatedExternalPluginCatalogProvider())
    {
    }

    internal OutOfProcessAssemblyExternalPluginHostAdapter(
        IExternalPluginTemporaryDirectoryManager temporaryDirectoryManager,
        IExternalPluginAssemblyStager assemblyStager,
        IExternalPluginCatalogProvider catalogProvider)
    {
        _temporaryDirectoryManager = temporaryDirectoryManager;
        _assemblyStager = assemblyStager;
        _catalogProvider = catalogProvider;
    }

    public bool CanExecute(GenerationPluginManifest manifest)
        => manifest.ExecutionMode == PluginExecutionMode.DotNetAssembly;

    public ExternalPluginExecutionResult Execute(GenerationPluginManifest manifest, SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs)
    {
        if (!TryValidatePackageProvenance(manifest, out var provenanceWarning))
        {
            return new ExternalPluginExecutionResult
            {
                Manifest = manifest,
                Executed = false,
                Warnings = new()
                {
                    provenanceWarning!
                }
            };
        }

        var executionManifest = ExternalPluginExecutionManifest.Create(manifest, context.GeneratedAt);
        var hostLaunch = ResolveHostPath();
        if (hostLaunch is null)
        {
            return new ExternalPluginExecutionResult
            {
                Manifest = manifest,
                Executed = false,
                Warnings = new()
                {
                    "DotNetAssembly external plugin host is unavailable."
                }
            };
        }

        string tempRoot;
        try
        {
            tempRoot = _temporaryDirectoryManager.CreateDirectory();
        }
        catch (Exception ex)
        {
            return Failure(manifest, $"DotNetAssembly host execution failed: {ex.Message}");
        }

        var requestPath = Path.Combine(tempRoot, "request.json");
        var responsePath = Path.Combine(tempRoot, "response.json");
        ExternalPluginExecutionResult primaryResult;
        try
        {
            primaryResult = ExecuteInTemporaryDirectoryAsync(
                    manifest,
                    executionManifest,
                    world,
                    context,
                    hostLaunch,
                    tempRoot,
                    requestPath,
                    responsePath)
                .GetAwaiter()
                .GetResult();
        }
        catch (PluginInputPayloadLimitExceededException)
        {
            primaryResult = Failure(
                manifest,
                $"Input payload exceeded the configured limit of {context.ExternalPlugins.MaxInputPayloadBytes} bytes.");
        }
        catch (PluginPathSecurityException ex)
        {
            primaryResult = Failure(manifest, ex.Message);
        }
        catch (Exception ex)
        {
            primaryResult = Failure(manifest, $"DotNetAssembly host execution failed: {ex.Message}");
        }

        ExternalPluginCleanupResult cleanup;
        try
        {
            cleanup = _temporaryDirectoryManager.Cleanup(tempRoot, requestPath, responsePath);
        }
        catch (Exception ex)
        {
            cleanup = new ExternalPluginCleanupResult(false, ex.Message);
        }
        if (cleanup.Succeeded)
        {
            return primaryResult;
        }

        return new ExternalPluginExecutionResult
        {
            Manifest = manifest,
            Executed = false,
            Warnings = primaryResult.Warnings
                .Concat(new[] { $"Assembly host cleanup failed: {cleanup.Error}" })
                .ToList()
        };
    }

    private async Task<ExternalPluginExecutionResult> ExecuteInTemporaryDirectoryAsync(
        GenerationPluginManifest manifest,
        GenerationPluginManifest executionManifest,
        SyntheticEnterpriseWorld world,
        GenerationContext context,
        HostLaunchSpec hostLaunch,
        string tempRoot,
        string requestPath,
        string responsePath)
    {
        var pluginCatalogs = _catalogProvider.Load(manifest, context.ExternalPlugins);
        var stagedAssembly = _assemblyStager.Stage(manifest, tempRoot);
        var stagedManifest = CopyManifestWithEntryPoint(executionManifest, stagedAssembly.EntryPoint);
        var request = new ExternalPluginExecutionRequest
        {
            Manifest = stagedManifest,
            InputWorld = world,
            Request = new ExternalPluginRequestMetadata
            {
                Capability = manifest.Capability,
                ScenarioName = context.Scenario.Name,
                Seed = context.Seed,
                GeneratedAt = context.GeneratedAt,
                Metadata = new Dictionary<string, string?>(context.Metadata, StringComparer.OrdinalIgnoreCase),
                PluginSettings = ResolvePluginSettings(context.ExternalPlugins, manifest.Capability)
            },
            PluginCatalogs = pluginCatalogs
        };
        using (var requestStream = new FileStream(requestPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var payloadStream = new BoundedPluginPayloadStream(
                   requestStream,
                   Math.Max(1024, context.ExternalPlugins.MaxInputPayloadBytes)))
        {
            JsonSerializer.Serialize(payloadStream, request, JsonOptions);
        }

        var processStartInfo = CreateProcessStartInfo(hostLaunch, tempRoot, requestPath, responsePath);
        using var process = Process.Start(processStartInfo);
        if (process is null)
        {
            return Failure(manifest, "DotNetAssembly external plugin host could not be started.");
        }

        var outputLimit = Math.Max(1024, context.ExternalPlugins.MaxOutputPayloadBytes);
        var outputBudget = new PluginOutputByteBudget(outputLimit);
        var retainedBytes = (int)Math.Min(
            outputLimit,
            Math.Max(256L, ((long)Math.Max(32, context.ExternalPlugins.MaxDiagnosticCharacters) * 4) + 64));
        var stdOutCapture = new BoundedProcessStreamCapture(process.StandardOutput.BaseStream, outputBudget, retainedBytes);
        var stdErrCapture = new BoundedProcessStreamCapture(process.StandardError.BaseStream, outputBudget, retainedBytes);
        using var captureCancellation = new CancellationTokenSource();
        var stdOutTask = stdOutCapture.CaptureAsync(captureCancellation.Token);
        var stdErrTask = stdErrCapture.CaptureAsync(captureCancellation.Token);
        var exitTask = process.WaitForExitAsync();
        var timeout = TimeSpan.FromSeconds(Math.Max(1, context.ExternalPlugins.ExecutionTimeoutSeconds));
        var timeoutTask = Task.Delay(timeout);
        var completed = await Task.WhenAny(exitTask, outputBudget.LimitExceededTask, timeoutTask).ConfigureAwait(false);

        if (completed == outputBudget.LimitExceededTask)
        {
            KillProcess(process);
            captureCancellation.Cancel();
        }
        else if (completed == timeoutTask)
        {
            KillProcess(process);
            captureCancellation.Cancel();
        }

        await Task.WhenAll(IgnorePipeClosure(stdOutTask), IgnorePipeClosure(stdErrTask)).ConfigureAwait(false);
        if (outputBudget.LimitExceeded)
        {
            return Failure(
                manifest,
                $"Plugin output exceeded the configured limit of {context.ExternalPlugins.MaxOutputPayloadBytes} bytes.");
        }

        if (completed == timeoutTask)
        {
            return Failure(manifest, $"Assembly host timed out after {timeout.TotalSeconds:0} seconds.");
        }

        await exitTask.ConfigureAwait(false);
        ExternalPluginExecutionResponse? response = null;
        if (File.Exists(responsePath))
        {
            try
            {
                using var responseFile = new FileStream(
                    responsePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 64 * 1024,
                    FileOptions.SequentialScan);
                using var responseStream = new BoundedPluginOutputReadStream(responseFile, outputBudget);
                response = JsonSerializer.Deserialize<ExternalPluginExecutionResponse>(responseStream, JsonOptions);
            }
            catch (PluginOutputPayloadLimitExceededException)
            {
                return Failure(
                    manifest,
                    $"Plugin output exceeded the configured limit of {context.ExternalPlugins.MaxOutputPayloadBytes} bytes.");
            }
        }

        var stdOut = stdOutCapture.GetText();
        var stdErr = stdErrCapture.GetText();
        var warnings = response?.Warnings?
            .Select(warning => LimitDiagnostic(warning, context.ExternalPlugins))
            .ToList() ?? new List<string>();

        if (!string.IsNullOrWhiteSpace(stdOut))
        {
            warnings.Add(LimitDiagnostic($"[stdout] {stdOut.Trim()}", context.ExternalPlugins));
        }

        if (!string.IsNullOrWhiteSpace(stdErr))
        {
            warnings.Add(LimitDiagnostic($"[stderr] {stdErr.Trim()}", context.ExternalPlugins));
        }

        if (process.ExitCode != 0 && string.IsNullOrWhiteSpace(stdErr))
        {
            warnings.Add(LimitDiagnostic($"Assembly host exited with code {process.ExitCode}.", context.ExternalPlugins));
        }
        warnings = warnings
            .Take(Math.Max(0, context.ExternalPlugins.MaxDiagnosticEntries))
            .ToList();

        var responseRecords = response?.Records ?? new List<PluginGeneratedRecord>();
        var boundedRecords = responseRecords
            .Take(Math.Max(0, context.ExternalPlugins.MaxGeneratedRecords))
            .ToList();
        var truncationNotices = new List<string>();
        if (responseRecords.Count > boundedRecords.Count)
        {
            truncationNotices.Add($"Generated records were truncated from {responseRecords.Count} to {boundedRecords.Count}.");
        }

        var warningLimit = Math.Max(0, context.ExternalPlugins.MaxWarningCount);
        if (warnings.Count > warningLimit)
        {
            truncationNotices.Add($"Plugin warnings were truncated from {warnings.Count} to {warningLimit}.");
        }

        var boundedWarnings = BuildBoundedAssemblyWarnings(
            warnings,
            truncationNotices,
            warningLimit,
            outputBudget);

        return new ExternalPluginExecutionResult
        {
            Manifest = manifest,
            Executed = response?.Executed == true,
            Records = boundedRecords,
            Warnings = boundedWarnings
        };
    }

    private static ProcessStartInfo CreateProcessStartInfo(
        HostLaunchSpec hostLaunch,
        string tempRoot,
        string requestPath,
        string responsePath)
    {
        var processStartInfo = hostLaunch.UseDotNetHost
            ? new ProcessStartInfo("dotnet")
            : new ProcessStartInfo(hostLaunch.HostPath);
        processStartInfo.RedirectStandardOutput = true;
        processStartInfo.RedirectStandardError = true;
        processStartInfo.UseShellExecute = false;
        processStartInfo.CreateNoWindow = true;
        processStartInfo.WorkingDirectory = tempRoot;
        if (hostLaunch.UseDotNetHost)
        {
            processStartInfo.ArgumentList.Add("exec");
            processStartInfo.ArgumentList.Add(hostLaunch.HostPath);
        }

        processStartInfo.ArgumentList.Add("--request");
        processStartInfo.ArgumentList.Add(requestPath);
        processStartInfo.ArgumentList.Add("--response");
        processStartInfo.ArgumentList.Add(responsePath);
        processStartInfo.Environment["DOTNET_EnableDiagnostics"] = "0";
        processStartInfo.Environment["COMPlus_EnableDiagnostics"] = "0";
        processStartInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        return processStartInfo;
    }

    private static void KillProcess(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
    }

    private static async Task IgnorePipeClosure(Task captureTask)
    {
        try
        {
            await captureTask.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
        }
    }

    private static ExternalPluginExecutionResult Failure(GenerationPluginManifest manifest, string warning)
        => new()
        {
            Manifest = manifest,
            Executed = false,
            Warnings = new() { warning }
        };

    private static HostLaunchSpec? ResolveHostPath()
    {
        var searchRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var startPath in new[]
                 {
                     AppContext.BaseDirectory,
                     Path.GetDirectoryName(typeof(OutOfProcessAssemblyExternalPluginHostAdapter).Assembly.Location) ?? string.Empty
                 }.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            foreach (var candidate in ResolveBundledHostCandidates(startPath))
            {
                if (CanLaunchHostCandidate(candidate))
                {
                    return candidate;
                }
            }

            foreach (var root in EnumerateSelfAndParents(startPath))
            {
                if (!searchRoots.Add(root))
                {
                    continue;
                }

                foreach (var configuration in new[] { "Debug", "Release" })
                {
                    foreach (var candidate in ResolveSourceHostCandidates(root, configuration))
                    {
                        if (CanLaunchHostCandidate(candidate))
                        {
                            return candidate;
                        }
                    }
                }
            }
        }

        return null;
    }

    private static IEnumerable<HostLaunchSpec> ResolveBundledHostCandidates(string startPath)
    {
        yield return new HostLaunchSpec(Path.Combine(startPath, "plugin-host", HostAssemblyName), UseDotNetHost: true);
        yield return new HostLaunchSpec(Path.Combine(startPath, HostAssemblyName), UseDotNetHost: true);

        if (OperatingSystem.IsWindows())
        {
            yield return new HostLaunchSpec(Path.Combine(startPath, "plugin-host", HostExecutableName), UseDotNetHost: false);
            yield return new HostLaunchSpec(Path.Combine(startPath, HostExecutableName), UseDotNetHost: false);
        }
    }

    private static IEnumerable<HostLaunchSpec> ResolveSourceHostCandidates(string root, string configuration)
    {
        var sourceRoot = Path.Combine(root, "src", "SyntheticEnterprise.PluginHost", "bin", configuration, "net8.0");
        yield return new HostLaunchSpec(Path.Combine(sourceRoot, HostAssemblyName), UseDotNetHost: true);

        if (OperatingSystem.IsWindows())
        {
            yield return new HostLaunchSpec(Path.Combine(sourceRoot, HostExecutableName), UseDotNetHost: false);
        }
    }

    private static bool CanLaunchHostCandidate(HostLaunchSpec candidate)
    {
        if (!File.Exists(candidate.HostPath))
        {
            return false;
        }

        if (candidate.UseDotNetHost)
        {
            return true;
        }

        var companionAssemblyPath = Path.ChangeExtension(candidate.HostPath, ".dll");
        return !string.IsNullOrWhiteSpace(companionAssemblyPath) && File.Exists(companionAssemblyPath);
    }

    private static IEnumerable<string> EnumerateSelfAndParents(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        while (current is not null)
        {
            yield return current.FullName;
            current = current.Parent;
        }
    }

    private static bool TryValidatePackageProvenance(GenerationPluginManifest manifest, out string? warning)
    {
        if (!ExternalPluginPathSecurity.TryValidateManifestPaths(manifest, out warning))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(manifest.EntryPoint) || !File.Exists(manifest.EntryPoint))
        {
            warning = "Assembly plugin entry point is unavailable at execution time.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(manifest.Provenance.EntryPointHash))
        {
            warning = "Assembly plugin provenance is incomplete because the discovered entry point hash is missing.";
            return false;
        }

        using (var verifiedEntryPoint = ExternalPluginPathSecurity.OpenVerifiedEntryPoint(manifest, out var entryPointWarning))
        {
            if (verifiedEntryPoint is null)
            {
                warning = entryPointWarning;
                return false;
            }
        }

        foreach (var localDataPath in manifest.LocalDataPaths)
        {
            if (!manifest.Provenance.LocalDataHashes.TryGetValue(localDataPath, out var expectedHash))
            {
                warning = $"Assembly plugin provenance is incomplete for local data path '{localDataPath}'.";
                return false;
            }

            if (!File.Exists(localDataPath))
            {
                warning = $"Assembly plugin local data path '{localDataPath}' is unavailable at execution time.";
                return false;
            }

            var currentHash = ExternalPluginPathSecurity.ComputeVerifiedPackageFileHash(
                manifest.SourcePath!,
                localDataPath,
                ExternalPluginCatalogLoader.MaximumCatalogFileBytes,
                out var localDataWarning);
            if (currentHash is null)
            {
                warning = localDataWarning;
                return false;
            }

            if (!string.Equals(currentHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                warning = $"Assembly plugin local data hash no longer matches discovered provenance for '{localDataPath}'.";
                return false;
            }
        }

        warning = null;
        return true;
    }

    private static Dictionary<string, string?> ResolvePluginSettings(ExternalPluginExecutionSettings settings, string capability)
        => settings.CapabilityConfigurations
            .FirstOrDefault(configuration => string.Equals(configuration.Capability, capability, StringComparison.OrdinalIgnoreCase))
            ?.Settings is { } configurationSettings
            ? new Dictionary<string, string?>(configurationSettings, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    private static List<string> BuildBoundedAssemblyWarnings(
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> truncationNotices,
        int warningLimit,
        PluginOutputByteBudget outputBudget)
    {
        if (warningLimit == 0)
        {
            return new List<string>();
        }

        var reservedNoticeCount = Math.Min(truncationNotices.Count, warningLimit);
        var bounded = warnings.Take(warningLimit - reservedNoticeCount).ToList();
        foreach (var notice in truncationNotices.Take(reservedNoticeCount))
        {
            var serializedBytes = JsonSerializer.SerializeToUtf8Bytes(notice, JsonOptions).Length + 1;
            if (serializedBytes <= outputBudget.RemainingBytes && outputBudget.TryConsume(serializedBytes))
            {
                bounded.Add(notice);
            }
        }

        return bounded;
    }

    private static GenerationPluginManifest CopyManifestWithEntryPoint(
        GenerationPluginManifest manifest,
        string stagedEntryPoint)
        => new()
        {
            Capability = manifest.Capability,
            DisplayName = manifest.DisplayName,
            Description = manifest.Description,
            PluginKind = manifest.PluginKind,
            ExecutionMode = manifest.ExecutionMode,
            SourcePath = manifest.SourcePath,
            EntryPoint = stagedEntryPoint,
            LocalDataPaths = manifest.LocalDataPaths,
            Dependencies = manifest.Dependencies,
            Parameters = manifest.Parameters,
            Security = manifest.Security,
            Provenance = manifest.Provenance,
            Metadata = manifest.Metadata
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

    private sealed record HostLaunchSpec(string HostPath, bool UseDotNetHost);
}
