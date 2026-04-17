namespace SyntheticEnterprise.Core.Plugins;

using SyntheticEnterprise.Contracts.Plugins;

public interface IGenerationPluginPackageValidator
{
    IReadOnlyList<GenerationPluginPackageValidationReport> Validate(IEnumerable<string> rootPaths, ExternalPluginExecutionSettings settings);
}

public sealed class GenerationPluginPackageValidator : IGenerationPluginPackageValidator
{
    private readonly IExternalGenerationPluginCatalog _catalog;

    public GenerationPluginPackageValidator(IExternalGenerationPluginCatalog catalog)
    {
        _catalog = catalog;
    }

    public IReadOnlyList<GenerationPluginPackageValidationReport> Validate(IEnumerable<string> rootPaths, ExternalPluginExecutionSettings settings)
    {
        var reports = new List<GenerationPluginPackageValidationReport>();

        foreach (var rootPath in rootPaths.Where(path => !string.IsNullOrWhiteSpace(path)).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var plugins = Directory.Exists(rootPath)
                ? _catalog.Inspect(new[] { rootPath }, settings).ToList()
                : new List<GenerationPluginInspectionRecord>();
            var messages = new List<string>();

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
                || plugins.Any(plugin => plugin.RequiresHashApproval && !plugin.Trusted);

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
                Plugins = plugins
            });
        }

        return reports;
    }
}
