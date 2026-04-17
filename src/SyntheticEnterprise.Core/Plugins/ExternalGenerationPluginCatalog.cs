namespace SyntheticEnterprise.Core.Plugins;

using System.Security.Cryptography;
using System.Text.RegularExpressions;
using SyntheticEnterprise.Contracts.Plugins;

public interface IExternalGenerationPluginCatalog
{
    IReadOnlyList<GenerationPluginManifest> Discover(string rootPath);
    IReadOnlyList<GenerationPluginManifest> Discover(IEnumerable<string> rootPaths);
    IReadOnlyList<GenerationPluginInspectionRecord> Inspect(IEnumerable<string> rootPaths, ExternalPluginExecutionSettings settings);
}

public interface IGenerationPluginManifestValidator
{
    PluginManifestValidationResult Validate(GenerationPluginManifest manifest);
}

public interface IGenerationPluginSecurityPolicy
{
    PluginSecurityDecision Evaluate(GenerationPluginManifest manifest);
}

public interface IExternalPluginTrustPolicy
{
    PluginTrustDecision Evaluate(GenerationPluginManifest manifest, ExternalPluginExecutionSettings settings);
}

public interface IGenerationPluginRegistry
{
    IReadOnlyList<GenerationPluginManifest> GetBuiltInManifests();
    IReadOnlyList<GenerationPluginManifest> GetDiscoveredManifests(IEnumerable<string> rootPaths);
    IReadOnlyList<GenerationPluginManifest> GetAllManifests(IEnumerable<string> rootPaths);
}

public sealed class FileSystemExternalGenerationPluginCatalog : IExternalGenerationPluginCatalog
{
    private static readonly Regex QuotedValuePattern = new(@"'(?<value>[^']*)'", RegexOptions.Compiled);
    private readonly IGenerationPluginManifestValidator _validator;
    private readonly IGenerationPluginSecurityPolicy _securityPolicy;
    private readonly IExternalPluginTrustPolicy _trustPolicy;

    public FileSystemExternalGenerationPluginCatalog(
        IGenerationPluginManifestValidator validator,
        IGenerationPluginSecurityPolicy securityPolicy,
        IExternalPluginTrustPolicy trustPolicy)
    {
        _validator = validator;
        _securityPolicy = securityPolicy;
        _trustPolicy = trustPolicy;
    }

    public IReadOnlyList<GenerationPluginManifest> Discover(string rootPath)
        => Discover(new[] { rootPath });

    public IReadOnlyList<GenerationPluginManifest> Discover(IEnumerable<string> rootPaths)
    {
        var manifests = new List<GenerationPluginManifest>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rootPath in rootPaths.Where(path => !string.IsNullOrWhiteSpace(path)).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(rootPath))
            {
                continue;
            }

            foreach (var jsonFile in Directory.EnumerateFiles(rootPath, "*.generator.json", SearchOption.AllDirectories))
            {
                var manifest = TryReadJsonManifest(jsonFile);
                if (manifest is not null && _validator.Validate(manifest).IsValid && seen.Add($"{manifest.Capability}|{manifest.SourcePath}"))
                {
                    manifests.Add(manifest);
                }
            }

