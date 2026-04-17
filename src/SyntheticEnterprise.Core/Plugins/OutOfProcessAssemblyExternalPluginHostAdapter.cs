namespace SyntheticEnterprise.Core.Plugins;

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Contracts.Plugins;

public sealed class OutOfProcessAssemblyExternalPluginHostAdapter : IExternalPluginHostAdapter
{
    private const string HostAssemblyName = "SyntheticEnterprise.PluginHost.dll";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

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

        var hostPath = ResolveHostPath();
        if (hostPath is null)
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

        var tempRoot = Path.Combine(Path.GetTempPath(), $"datagen-assembly-host-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var requestPath = Path.Combine(tempRoot, "request.json");
            var responsePath = Path.Combine(tempRoot, "response.json");
            var request = new ExternalPluginExecutionRequest
            {
                Manifest = manifest,
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
                PluginCatalogs = ExternalPluginCatalogLoader.LoadPluginCatalogs(manifest)
            };
            var requestBytes = JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions);
            if (requestBytes.Length > Math.Max(1024, context.ExternalPlugins.MaxInputPayloadBytes))
            {
                return new ExternalPluginExecutionResult
                {
                    Manifest = manifest,
                    Executed = false,
                    Warnings = new()
                    {
                        $"Input payload exceeded the configured limit of {context.ExternalPlugins.MaxInputPayloadBytes} bytes."
                    }
                };
            }

            File.WriteAllBytes(requestPath, requestBytes);

