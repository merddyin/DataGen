namespace SyntheticEnterprise.Contracts.Plugins;

using System.Text.Json.Serialization;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PluginExecutionMode
{
    InProcess,
    PowerShellScript,
    DotNetAssembly,
    MetadataOnly
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PluginRuntimeCapability
{
    GenerateData,
    ReadPluginData,
    EmitDiagnostics,
    WriteFiles,
    InvokeNetwork,
    StartProcess,
    ModifyEnvironment,
    ExecuteArbitraryCommands
}

public sealed class PluginParameterDescriptor
{
    public required string Name { get; init; }
    public required string TypeName { get; init; }
    public string? HelpText { get; init; }
    public bool Required { get; init; }
    public object? DefaultValue { get; init; }
}

public sealed class GenerationPluginManifest
{
    public required string Capability { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string PluginKind { get; init; } = "BuiltIn";
    public PluginExecutionMode ExecutionMode { get; init; } = PluginExecutionMode.InProcess;
    public string? SourcePath { get; init; }
    public string? EntryPoint { get; init; }
    public List<string> LocalDataPaths { get; init; } = new();
    public List<string> Dependencies { get; init; } = new();
    public List<PluginParameterDescriptor> Parameters { get; init; } = new();
    public PluginSecurityProfile Security { get; init; } = new();
    public PluginProvenance Provenance { get; init; } = new();
    public Dictionary<string, string?> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class GenerationPluginExecutionPlan
{
    public List<GenerationPluginManifest> ActivePlugins { get; init; } = new();
}

public sealed class GenerationPluginCapabilityContribution
{
    public required string Capability { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public PluginExecutionMode ExecutionMode { get; init; } = PluginExecutionMode.MetadataOnly;
    public string PluginKind { get; init; } = string.Empty;
    public List<string> Dependencies { get; init; } = new();
    public List<PluginParameterDescriptor> Parameters { get; init; } = new();
    public Dictionary<string, string?> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<PluginRuntimeCapability> RequestedCapabilities { get; init; } = new();
}

public sealed class ExternalPluginCapabilityPlan
{
    public List<GenerationPluginManifest> ActivePlugins { get; init; } = new();
    public List<GenerationPluginCapabilityContribution> Contributions { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
}

public sealed class PluginManifestValidationMessage
{
    public required string Message { get; init; }
    public bool IsError { get; init; }
}

public sealed class PluginManifestValidationResult
{
    public required GenerationPluginManifest Manifest { get; init; }
    public List<PluginManifestValidationMessage> Messages { get; init; } = new();
    public bool IsValid => Messages.All(message => !message.IsError);
}

public sealed class PluginSecurityProfile
{
    public bool DataOnly { get; init; } = true;
    public List<PluginRuntimeCapability> RequestedCapabilities { get; init; } = new();
}

public sealed class PluginSecurityDecision
{
    public required GenerationPluginManifest Manifest { get; init; }
    public bool Allowed { get; init; }
    public List<PluginRuntimeCapability> GrantedCapabilities { get; init; } = new();
    public List<string> DeniedReasons { get; init; } = new();
}

public sealed class ExternalPluginCapabilityConfiguration
{
    public required string Capability { get; init; }
    public Dictionary<string, string?> Settings { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ScenarioPackSelection
{
    public required string PackId { get; init; }
    public bool Enabled { get; init; } = true;
    public Dictionary<string, string?> Settings { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ScenarioPackProfile
{
    public string DiscoveryMode { get; init; } = "Bundled";
    public bool IncludeBundledPacks { get; init; } = true;
    public List<string> PackRootPaths { get; init; } = new();
    public List<ScenarioPackSelection> EnabledPacks { get; init; } = new();
}

public sealed class ExternalPluginScenarioProfile
{
    public List<string> PluginRootPaths { get; init; } = new();
    public List<string> EnabledCapabilities { get; init; } = new();
    public List<ExternalPluginCapabilityConfiguration> CapabilityConfigurations { get; init; } = new();
    public bool? AllowAssemblyPlugins { get; init; }
    public int? ExecutionTimeoutSeconds { get; init; }
    public int? MaxGeneratedRecords { get; init; }
    public int? MaxWarningCount { get; init; }
    public int? MaxDiagnosticEntries { get; init; }
    public int? MaxDiagnosticCharacters { get; init; }
    public int? MaxInputPayloadBytes { get; init; }
    public int? MaxOutputPayloadBytes { get; init; }
    public bool? RequireContentHashAllowList { get; init; }
    public bool? RequireAssemblyHashApproval { get; init; }
    public List<string> AllowedContentHashes { get; init; } = new();
}

public sealed class ExternalPluginExecutionOverrides
{
    public List<string>? PluginRootPaths { get; init; }
    public List<string>? EnabledCapabilities { get; init; }
    public List<ExternalPluginCapabilityConfiguration>? CapabilityConfigurations { get; init; }
    public bool? AllowAssemblyPlugins { get; init; }
    public int? ExecutionTimeoutSeconds { get; init; }
    public int? MaxGeneratedRecords { get; init; }
    public int? MaxWarningCount { get; init; }
    public int? MaxDiagnosticEntries { get; init; }
    public int? MaxDiagnosticCharacters { get; init; }
    public int? MaxInputPayloadBytes { get; init; }
    public int? MaxOutputPayloadBytes { get; init; }
    public bool? RequireContentHashAllowList { get; init; }
    public bool? RequireAssemblyHashApproval { get; init; }
    public List<string>? AllowedContentHashes { get; init; }
}

public sealed class ExternalPluginExecutionSettings
{
    public bool Enabled { get; init; }
    public List<string> PluginRootPaths { get; init; } = new();
    public List<string> EnabledCapabilities { get; init; } = new();
    public List<ExternalPluginCapabilityConfiguration> CapabilityConfigurations { get; init; } = new();
    public bool AllowAssemblyPlugins { get; init; }
    public int ExecutionTimeoutSeconds { get; init; } = 10;
    public int MaxGeneratedRecords { get; init; } = 5000;
    public int MaxWarningCount { get; init; } = 100;
    public int MaxDiagnosticEntries { get; init; } = 32;
    public int MaxDiagnosticCharacters { get; init; } = 4096;
    public int MaxInputPayloadBytes { get; init; } = 64 * 1024 * 1024;
    public int MaxOutputPayloadBytes { get; init; } = 64 * 1024 * 1024;
    public bool RequireContentHashAllowList { get; init; }
    public bool RequireAssemblyHashApproval { get; init; } = true;
    public List<string> AllowedContentHashes { get; init; } = new();
}

public sealed class PluginProvenance
{
    public string? ContentHash { get; init; }
    public string? EntryPointHash { get; init; }
    public Dictionary<string, string> LocalDataHashes { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public string? DiscoveredAtUtc { get; init; }
}

public sealed class PluginTrustDecision
{
    public required GenerationPluginManifest Manifest { get; init; }
    public bool Allowed { get; init; }
    public List<string> Reasons { get; init; } = new();
}

public sealed class ExternalPluginRequestMetadata
{
    public required string Capability { get; init; }
    public required string ScenarioName { get; init; }
    public int? Seed { get; init; }
    public DateTimeOffset GeneratedAt { get; init; }
    public Dictionary<string, string?> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string?> PluginSettings { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ExternalPluginExecutionRequest
{
    public required GenerationPluginManifest Manifest { get; init; }
    public required ExternalPluginRequestMetadata Request { get; init; }
    public required SyntheticEnterpriseWorld InputWorld { get; init; }
    public CatalogSet PluginCatalogs { get; init; } = new();
}

public sealed class ExternalPluginExecutionResponse
{
    public bool Executed { get; init; }
    public List<PluginGeneratedRecord> Records { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
}

public interface IExternalGenerationAssemblyPlugin
{
    string Capability { get; }
    ExternalPluginExecutionResponse Execute(ExternalPluginExecutionRequest request);
}

public sealed class GenerationPluginInspectionRecord
{
    public required string SourcePath { get; init; }
    public required string SourceType { get; init; }
    public string Capability { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string PluginKind { get; init; } = string.Empty;
    public PluginExecutionMode ExecutionMode { get; init; } = PluginExecutionMode.MetadataOnly;
    public string? EntryPoint { get; init; }
    public string? ContentHash { get; init; }
    public string? EntryPointHash { get; init; }
    public int LocalDataHashCount { get; init; }
    public bool HasCompleteProvenance { get; init; }
    public bool Parsed { get; init; }
    public bool Valid { get; init; }
    public bool SecurityAllowed { get; init; }
    public bool Trusted { get; init; }
    public bool EligibleForActivation { get; init; }
    public bool RequiresAssemblyOptIn { get; init; }
    public bool RequiresHashApproval { get; init; }
    public List<string> ValidationMessages { get; init; } = new();
    public List<string> SecurityMessages { get; init; } = new();
    public List<string> TrustMessages { get; init; } = new();
    public List<PluginRuntimeCapability> RequestedCapabilities { get; init; } = new();
    public List<PluginRuntimeCapability> GrantedCapabilities { get; init; } = new();
    public List<string> Dependencies { get; init; } = new();
    public List<PluginParameterDescriptor> Parameters { get; init; } = new();
    public Dictionary<string, string?> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class GenerationPluginPackageValidationReport
{
    public required string RootPath { get; init; }
    public int PluginCount { get; init; }
    public int ParsedCount { get; init; }
    public int ValidCount { get; init; }
    public int TrustedCount { get; init; }
    public int EligibleCount { get; init; }
    public bool HasErrors { get; init; }
    public List<string> Messages { get; init; } = new();
    public List<GenerationPluginInspectionRecord> Plugins { get; init; } = new();
}

public sealed class GenerationPluginRegistration
{
    public required string Capability { get; init; }
    public required string DisplayName { get; init; }
    public required string RootPath { get; init; }
    public required string SourcePath { get; init; }
    public required string ContentHash { get; init; }
    public PluginExecutionMode ExecutionMode { get; init; } = PluginExecutionMode.MetadataOnly;
    public string PluginKind { get; init; } = string.Empty;
    public bool AllowAssemblyPlugins { get; init; }
    public bool Enabled { get; init; } = true;
    public string RegisteredAtUtc { get; init; } = DateTimeOffset.UtcNow.ToString("O");
}

public sealed class GenerationPluginRegistrationResult
{
    public List<GenerationPluginRegistration> Registered { get; init; } = new();
    public List<string> Messages { get; init; } = new();
}

public sealed class GenerationPluginInstallationResult
{
    public required string ManagedRootPath { get; init; }
    public List<string> InstalledPaths { get; init; } = new();
    public List<GenerationPluginRegistration> Registered { get; init; } = new();
    public List<string> Messages { get; init; } = new();
}