            foreach (var psd1File in Directory.EnumerateFiles(rootPath, "*.Generator.psd1", SearchOption.AllDirectories))
            {
                var manifest = TryReadLegacyManifest(psd1File);
                if (manifest is not null && _validator.Validate(manifest).IsValid && seen.Add($"{manifest.Capability}|{manifest.SourcePath}"))
                {
                    manifests.Add(manifest);
                }
            }
        }

        return manifests
            .OrderBy(manifest => manifest.Capability, StringComparer.OrdinalIgnoreCase)
            .ThenBy(manifest => manifest.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<GenerationPluginInspectionRecord> Inspect(IEnumerable<string> rootPaths, ExternalPluginExecutionSettings settings)
    {
        var results = new List<GenerationPluginInspectionRecord>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rootPath in rootPaths.Where(path => !string.IsNullOrWhiteSpace(path)).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(rootPath))
            {
                continue;
            }

            foreach (var jsonFile in Directory.EnumerateFiles(rootPath, "*.generator.json", SearchOption.AllDirectories))
            {
                if (seen.Add(jsonFile))
                {
                    results.Add(InspectManifestFile(jsonFile, "JsonManifest", TryReadJsonManifest(jsonFile), settings));
                }
            }

            foreach (var psd1File in Directory.EnumerateFiles(rootPath, "*.Generator.psd1", SearchOption.AllDirectories))
            {
                if (seen.Add(psd1File))
                {
                    results.Add(InspectManifestFile(psd1File, "LegacyManifest", TryReadLegacyManifest(psd1File), settings));
                }
            }
        }

        return results
            .OrderBy(item => item.Capability, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private GenerationPluginInspectionRecord InspectManifestFile(
        string sourcePath,
        string sourceType,
        GenerationPluginManifest? manifest,
        ExternalPluginExecutionSettings settings)
    {
        if (manifest is null)
        {
            return new GenerationPluginInspectionRecord
            {
                SourcePath = sourcePath,
                SourceType = sourceType,
                Capability = Path.GetFileNameWithoutExtension(sourcePath),
                DisplayName = Path.GetFileNameWithoutExtension(sourcePath),
                PluginKind = "Unknown",
                Parsed = false,
                Valid = false,
                SecurityAllowed = false,
                Trusted = false,
                EligibleForActivation = false,
                ValidationMessages = new()
                {
                    "Plugin manifest could not be parsed."
                }
            };
        }

        var validation = _validator.Validate(manifest);
        var security = _securityPolicy.Evaluate(manifest);
        var trust = _trustPolicy.Evaluate(manifest, settings);
        var securityMessages = security.Allowed
            ? new List<string>()
            : security.DeniedReasons.ToList();
        var trustMessages = trust.Allowed
            ? new List<string>()
            : trust.Reasons.ToList();
        var validationMessages = validation.Messages
            .Select(message => message.Message)
            .ToList();

        return new GenerationPluginInspectionRecord
        {
            SourcePath = sourcePath,
            SourceType = sourceType,
            Capability = manifest.Capability,
            DisplayName = manifest.DisplayName,
            PluginKind = manifest.PluginKind,
            ExecutionMode = manifest.ExecutionMode,
            EntryPoint = manifest.EntryPoint,
            ContentHash = manifest.Provenance.ContentHash,
            EntryPointHash = manifest.Provenance.EntryPointHash,
            LocalDataHashCount = manifest.Provenance.LocalDataHashes.Count,
            HasCompleteProvenance = FileSystemExternalGenerationPluginCatalog_Helpers.HasCompleteProvenance(manifest),
            Parsed = true,
            Valid = validation.IsValid,
            SecurityAllowed = security.Allowed,
            Trusted = trust.Allowed,
            EligibleForActivation = validation.IsValid && security.Allowed && trust.Allowed,
            RequiresAssemblyOptIn = manifest.ExecutionMode == PluginExecutionMode.DotNetAssembly,
            RequiresHashApproval = settings.RequireContentHashAllowList
                || (manifest.ExecutionMode == PluginExecutionMode.DotNetAssembly && settings.RequireAssemblyHashApproval),
            ValidationMessages = validationMessages,
            SecurityMessages = securityMessages,
            TrustMessages = trustMessages,
            RequestedCapabilities = manifest.Security.RequestedCapabilities.ToList(),
            GrantedCapabilities = security.GrantedCapabilities.ToList(),
            Dependencies = manifest.Dependencies.ToList(),
            Parameters = manifest.Parameters.ToList(),
            Metadata = new Dictionary<string, string?>(manifest.Metadata, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static GenerationPluginManifest? TryReadJsonManifest(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            var manifest = System.Text.Json.JsonSerializer.Deserialize<GenerationPluginManifest>(json, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            var hasExplicitPluginKind = json.Contains("\"pluginKind\"", StringComparison.OrdinalIgnoreCase);

            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Capability))
            {
                return null;
            }

            return new GenerationPluginManifest
            {
                Capability = manifest.Capability,
                DisplayName = manifest.DisplayName,
                Description = manifest.Description,
                PluginKind = hasExplicitPluginKind && !string.IsNullOrWhiteSpace(manifest.PluginKind) ? manifest.PluginKind : "Manifest",
                ExecutionMode = ResolveJsonExecutionMode(manifest),
                SourcePath = path,
                EntryPoint = ResolveJsonEntryPoint(path, manifest),
                LocalDataPaths = ResolveLocalDataPaths(path, manifest.LocalDataPaths),
                Dependencies = manifest.Dependencies,
                Parameters = manifest.Parameters,
                Security = ResolveSecurity(manifest),
                Provenance = BuildProvenance(path, ResolveJsonEntryPoint(path, manifest), ResolveLocalDataPaths(path, manifest.LocalDataPaths)),
                Metadata = manifest.Metadata
            };
        }
        catch
        {
            return null;
        }
    }

    private static GenerationPluginManifest? TryReadLegacyManifest(string path)
    {
        try
        {
            var text = File.ReadAllText(path);
            var sourceDirectory = Path.GetDirectoryName(path) ?? string.Empty;
            var capability = FirstNonEmpty(
                ReadSingleQuotedAssignment(text, "FriendlyName"),
                ReadSingleQuotedAssignment(text, "FunctionsToExport"),
                Path.GetFileNameWithoutExtension(path).Replace(".Generator", string.Empty, StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(capability))
            {
                return null;
            }

            var rootModule = ReadSingleQuotedAssignment(text, "RootModule");
            var description = FirstNonEmpty(
                ReadHereStringAssignment(text, "Description"),
                ReadSingleQuotedAssignment(text, "Description"));
            var generatorType = ReadSingleQuotedAssignment(text, "GeneratorType");
            var dependencies = ReadArrayAssignment(text, "DependsOn")
                .Select(TrimLegacyDependency)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var localData = ReadArrayAssignment(text, "LocalDataFiles")
                .Select(value => Path.GetFullPath(Path.Combine(sourceDirectory, value.Replace('/', Path.DirectorySeparatorChar))))
                .ToList();

            return new GenerationPluginManifest
            {
                Capability = capability,
                DisplayName = capability,
                Description = description ?? string.Empty,
                PluginKind = "LegacyManifest",
                ExecutionMode = PluginExecutionMode.PowerShellScript,
                SourcePath = path,
                EntryPoint = string.IsNullOrWhiteSpace(rootModule) ? null : Path.GetFullPath(Path.Combine(sourceDirectory, rootModule)),
                Dependencies = dependencies,
                LocalDataPaths = localData,
                Security = BuildDefaultSecurity(localData.Count > 0),
                Provenance = BuildProvenance(path, string.IsNullOrWhiteSpace(rootModule) ? null : Path.GetFullPath(Path.Combine(sourceDirectory, rootModule)), localData),
                Metadata = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["GeneratorType"] = generatorType,
                    ["ManifestFormat"] = "PowerShellModuleManifest"
                }
            };
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadSingleQuotedAssignment(string text, string key)
    {
        var match = Regex.Match(text, $@"^\s*{Regex.Escape(key)}\s*=\s*'(?<value>[^']*)'", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["value"].Value.Trim() : null;
    }

    private static string? ReadHereStringAssignment(string text, string key)
    {
        var match = Regex.Match(text, $@"^\s*{Regex.Escape(key)}\s*=\s*@(?<quote>['""])(?<value>.*?)(?:\k<quote>)@", RegexOptions.Singleline | RegexOptions.Multiline | RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["value"].Value.Trim() : null;
    }

    private static IReadOnlyList<string> ReadArrayAssignment(string text, string key)
    {
        var match = Regex.Match(text, $@"^\s*{Regex.Escape(key)}\s*=\s*@\((?<value>.*?)\)", RegexOptions.Singleline | RegexOptions.Multiline | RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return Array.Empty<string>();
        }

        return QuotedValuePattern
            .Matches(match.Groups["value"].Value)
            .Select(result => result.Groups["value"].Value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
    }

    private static string TrimLegacyDependency(string value)
        => value.Replace(".Generator", string.Empty, StringComparison.OrdinalIgnoreCase);

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static PluginExecutionMode ResolveJsonExecutionMode(GenerationPluginManifest manifest)
    {
        if (manifest.ExecutionMode != PluginExecutionMode.InProcess)
        {
            return manifest.ExecutionMode;
        }

        if (string.IsNullOrWhiteSpace(manifest.EntryPoint))
        {
            return PluginExecutionMode.MetadataOnly;
        }

        return Path.GetExtension(manifest.EntryPoint).ToLowerInvariant() switch
        {
            ".ps1" or ".psm1" => PluginExecutionMode.PowerShellScript,
            ".dll" => PluginExecutionMode.DotNetAssembly,
            _ => PluginExecutionMode.MetadataOnly
        };
    }

    private static string? ResolveJsonEntryPoint(string manifestPath, GenerationPluginManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.EntryPoint))
        {
            return null;
        }

        var manifestDirectory = Path.GetDirectoryName(manifestPath) ?? string.Empty;
        return Path.GetFullPath(Path.Combine(manifestDirectory, manifest.EntryPoint));
    }

    private static List<string> ResolveLocalDataPaths(string manifestPath, IReadOnlyList<string> localDataPaths)
    {
        var manifestDirectory = Path.GetDirectoryName(manifestPath) ?? string.Empty;
        return localDataPaths
            .Select(path => Path.GetFullPath(Path.Combine(manifestDirectory, path.Replace('/', Path.DirectorySeparatorChar))))
            .ToList();
    }

    private static PluginSecurityProfile ResolveSecurity(GenerationPluginManifest manifest)
    {
        var requestedCapabilities = manifest.Security.RequestedCapabilities.Count > 0
            ? manifest.Security.RequestedCapabilities.ToList()
            : BuildDefaultSecurity(manifest.LocalDataPaths.Count > 0).RequestedCapabilities;

        return new PluginSecurityProfile
        {
            DataOnly = manifest.Security.DataOnly,
            RequestedCapabilities = requestedCapabilities
        };
    }

    private static PluginSecurityProfile BuildDefaultSecurity(bool hasLocalData)
    {
        var capabilities = new List<PluginRuntimeCapability>
        {
            PluginRuntimeCapability.GenerateData
        };

        if (hasLocalData)
        {
            capabilities.Add(PluginRuntimeCapability.ReadPluginData);
        }

        return new PluginSecurityProfile
        {
            DataOnly = true,
            RequestedCapabilities = capabilities
        };
    }

    private static PluginProvenance BuildProvenance(string manifestPath, string? entryPointPath, IReadOnlyList<string> localDataPaths)
    {
        var localHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in localDataPaths.Where(File.Exists).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            localHashes[path] = ComputeFileHash(path);
        }

        var combinedMaterial = string.Join("|", new[]
        {
            File.Exists(manifestPath) ? ComputeFileHash(manifestPath) : string.Empty,
            !string.IsNullOrWhiteSpace(entryPointPath) && File.Exists(entryPointPath) ? ComputeFileHash(entryPointPath) : string.Empty,
            string.Join("|", localHashes.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase).Select(entry => $"{entry.Key}:{entry.Value}"))
        });

        return new PluginProvenance
        {
            ContentHash = ComputeStringHash(combinedMaterial),
            EntryPointHash = !string.IsNullOrWhiteSpace(entryPointPath) && File.Exists(entryPointPath) ? ComputeFileHash(entryPointPath) : null,
            LocalDataHashes = localHashes,
            DiscoveredAtUtc = DateTimeOffset.UtcNow.ToString("O")
        };
    }

    private static string ComputeFileHash(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    private static string ComputeStringHash(string value)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(value)));
    }
}

