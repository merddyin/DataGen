namespace SyntheticEnterprise.Core.Plugins;

using SyntheticEnterprise.Contracts.Plugins;

public interface IGenerationPluginPackageValidator
{
    IReadOnlyList<GenerationPluginPackageValidationReport> Validate(
        IEnumerable<string> rootPaths,
        ExternalPluginExecutionSettings settings,
        bool validatePackContract = false);
}

public sealed class GenerationPluginPackageValidator : IGenerationPluginPackageValidator
{
    private readonly IExternalGenerationPluginCatalog _catalog;

    public GenerationPluginPackageValidator(IExternalGenerationPluginCatalog catalog)
    {
        _catalog = catalog;
    }

    public IReadOnlyList<GenerationPluginPackageValidationReport> Validate(
        IEnumerable<string> rootPaths,
        ExternalPluginExecutionSettings settings,
        bool validatePackContract = false)
    {
        var reports = new List<GenerationPluginPackageValidationReport>();

        foreach (var rootPath in rootPaths.Where(path => !string.IsNullOrWhiteSpace(path)).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var plugins = Directory.Exists(rootPath)
                ? _catalog.Inspect(new[] { rootPath }, settings).ToList()
                : new List<GenerationPluginInspectionRecord>();
            var messages = new List<string>();
            var packContractIssues = validatePackContract
                ? BuildPackContractIssues(plugins)
                : new List<GenerationPluginContractIssue>();

            if (!Directory.Exists(rootPath))
            {
                messages.Add("Plugin root path does not exist.");
            }
            else if (plugins.Count == 0)
            {
                messages.Add("No plugin manifests were discovered under this root.");
            }

            var hasErrors = !Directory.Exists(rootPath)
                || plugins.Any(plugin => !plugin.Parsed || !plugin.Valid || !plugin.SecurityAllowed)
                || plugins.Any(plugin => plugin.RequiresAssemblyOptIn && !settings.AllowAssemblyPlugins)
                || plugins.Any(plugin => plugin.RequiresHashApproval && !plugin.Trusted)
                || packContractIssues.Any(issue => issue.IsError);

            reports.Add(new GenerationPluginPackageValidationReport
            {
                RootPath = rootPath,
                PluginCount = plugins.Count,
                ParsedCount = plugins.Count(plugin => plugin.Parsed),
                ValidCount = plugins.Count(plugin => plugin.Valid),
                TrustedCount = plugins.Count(plugin => plugin.Trusted),
                EligibleCount = plugins.Count(plugin => plugin.EligibleForActivation),
                HasErrors = hasErrors,
                Messages = messages,
                Plugins = plugins,
                PackContractChecked = validatePackContract,
                PackContractErrorCount = packContractIssues.Count(issue => issue.IsError),
                PackContractWarningCount = packContractIssues.Count(issue => !issue.IsError),
                PackContractIssues = packContractIssues
            });
        }

        return reports;
    }

    private static List<GenerationPluginContractIssue> BuildPackContractIssues(IReadOnlyCollection<GenerationPluginInspectionRecord> plugins)
    {
        var issues = new List<GenerationPluginContractIssue>();

        foreach (var plugin in plugins)
        {
            var metadata = plugin.Metadata ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var pluginKind = plugin.PluginKind ?? string.Empty;
            var packId = metadata.TryGetValue("packId", out var resolvedPackId) ? resolvedPackId : null;
            var packPhase = metadata.TryGetValue("packPhase", out var resolvedPackPhase) ? resolvedPackPhase : null;
            var category = metadata.TryGetValue("category", out var resolvedCategory) ? resolvedCategory : null;

            if (!pluginKind.EndsWith("Pack", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new GenerationPluginContractIssue
                {
                    Capability = plugin.Capability,
                    RuleId = "pack-kind",
                    Message = $"Pack plugins should use a pluginKind ending with 'Pack'. Found '{plugin.PluginKind}'.",
                    IsError = true
                });
            }

            if (string.IsNullOrWhiteSpace(packId))
            {
                issues.Add(new GenerationPluginContractIssue
                {
                    Capability = plugin.Capability,
                    RuleId = "pack-id-missing",
                    Message = "Pack metadata must declare 'packId'.",
                    IsError = true
                });
            }
            else if (!string.Equals(packId, plugin.Capability, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new GenerationPluginContractIssue
                {
                    Capability = plugin.Capability,
                    RuleId = "pack-id-mismatch",
                    Message = $"Pack metadata 'packId' should match capability '{plugin.Capability}'. Found '{packId}'.",
                    IsError = true
                });
            }

            if (string.IsNullOrWhiteSpace(packPhase))
            {
                issues.Add(new GenerationPluginContractIssue
                {
                    Capability = plugin.Capability,
                    RuleId = "pack-phase-missing",
                    Message = "Pack metadata must declare 'packPhase'.",
                    IsError = true
                });
            }
            else if (!string.Equals(packPhase, "PostWorldGeneration", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new GenerationPluginContractIssue
                {
                    Capability = plugin.Capability,
                    RuleId = "pack-phase-nonstandard",
                    Message = $"Pack phase '{packPhase}' is non-standard. Current first-party packs use 'PostWorldGeneration'.",
                    IsError = false
                });
            }

            if (string.IsNullOrWhiteSpace(category))
            {
                issues.Add(new GenerationPluginContractIssue
                {
                    Capability = plugin.Capability,
                    RuleId = "pack-category-missing",
                    Message = "Pack metadata should declare 'category' so authoring surfaces can group the pack coherently.",
                    IsError = false
                });
            }

            if (!plugin.RequestedCapabilities.Contains(PluginRuntimeCapability.GenerateData))
            {
                issues.Add(new GenerationPluginContractIssue
                {
                    Capability = plugin.Capability,
                    RuleId = "pack-generate-data-missing",
                    Message = "Pack plugins must request the 'GenerateData' runtime capability.",
                    IsError = true
                });
            }
        }

        return issues;
    }
}
