namespace SyntheticEnterprise.Core.Plugins;

using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Contracts.Plugins;
using SyntheticEnterprise.Core.Abstractions;

public interface IExternalPluginHostAdapter
{
    bool CanExecute(GenerationPluginManifest manifest);
    ExternalPluginExecutionResult Execute(GenerationPluginManifest manifest, SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs);
}

public interface IExternalPluginOrchestrator
{
    ExternalPluginApplicationResult Apply(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs);
}

public interface IExternalPluginCapabilityResolver
{
    ExternalPluginCapabilityPlan Resolve(GenerationContext context);
}

public sealed class ExternalPluginExecutionResult
{
    public required GenerationPluginManifest Manifest { get; init; }
    public List<PluginGeneratedRecord> Records { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
    public bool Executed { get; init; }
}

public sealed class ExternalPluginApplicationResult
{
    public List<string> AppliedCapabilities { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
}

public sealed class ExternalPluginCapabilityResolver : IExternalPluginCapabilityResolver
{
    private readonly IGenerationPluginRegistry _registry;

    public ExternalPluginCapabilityResolver(IGenerationPluginRegistry registry)
    {
        _registry = registry;
    }

    public ExternalPluginCapabilityPlan Resolve(GenerationContext context)
    {
        if (!context.ExternalPlugins.Enabled
            || context.ExternalPlugins.PluginRootPaths.Count == 0
            || context.ExternalPlugins.EnabledCapabilities.Count == 0)
        {
            return new ExternalPluginCapabilityPlan();
        }

        var requestedCapabilities = context.ExternalPlugins.EnabledCapabilities
            .Where(capability => !string.IsNullOrWhiteSpace(capability))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var discovered = _registry
            .GetDiscoveredManifests(context.ExternalPlugins.PluginRootPaths)
            .ToDictionary(manifest => manifest.Capability, StringComparer.OrdinalIgnoreCase);

        var ordered = new List<GenerationPluginManifest>();
        var warnings = new List<string>();
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var capability in requestedCapabilities)
        {
            Visit(capability);
        }

        return new ExternalPluginCapabilityPlan
        {
            ActivePlugins = ordered,
            Contributions = ordered.Select(manifest => new GenerationPluginCapabilityContribution
            {
                Capability = manifest.Capability,
                DisplayName = manifest.DisplayName,
                ExecutionMode = manifest.ExecutionMode,
                PluginKind = manifest.PluginKind,
                Dependencies = manifest.Dependencies.ToList(),
                Parameters = manifest.Parameters.ToList(),
                Metadata = new Dictionary<string, string?>(manifest.Metadata, StringComparer.OrdinalIgnoreCase),
                RequestedCapabilities = manifest.Security.RequestedCapabilities.ToList()
            }).ToList(),
            Warnings = warnings
        };

        void Visit(string capability)
        {
            if (visited.Contains(capability))
            {
                return;
            }

            if (!discovered.TryGetValue(capability, out var manifest))
            {
                warnings.Add($"Requested external plugin capability '{capability}' was not found.");
                visited.Add(capability);
                return;
            }

            if (!visiting.Add(capability))
            {
                throw new InvalidOperationException($"Circular external plugin dependency detected at '{capability}'.");
            }

            foreach (var dependency in manifest.Dependencies)
            {
                Visit(dependency);
            }

            visiting.Remove(capability);
            visited.Add(capability);
            ordered.Add(manifest);
        }
    }
}

public sealed class ExternalPluginOrchestrator : IExternalPluginOrchestrator
{
    private readonly IExternalPluginCapabilityResolver _capabilityResolver;
    private readonly IGenerationPluginSecurityPolicy _securityPolicy;
    private readonly IExternalPluginTrustPolicy _trustPolicy;
    private readonly IEnumerable<IExternalPluginHostAdapter> _hostAdapters;

    public ExternalPluginOrchestrator(
        IExternalPluginCapabilityResolver capabilityResolver,
        IGenerationPluginSecurityPolicy securityPolicy,
        IExternalPluginTrustPolicy trustPolicy,
        IEnumerable<IExternalPluginHostAdapter> hostAdapters)
    {
        _capabilityResolver = capabilityResolver;
        _securityPolicy = securityPolicy;
        _trustPolicy = trustPolicy;
        _hostAdapters = hostAdapters;
    }

    public ExternalPluginApplicationResult Apply(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs)
    {
        if (!context.ExternalPlugins.Enabled
            || context.ExternalPlugins.PluginRootPaths.Count == 0
            || context.ExternalPlugins.EnabledCapabilities.Count == 0)
        {
            return new ExternalPluginApplicationResult();
        }

        var plan = _capabilityResolver.Resolve(context);
        var warnings = plan.Warnings.ToList();
        var applied = new List<string>();

        foreach (var manifest in plan.ActivePlugins)
        {
            var trustDecision = _trustPolicy.Evaluate(manifest, context.ExternalPlugins);
            if (!trustDecision.Allowed)
            {
                warnings.AddRange(trustDecision.Reasons.Select(reason => $"{manifest.Capability}: {reason}"));
                continue;
            }

            var decision = _securityPolicy.Evaluate(manifest);
            if (!decision.Allowed)
            {
                warnings.AddRange(decision.DeniedReasons.Select(reason => $"{manifest.Capability}: {reason}"));
                continue;
            }

            var adapter = _hostAdapters.FirstOrDefault(candidate => candidate.CanExecute(manifest));
            if (adapter is null)
            {
                warnings.Add($"{manifest.Capability}: no execution host is available for mode '{manifest.ExecutionMode}'.");
                continue;
            }

            var result = adapter.Execute(manifest, world, context, catalogs);
            foreach (var record in result.Records)
            {
                world.PluginRecords.Add(record);
            }

            warnings.AddRange(result.Warnings.Select(warning => $"{manifest.Capability}: {warning}"));
            if (result.Executed)
            {
                applied.Add(manifest.Capability);
            }
        }

        return new ExternalPluginApplicationResult
        {
            AppliedCapabilities = applied,
            Warnings = warnings
        };
    }
}

public sealed class DeniedAssemblyExternalPluginHostAdapter : IExternalPluginHostAdapter
{
    public bool CanExecute(GenerationPluginManifest manifest)
        => manifest.ExecutionMode == PluginExecutionMode.DotNetAssembly;

    public ExternalPluginExecutionResult Execute(GenerationPluginManifest manifest, SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs)
        => new()
        {
            Manifest = manifest,
            Executed = false,
            Warnings = new()
            {
                "DotNetAssembly external plugins are not enabled until an isolated host process is implemented."
            }
        };
}