public sealed class GenerationPluginManifestValidator : IGenerationPluginManifestValidator
{
    private readonly IGenerationPluginSecurityPolicy _securityPolicy;

    public GenerationPluginManifestValidator(IGenerationPluginSecurityPolicy securityPolicy)
    {
        _securityPolicy = securityPolicy;
    }

    public PluginManifestValidationResult Validate(GenerationPluginManifest manifest)
    {
        var result = new PluginManifestValidationResult
        {
            Manifest = manifest
        };

        if (string.IsNullOrWhiteSpace(manifest.Capability))
        {
            result.Messages.Add(new PluginManifestValidationMessage
            {
                Message = "Capability is required.",
                IsError = true
            });
        }

        switch (manifest.ExecutionMode)
        {
            case PluginExecutionMode.InProcess:
                break;
            case PluginExecutionMode.MetadataOnly:
                if (!string.IsNullOrWhiteSpace(manifest.EntryPoint))
                {
                    result.Messages.Add(new PluginManifestValidationMessage
                    {
                        Message = "Metadata-only plugins must not declare an entry point.",
                        IsError = true
                    });
                }
                break;
            case PluginExecutionMode.PowerShellScript:
                ValidateEntryPoint(manifest, new[] { ".ps1", ".psm1" }, result);
                break;
            case PluginExecutionMode.DotNetAssembly:
                ValidateEntryPoint(manifest, new[] { ".dll" }, result);
                break;
        }

        foreach (var localDataPath in manifest.LocalDataPaths)
        {
            if (!File.Exists(localDataPath))
            {
                result.Messages.Add(new PluginManifestValidationMessage
                {
                    Message = $"Local data path not found: {localDataPath}",
                    IsError = true
                });
            }

            if (!IsWithinPluginRoot(manifest.SourcePath, localDataPath))
            {
                result.Messages.Add(new PluginManifestValidationMessage
                {
                    Message = $"Local data path '{localDataPath}' must stay within the plugin package root.",
                    IsError = true
                });
            }
        }

        var securityDecision = _securityPolicy.Evaluate(manifest);
        foreach (var deniedReason in securityDecision.DeniedReasons)
        {
            result.Messages.Add(new PluginManifestValidationMessage
            {
                Message = deniedReason,
                IsError = true
            });
        }

        return result;
    }

