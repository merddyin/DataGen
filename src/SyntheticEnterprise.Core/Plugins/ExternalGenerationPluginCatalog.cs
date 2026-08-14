namespace SyntheticEnterprise.Core.Plugins;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    internal const int MaximumManifestFileBytes = 256 * 1024;
    internal const int MaximumManifestJsonDepth = 64;
    private static readonly Regex QuotedValuePattern = new(@"'(?<value>[^']*)'", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions ManifestJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        MaxDepth = MaximumManifestJsonDepth
    };
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
            if (!Directory.Exists(rootPath)
                || !ExternalPluginPathSecurity.TryValidateNoReparsePoints(rootPath, out _))
            {
                continue;
            }

            foreach (var jsonFile in EnumerateManifestFiles(rootPath, ".generator.json"))
            {
                var manifest = TryReadJsonManifest(jsonFile);
                if (manifest is not null && _validator.Validate(manifest).IsValid && seen.Add($"{manifest.Capability}|{manifest.SourcePath}"))
                {
                    manifests.Add(manifest);
                }
            }

            foreach (var psd1File in EnumerateManifestFiles(rootPath, ".Generator.psd1"))
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
            if (!Directory.Exists(rootPath)
                || !ExternalPluginPathSecurity.TryValidateNoReparsePoints(rootPath, out _))
            {
                continue;
            }

            foreach (var jsonFile in EnumerateManifestFiles(rootPath, ".generator.json"))
            {
                if (seen.Add(jsonFile))
                {
                    var manifest = TryReadJsonManifest(jsonFile, out var rejection);
                    results.Add(InspectManifestFile(jsonFile, "JsonManifest", manifest, rejection, settings));
                }
            }

            foreach (var psd1File in EnumerateManifestFiles(rootPath, ".Generator.psd1"))
            {
                if (seen.Add(psd1File))
                {
                    var manifest = TryReadLegacyManifest(psd1File, out var rejection);
                    results.Add(InspectManifestFile(psd1File, "LegacyManifest", manifest, rejection, settings));
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
        string? parseRejection,
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
                    parseRejection ?? "Plugin manifest could not be parsed."
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
        => TryReadJsonManifest(path, out _);

    private static GenerationPluginManifest? TryReadJsonManifest(string path, out string? rejection)
    {
        try
        {
            var boundedManifest = ReadBoundedManifestText(path);
            var manifest = JsonSerializer.Deserialize<GenerationPluginManifest>(boundedManifest.Text, ManifestJsonOptions);
            var hasExplicitPluginKind = boundedManifest.Text.Contains("\"pluginKind\"", StringComparison.OrdinalIgnoreCase);

            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Capability))
            {
                rejection = "Plugin JSON manifest did not contain a capability.";
                return null;
            }

            var resolvedEntryPoint = ResolveJsonEntryPoint(path, manifest);
            var resolvedLocalDataPaths = ResolveLocalDataPaths(path, manifest.LocalDataPaths);
            var resolvedManifest = new GenerationPluginManifest
            {
                Capability = manifest.Capability,
                DisplayName = manifest.DisplayName,
                Description = manifest.Description,
                PluginKind = hasExplicitPluginKind && !string.IsNullOrWhiteSpace(manifest.PluginKind) ? manifest.PluginKind : "Manifest",
                ExecutionMode = ResolveJsonExecutionMode(manifest),
                SourcePath = path,
                EntryPoint = resolvedEntryPoint,
                LocalDataPaths = resolvedLocalDataPaths,
                Dependencies = manifest.Dependencies,
                Parameters = manifest.Parameters,
                Security = ResolveSecurity(manifest),
                Provenance = new PluginProvenance(),
                Metadata = manifest.Metadata
            };
            if (!ExternalPluginPathSecurity.TryValidateManifestPaths(resolvedManifest, out _))
            {
                rejection = "Plugin JSON manifest paths failed security validation.";
                return null;
            }

            rejection = null;
            return new GenerationPluginManifest
            {
                Capability = resolvedManifest.Capability,
                DisplayName = resolvedManifest.DisplayName,
                Description = resolvedManifest.Description,
                PluginKind = resolvedManifest.PluginKind,
                ExecutionMode = resolvedManifest.ExecutionMode,
                SourcePath = resolvedManifest.SourcePath,
                EntryPoint = resolvedManifest.EntryPoint,
                LocalDataPaths = resolvedManifest.LocalDataPaths,
                Dependencies = resolvedManifest.Dependencies,
                Parameters = resolvedManifest.Parameters,
                Security = resolvedManifest.Security,
                Provenance = BuildProvenance(
                    path,
                    resolvedEntryPoint,
                    resolvedLocalDataPaths,
                    resolvedManifest.ExecutionMode,
                    boundedManifest.Hash),
                Metadata = resolvedManifest.Metadata
            };
        }
        catch (PluginManifestReadException ex)
        {
            rejection = ex.Message;
            return null;
        }
        catch (JsonException ex) when (IsManifestJsonDepthExceeded(ex))
        {
            rejection = $"Plugin JSON manifest exceeded the maximum JSON depth of {MaximumManifestJsonDepth}: {ex.Message}";
            return null;
        }
        catch (JsonException ex)
        {
            rejection = $"Plugin JSON manifest could not be parsed: {ex.Message}";
            return null;
        }
        catch (PluginPathSecurityException ex)
        {
            rejection = ex.Message;
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            rejection = $"Plugin JSON manifest could not be read: {ex.Message}";
            return null;
        }
        catch (Exception ex)
        {
            rejection = $"Plugin JSON manifest could not be parsed: {ex.Message}";
            return null;
        }
    }

    private static bool IsManifestJsonDepthExceeded(JsonException exception)
        => exception.Message.Contains("maximum configured depth", StringComparison.OrdinalIgnoreCase)
           || exception.Message.Contains("maximum depth", StringComparison.OrdinalIgnoreCase);

    private static GenerationPluginManifest? TryReadLegacyManifest(string path)
        => TryReadLegacyManifest(path, out _);

    private static GenerationPluginManifest? TryReadLegacyManifest(string path, out string? rejection)
    {
        try
        {
            var boundedManifest = ReadBoundedManifestText(path);
            var text = boundedManifest.Text;
            var sourceDirectory = Path.GetDirectoryName(path) ?? string.Empty;
            var capability = FirstNonEmpty(
                ReadSingleQuotedAssignment(text, "FriendlyName"),
                ReadSingleQuotedAssignment(text, "FunctionsToExport"),
                Path.GetFileNameWithoutExtension(path).Replace(".Generator", string.Empty, StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(capability))
            {
                rejection = "Legacy plugin manifest did not contain a capability.";
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

            var entryPoint = string.IsNullOrWhiteSpace(rootModule) ? null : Path.GetFullPath(Path.Combine(sourceDirectory, rootModule));
            var resolvedManifest = new GenerationPluginManifest
            {
                Capability = capability,
                DisplayName = capability,
                Description = description ?? string.Empty,
                PluginKind = "LegacyManifest",
                ExecutionMode = PluginExecutionMode.PowerShellScript,
                SourcePath = path,
                EntryPoint = entryPoint,
                Dependencies = dependencies,
                LocalDataPaths = localData,
                Security = BuildDefaultSecurity(localData.Count > 0),
                Provenance = new PluginProvenance(),
                Metadata = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["GeneratorType"] = generatorType,
                    ["ManifestFormat"] = "PowerShellModuleManifest"
                }
            };
            if (!ExternalPluginPathSecurity.TryValidateManifestPaths(resolvedManifest, out _))
            {
                rejection = "Legacy plugin manifest paths failed security validation.";
                return null;
            }

            rejection = null;
            return new GenerationPluginManifest
            {
                Capability = resolvedManifest.Capability,
                DisplayName = resolvedManifest.DisplayName,
                Description = resolvedManifest.Description,
                PluginKind = resolvedManifest.PluginKind,
                ExecutionMode = resolvedManifest.ExecutionMode,
                SourcePath = resolvedManifest.SourcePath,
                EntryPoint = resolvedManifest.EntryPoint,
                LocalDataPaths = resolvedManifest.LocalDataPaths,
                Dependencies = resolvedManifest.Dependencies,
                Parameters = resolvedManifest.Parameters,
                Security = resolvedManifest.Security,
                Provenance = BuildProvenance(
                    path,
                    entryPoint,
                    localData,
                    resolvedManifest.ExecutionMode,
                    boundedManifest.Hash),
                Metadata = resolvedManifest.Metadata
            };
        }
        catch (PluginManifestReadException ex)
        {
            rejection = ex.Message;
            return null;
        }
        catch (PluginPathSecurityException ex)
        {
            rejection = ex.Message;
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            rejection = $"Legacy plugin manifest could not be read: {ex.Message}";
            return null;
        }
        catch (Exception ex)
        {
            rejection = $"Legacy plugin manifest could not be parsed: {ex.Message}";
            return null;
        }
    }

    private static BoundedManifestText ReadBoundedManifestText(string path)
    {
        using var source = ExternalPluginPathSecurity.OpenVerifiedPackageFile(path, path, out var warning)
            ?? throw new PluginManifestReadException(
                $"Plugin manifest failed handle-based path validation: {warning}");
        if (source.Length > MaximumManifestFileBytes)
        {
            throw new PluginManifestReadException(
                $"Plugin manifest exceeded the defensive manifest limit of {MaximumManifestFileBytes} bytes.");
        }

        try
        {
            var budget = new PluginInputByteBudget(MaximumManifestFileBytes);
            using var bounded = new BoundedPluginCatalogReadStream(
                source,
                budget,
                MaximumManifestFileBytes,
                leaveOpen: true);
            using var payload = new MemoryStream();
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[64 * 1024];
            while (true)
            {
                var bytesRead = bounded.Read(buffer, 0, buffer.Length);
                if (bytesRead == 0)
                {
                    break;
                }

                hash.AppendData(buffer, 0, bytesRead);
                payload.Write(buffer, 0, bytesRead);
            }

            payload.Position = 0;
            using var reader = new StreamReader(
                payload,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 4096,
                leaveOpen: true);
            return new BoundedManifestText(
                reader.ReadToEnd(),
                Convert.ToHexString(hash.GetHashAndReset()));
        }
        catch (PluginInputPayloadLimitExceededException)
        {
            throw new PluginManifestReadException(
                $"Plugin manifest exceeded the defensive manifest limit of {MaximumManifestFileBytes} bytes.");
        }
    }

    private sealed class PluginManifestReadException : Exception
    {
        public PluginManifestReadException(string message)
            : base(message)
        {
        }
    }

    private sealed record BoundedManifestText(string Text, string Hash);

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

    private static PluginProvenance BuildProvenance(
        string manifestPath,
        string? entryPointPath,
        IReadOnlyList<string> localDataPaths,
        PluginExecutionMode executionMode,
        string manifestHash)
    {
        var localHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in localDataPaths.Where(File.Exists).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            localHashes[path] = ExternalPluginPathSecurity.ComputeVerifiedPackageFileHash(
                                    manifestPath,
                                    path,
                                    ExternalPluginCatalogLoader.MaximumCatalogFileBytes,
                                    out var localDataWarning)
                                ?? throw new PluginPathSecurityException(localDataWarning!);
        }

        var entryPointHash = !string.IsNullOrWhiteSpace(entryPointPath) && File.Exists(entryPointPath)
            ? ExternalPluginPathSecurity.ComputeVerifiedPackageFileHash(
                manifestPath,
                entryPointPath,
                ExternalPluginPathSecurity.MaximumEntryPointBytes,
                out var entryPointWarning) ?? throw new PluginPathSecurityException(entryPointWarning!)
            : null;

        var packageHash = executionMode == PluginExecutionMode.DotNetAssembly
                          && !string.IsNullOrWhiteSpace(entryPointPath)
            ? FileSystemExternalPluginAssemblyStager.ComputeDiscoveredPackageHash(manifestPath, entryPointPath)
            : null;
        var contentHash = FileSystemExternalPluginAssemblyStager.ComputeApprovedContentHash(
            manifestHash,
            entryPointHash ?? string.Empty,
            localHashes,
            packageHash);

        return new PluginProvenance
        {
            ContentHash = contentHash,
            EntryPointHash = entryPointHash,
            LocalDataHashes = localHashes,
            DiscoveredAtUtc = DateTimeOffset.UtcNow.ToString("O")
        };
    }

    private static IEnumerable<string> EnumerateManifestFiles(string rootPath, string fileNameSuffix)
    {
        var pending = new Stack<string>();
        pending.Push(rootPath);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var file in Directory.EnumerateFiles(current, "*", SearchOption.TopDirectoryOnly))
            {
                if (file.EndsWith(fileNameSuffix, StringComparison.OrdinalIgnoreCase)
                    && ExternalPluginPathSecurity.TryValidateNoReparsePoints(file, out _))
                {
                    yield return file;
                }
            }

            foreach (var directory in Directory.EnumerateDirectories(current, "*", SearchOption.TopDirectoryOnly))
            {
                if (ExternalPluginPathSecurity.TryValidateNoReparsePoints(directory, out _))
                {
                    pending.Push(directory);
                }
            }
        }
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

        if (!ExternalPluginPathSecurity.TryValidateManifestPaths(manifest, out var pathWarning))
        {
            result.Messages.Add(new PluginManifestValidationMessage
            {
                Message = pathWarning!,
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