            var processStartInfo = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = tempRoot
            };
            processStartInfo.ArgumentList.Add("exec");
            processStartInfo.ArgumentList.Add(hostPath);
            processStartInfo.ArgumentList.Add("--request");
            processStartInfo.ArgumentList.Add(requestPath);
            processStartInfo.ArgumentList.Add("--response");
            processStartInfo.ArgumentList.Add(responsePath);
            processStartInfo.Environment["DOTNET_EnableDiagnostics"] = "0";
            processStartInfo.Environment["COMPlus_EnableDiagnostics"] = "0";
            processStartInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

            using var process = Process.Start(processStartInfo);
            if (process is null)
            {
                return new ExternalPluginExecutionResult
                {
                    Manifest = manifest,
                    Executed = false,
                    Warnings = new()
                    {
                        "DotNetAssembly external plugin host could not be started."
                    }
                };
            }

            var stdOutTask = process.StandardOutput.ReadToEndAsync();
            var stdErrTask = process.StandardError.ReadToEndAsync();
            var timeout = TimeSpan.FromSeconds(Math.Max(1, context.ExternalPlugins.ExecutionTimeoutSeconds));
            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
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
                        $"Assembly host timed out after {timeout.TotalSeconds:0} seconds."
                    }
                };
            }

            var stdOut = stdOutTask.GetAwaiter().GetResult();
            var stdErr = stdErrTask.GetAwaiter().GetResult();
            if (File.Exists(responsePath) && new FileInfo(responsePath).Length > Math.Max(1024, context.ExternalPlugins.MaxOutputPayloadBytes))
            {
                return new ExternalPluginExecutionResult
                {
                    Manifest = manifest,
                    Executed = false,
                    Warnings = new()
                    {
                        $"Plugin output exceeded the configured limit of {context.ExternalPlugins.MaxOutputPayloadBytes} bytes."
                    }
                };
            }

            var response = File.Exists(responsePath)
                ? JsonSerializer.Deserialize<ExternalPluginExecutionResponse>(File.ReadAllText(responsePath), JsonOptions)
                : null;

            var warnings = response?.Warnings?.ToList() ?? new List<string>();
            warnings = warnings
                .Select(warning => LimitDiagnostic(warning, context.ExternalPlugins))
                .ToList();

            if (!string.IsNullOrWhiteSpace(stdOut))
            {
                warnings.Add(LimitDiagnostic($"[stdout] {stdOut.Trim()}", context.ExternalPlugins));
            }

            if (!string.IsNullOrWhiteSpace(stdErr))
            {
                warnings.Add(LimitDiagnostic($"[stderr] {stdErr.Trim()}", context.ExternalPlugins));
            }

            warnings = warnings
                .Take(Math.Max(0, context.ExternalPlugins.MaxDiagnosticEntries))
                .ToList();

            if (process.ExitCode != 0 && string.IsNullOrWhiteSpace(stdErr))
            {
                warnings.Add(LimitDiagnostic($"Assembly host exited with code {process.ExitCode}.", context.ExternalPlugins));
            }

            var boundedRecords = (response?.Records ?? new List<PluginGeneratedRecord>())
                .Take(Math.Max(0, context.ExternalPlugins.MaxGeneratedRecords))
                .ToList();
            var boundedWarnings = warnings
                .Take(Math.Max(0, context.ExternalPlugins.MaxWarningCount))
                .ToList();

            if ((response?.Records?.Count ?? 0) > boundedRecords.Count)
            {
                boundedWarnings.Add($"Generated records were truncated from {response!.Records.Count} to {boundedRecords.Count}.");
            }

            if (warnings.Count > boundedWarnings.Count)
            {
                boundedWarnings.Add($"Plugin warnings were truncated from {warnings.Count} to {boundedWarnings.Count}.");
            }

            return new ExternalPluginExecutionResult
            {
                Manifest = manifest,
                Executed = response?.Executed == true,
                Records = boundedRecords,
                Warnings = boundedWarnings
            };
        }
        catch (Exception ex)
        {
            return new ExternalPluginExecutionResult
            {
                Manifest = manifest,
                Executed = false,
                Warnings = new()
                {
                    $"DotNetAssembly host execution failed: {ex.Message}"
                }
            };
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static string? ResolveHostPath()
    {
        var searchRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var startPath in new[]
                 {
                     AppContext.BaseDirectory,
                     Path.GetDirectoryName(typeof(OutOfProcessAssemblyExternalPluginHostAdapter).Assembly.Location) ?? string.Empty
                 }.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            var directCandidate = Path.Combine(startPath, "plugin-host", HostAssemblyName);
            if (File.Exists(directCandidate))
            {
                return directCandidate;
            }

            foreach (var root in EnumerateSelfAndParents(startPath))
            {
                if (!searchRoots.Add(root))
                {
                    continue;
                }

                foreach (var configuration in new[] { "Debug", "Release" })
                {
                    var sourceCandidate = Path.Combine(root, "src", "SyntheticEnterprise.PluginHost", "bin", configuration, "net8.0", HostAssemblyName);
                    if (File.Exists(sourceCandidate))
                    {
                        return sourceCandidate;
                    }
                }
            }
        }

        return null;
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

        var currentEntryPointHash = ComputeFileHash(manifest.EntryPoint);
        if (!string.Equals(currentEntryPointHash, manifest.Provenance.EntryPointHash, StringComparison.OrdinalIgnoreCase))
        {
            warning = "Assembly plugin entry point hash no longer matches discovered provenance.";
            return false;
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

            var currentHash = ComputeFileHash(localDataPath);
            if (!string.Equals(currentHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                warning = $"Assembly plugin local data hash no longer matches discovered provenance for '{localDataPath}'.";
                return false;
            }
        }

        warning = null;
        return true;
    }

    private static string ComputeFileHash(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    private static Dictionary<string, string?> ResolvePluginSettings(ExternalPluginExecutionSettings settings, string capability)
        => settings.CapabilityConfigurations
            .FirstOrDefault(configuration => string.Equals(configuration.Capability, capability, StringComparison.OrdinalIgnoreCase))
            ?.Settings is { } configurationSettings
            ? new Dictionary<string, string?>(configurationSettings, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    private static string LimitDiagnostic(string message, ExternalPluginExecutionSettings settings)
    {
        var maxCharacters = Math.Max(32, settings.MaxDiagnosticCharacters);
        if (string.IsNullOrWhiteSpace(message) || message.Length <= maxCharacters)
        {
            return message;
        }

        return $"{message[..maxCharacters]}...(truncated)";
    }
}