    private static void ValidateEntryPoint(GenerationPluginManifest manifest, IReadOnlyCollection<string> allowedExtensions, PluginManifestValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(manifest.EntryPoint))
        {
            result.Messages.Add(new PluginManifestValidationMessage
            {
                Message = "An entry point is required for this execution mode.",
                IsError = true
            });
            return;
        }

        var extension = Path.GetExtension(manifest.EntryPoint);
        if (!allowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            result.Messages.Add(new PluginManifestValidationMessage
            {
                Message = $"Entry point '{manifest.EntryPoint}' is not a supported file type for execution mode '{manifest.ExecutionMode}'.",
                IsError = true
            });
        }

        if (!File.Exists(manifest.EntryPoint))
        {
            result.Messages.Add(new PluginManifestValidationMessage
            {
                Message = $"Entry point not found: {manifest.EntryPoint}",
                IsError = true
            });
        }

        if (!IsWithinPluginRoot(manifest.SourcePath, manifest.EntryPoint))
        {
            result.Messages.Add(new PluginManifestValidationMessage
            {
                Message = $"Entry point '{manifest.EntryPoint}' must stay within the plugin package root.",
                IsError = true
            });
        }
    }

    private static bool IsWithinPluginRoot(string? sourcePath, string? candidatePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(candidatePath))
        {
            return true;
        }

        var pluginRoot = Path.GetDirectoryName(Path.GetFullPath(sourcePath));
        if (string.IsNullOrWhiteSpace(pluginRoot))
        {
            return true;
        }

        var normalizedRoot = EnsureTrailingSeparator(Path.GetFullPath(pluginRoot));
        var normalizedCandidate = Path.GetFullPath(candidatePath);
        return normalizedCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureTrailingSeparator(string path)
        => path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
}

public sealed class DataOnlyGenerationPluginSecurityPolicy : IGenerationPluginSecurityPolicy
{
    private static readonly HashSet<PluginRuntimeCapability> SafeCapabilities = new()
    {
        PluginRuntimeCapability.GenerateData,
        PluginRuntimeCapability.ReadPluginData,
        PluginRuntimeCapability.EmitDiagnostics
    };

    public PluginSecurityDecision Evaluate(GenerationPluginManifest manifest)
    {
        var deniedReasons = new List<string>();
        var granted = new List<PluginRuntimeCapability>();

        var requested = manifest.Security.RequestedCapabilities.Count == 0
            ? new[] { PluginRuntimeCapability.GenerateData }
            : manifest.Security.RequestedCapabilities.AsEnumerable();

        if (!manifest.Security.DataOnly && !string.Equals(manifest.PluginKind, "BuiltIn", StringComparison.OrdinalIgnoreCase))
        {
            deniedReasons.Add("External plugins must be declared as data-only.");
        }

        foreach (var capability in requested.Distinct())
        {
            if (SafeCapabilities.Contains(capability))
            {
                granted.Add(capability);
                continue;
            }

            if (!string.Equals(manifest.PluginKind, "BuiltIn", StringComparison.OrdinalIgnoreCase))
            {
                deniedReasons.Add($"Capability '{capability}' is not allowed for external data-generation plugins.");
                continue;
            }

            granted.Add(capability);
        }

        return new PluginSecurityDecision
        {
            Manifest = manifest,
            Allowed = deniedReasons.Count == 0,
            GrantedCapabilities = granted,
            DeniedReasons = deniedReasons
        };
    }
}

public sealed class AllowListExternalPluginTrustPolicy : IExternalPluginTrustPolicy
{
    public PluginTrustDecision Evaluate(GenerationPluginManifest manifest, ExternalPluginExecutionSettings settings)
    {
        if (manifest.ExecutionMode == PluginExecutionMode.DotNetAssembly && !settings.AllowAssemblyPlugins)
        {
            return new PluginTrustDecision
            {
                Manifest = manifest,
                Allowed = false,
                Reasons = new()
                {
                    "DotNetAssembly plugins require explicit AllowAssemblyPlugins opt-in."
                }
            };
        }

        var requireAllowList = settings.RequireContentHashAllowList
            || (manifest.ExecutionMode == PluginExecutionMode.DotNetAssembly && settings.RequireAssemblyHashApproval);

        if (manifest.ExecutionMode == PluginExecutionMode.DotNetAssembly && !HasCompleteProvenance(manifest))
        {
            return new PluginTrustDecision
            {
                Manifest = manifest,
                Allowed = false,
                Reasons = new()
                {
                    "DotNetAssembly plugins must include complete entry point and local data provenance before they can be trusted."
                }
            };
        }

        if (!requireAllowList)
        {
            return new PluginTrustDecision
            {
                Manifest = manifest,
                Allowed = true
            };
        }

        var contentHash = manifest.Provenance.ContentHash;
        if (string.IsNullOrWhiteSpace(contentHash))
        {
            return new PluginTrustDecision
            {
                Manifest = manifest,
                Allowed = false,
                Reasons = new()
                {
                    "Plugin content hash is unavailable, so trust requirements cannot be satisfied."
                }
            };
        }

        var allowList = new HashSet<string>(settings.AllowedContentHashes, StringComparer.OrdinalIgnoreCase);
        return new PluginTrustDecision
        {
            Manifest = manifest,
            Allowed = allowList.Contains(contentHash),
            Reasons = allowList.Contains(contentHash)
                ? new()
                : new()
                {
                    $"Plugin content hash '{contentHash}' is not in the allowed hash list."
                }
        };
    }

    private static bool HasCompleteProvenance(GenerationPluginManifest manifest)
        => FileSystemExternalGenerationPluginCatalog_Helpers.HasCompleteProvenance(manifest);
}

internal static class FileSystemExternalGenerationPluginCatalog_Helpers
{
    internal static bool HasCompleteProvenance(GenerationPluginManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.Provenance.ContentHash))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(manifest.EntryPoint)
            && manifest.ExecutionMode != PluginExecutionMode.MetadataOnly
            && string.IsNullOrWhiteSpace(manifest.Provenance.EntryPointHash))
        {
            return false;
        }

        return manifest.LocalDataPaths.All(path =>
            string.IsNullOrWhiteSpace(path)
            || manifest.Provenance.LocalDataHashes.ContainsKey(path));
    }
}

public sealed class GenerationPluginRegistry : IGenerationPluginRegistry
{
    private readonly IEnumerable<IWorldGenerationPlugin> _builtInPlugins;
    private readonly IExternalGenerationPluginCatalog _externalCatalog;

    public GenerationPluginRegistry(IEnumerable<IWorldGenerationPlugin> builtInPlugins, IExternalGenerationPluginCatalog externalCatalog)
    {
        _builtInPlugins = builtInPlugins;
        _externalCatalog = externalCatalog;
    }

    public IReadOnlyList<GenerationPluginManifest> GetBuiltInManifests()
        => _builtInPlugins
            .Select(plugin => plugin.Manifest)
            .OrderBy(manifest => manifest.Capability, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public IReadOnlyList<GenerationPluginManifest> GetDiscoveredManifests(IEnumerable<string> rootPaths)
        => _externalCatalog.Discover(rootPaths);

    public IReadOnlyList<GenerationPluginManifest> GetAllManifests(IEnumerable<string> rootPaths)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<GenerationPluginManifest>();

        foreach (var manifest in GetBuiltInManifests().Concat(GetDiscoveredManifests(rootPaths)))
        {
            if (seen.Add($"{manifest.PluginKind}:{manifest.Capability}:{manifest.SourcePath}"))
            {
                results.Add(manifest);
            }
        }

        return results;
    }
}
