namespace SyntheticEnterprise.Core.Plugins;

using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Contracts.Plugins;
using SyntheticEnterprise.Core.Abstractions;

public interface IWorldGenerationPlugin
{
    GenerationPluginManifest Manifest { get; }
    bool IsEnabled(ScenarioDefinition scenario);
    void Execute(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs);
}

public interface IGenerationPluginExecutionPlanner
{
    GenerationPluginExecutionPlan BuildPlan(ScenarioDefinition scenario, IEnumerable<IWorldGenerationPlugin> plugins);
}

public sealed class GenerationPluginExecutionPlanner : IGenerationPluginExecutionPlanner
{
    public GenerationPluginExecutionPlan BuildPlan(ScenarioDefinition scenario, IEnumerable<IWorldGenerationPlugin> plugins)
    {
        var available = plugins
            .Where(plugin => plugin.IsEnabled(scenario))
            .ToDictionary(plugin => plugin.Manifest.Capability, StringComparer.OrdinalIgnoreCase);

        var ordered = new List<GenerationPluginManifest>();
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var plugin in available.Values.OrderBy(plugin => plugin.Manifest.Capability, StringComparer.OrdinalIgnoreCase))
        {
            Visit(plugin, available, visiting, visited, ordered);
        }

        return new GenerationPluginExecutionPlan
        {
            ActivePlugins = ordered
        };
    }

    private static void Visit(
        IWorldGenerationPlugin plugin,
        IReadOnlyDictionary<string, IWorldGenerationPlugin> available,
        HashSet<string> visiting,
        HashSet<string> visited,
        List<GenerationPluginManifest> ordered)
    {
        var capability = plugin.Manifest.Capability;
        if (visited.Contains(capability))
        {
            return;
        }

        if (!visiting.Add(capability))
        {
            throw new InvalidOperationException($"Circular plugin dependency detected at '{capability}'.");
        }

        foreach (var dependency in plugin.Manifest.Dependencies)
        {
            if (available.TryGetValue(dependency, out var dependencyPlugin))
            {
                Visit(dependencyPlugin, available, visiting, visited, ordered);
            }
        }

        visiting.Remove(capability);
        visited.Add(capability);
        ordered.Add(plugin.Manifest);
    }
}

public sealed class OrganizationGenerationPlugin : IWorldGenerationPlugin
{
    private readonly IOrganizationGenerator _generator;

    public OrganizationGenerationPlugin(IOrganizationGenerator generator)
    {
        _generator = generator;
    }

    public GenerationPluginManifest Manifest { get; } = new()
    {
        Capability = "Organization",
        DisplayName = "Organization Baseline"
    };

    public bool IsEnabled(ScenarioDefinition scenario) => true;

    public void Execute(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs)
        => _generator.GenerateOrganizations(world, context, catalogs);
}

public sealed class GeographyGenerationPlugin : IWorldGenerationPlugin
{
    private readonly IGeographyGenerator _generator;

    public GeographyGenerationPlugin(IGeographyGenerator generator)
    {
        _generator = generator;
    }

    public GenerationPluginManifest Manifest { get; } = new()
    {
        Capability = "Geography",
        DisplayName = "Geography and Addressing",
        Dependencies = new() { "Organization" }
    };

    public bool IsEnabled(ScenarioDefinition scenario) => true;

    public void Execute(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs)
        => _generator.GenerateOffices(world, context, catalogs);
}

public sealed class IdentityGenerationPlugin : IWorldGenerationPlugin
{
    private readonly IIdentityGenerator _generator;

    public IdentityGenerationPlugin(IIdentityGenerator generator)
    {
        _generator = generator;
    }

    public GenerationPluginManifest Manifest { get; } = new()
    {
        Capability = "Identity",
        DisplayName = "Identity Layer",
        Dependencies = new() { "Organization", "Geography" }
    };

    public bool IsEnabled(ScenarioDefinition scenario)
        => scenario.Identity.IncludeHybridDirectory || scenario.Identity.IncludeM365StyleGroups;

    public void Execute(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs)
        => _generator.GenerateIdentity(world, context, catalogs);
}

public sealed class InfrastructureGenerationPlugin : IWorldGenerationPlugin
{
    private readonly IInfrastructureGenerator _generator;

    public InfrastructureGenerationPlugin(IInfrastructureGenerator generator)
    {
        _generator = generator;
    }

    public GenerationPluginManifest Manifest { get; } = new()
    {
        Capability = "Infrastructure",
        DisplayName = "Infrastructure Layer",
        Dependencies = new() { "Organization", "Geography", "Identity" }
    };

    public bool IsEnabled(ScenarioDefinition scenario)
        => scenario.Infrastructure.IncludeServers
           || scenario.Infrastructure.IncludeWorkstations
           || scenario.Infrastructure.IncludeNetworkAssets
           || scenario.Infrastructure.IncludeTelephony;

    public void Execute(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs)
        => _generator.GenerateInfrastructure(world, context, catalogs);
}

public sealed class ApplicationGenerationPlugin : IWorldGenerationPlugin
{
    private readonly IApplicationGenerator _generator;

    public ApplicationGenerationPlugin(IApplicationGenerator generator)
    {
        _generator = generator;
    }

    public GenerationPluginManifest Manifest { get; } = new()
    {
        Capability = "Applications",
        DisplayName = "Application Layer",
        Dependencies = new() { "Organization", "Identity" }
    };

    public bool IsEnabled(ScenarioDefinition scenario)
        => scenario.Applications.IncludeApplications;

    public void Execute(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs)
        => _generator.GenerateApplications(world, context, catalogs);
}

public sealed class RepositoryGenerationPlugin : IWorldGenerationPlugin
{
    private readonly IRepositoryGenerator _generator;

    public RepositoryGenerationPlugin(IRepositoryGenerator generator)
    {
        _generator = generator;
    }

    public GenerationPluginManifest Manifest { get; } = new()
    {
        Capability = "Repository",
        DisplayName = "Repository Layer",
        Dependencies = new() { "Organization", "Identity", "Applications", "Infrastructure", "BusinessProcesses", "ApplicationTopology", "CloudTenancy" }
    };

    public bool IsEnabled(ScenarioDefinition scenario)
        => scenario.Repositories.IncludeDatabases
           || scenario.Repositories.IncludeFileShares
           || scenario.Repositories.IncludeCollaborationSites;

    public void Execute(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs)
        => _generator.GenerateRepositories(world, context, catalogs);
}

public sealed class ApplicationTopologyGenerationPlugin : IWorldGenerationPlugin
{
    private readonly IApplicationTopologyGenerator _generator;

    public ApplicationTopologyGenerationPlugin(IApplicationTopologyGenerator generator)
    {
        _generator = generator;
    }

    public GenerationPluginManifest Manifest { get; } = new()
    {
        Capability = "ApplicationTopology",
        DisplayName = "Application Service Topology",
        Dependencies = new() { "Applications", "Infrastructure" }
    };

    public bool IsEnabled(ScenarioDefinition scenario)
        => scenario.Applications.IncludeApplications;

    public void Execute(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs)
        => _generator.GenerateTopology(world, context, catalogs);
}

public sealed class BusinessProcessGenerationPlugin : IWorldGenerationPlugin
{
    private readonly IBusinessProcessGenerator _generator;

    public BusinessProcessGenerationPlugin(IBusinessProcessGenerator generator)
    {
        _generator = generator;
    }

    public GenerationPluginManifest Manifest { get; } = new()
    {
        Capability = "BusinessProcesses",
        DisplayName = "Business Process Model",
        Dependencies = new() { "Organization", "Applications", "Geography" }
    };

    public bool IsEnabled(ScenarioDefinition scenario)
        => scenario.Applications.IncludeApplications;

    public void Execute(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs)
        => _generator.GenerateBusinessProcesses(world, context, catalogs);
}

public sealed class CloudTenancyGenerationPlugin : IWorldGenerationPlugin
{
    private readonly ICloudTenantGenerator _generator;

    public CloudTenancyGenerationPlugin(ICloudTenantGenerator generator)
    {
        _generator = generator;
    }

    public GenerationPluginManifest Manifest { get; } = new()
    {
        Capability = "CloudTenancy",
        DisplayName = "Cloud Tenant Topology",
        Dependencies = new() { "Applications", "BusinessProcesses", "ApplicationTopology", "Geography" }
    };

    public bool IsEnabled(ScenarioDefinition scenario)
        => scenario.Applications.IncludeApplications && scenario.Applications.IncludeSaaSApplications;

    public void Execute(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs)
        => _generator.GenerateTenants(world, context, catalogs);
}

public sealed class ExternalEcosystemGenerationPlugin : IWorldGenerationPlugin
{
    private readonly IExternalEcosystemGenerator _generator;

    public ExternalEcosystemGenerationPlugin(IExternalEcosystemGenerator generator)
    {
        _generator = generator;
    }

    public GenerationPluginManifest Manifest { get; } = new()
    {
        Capability = "ExternalEcosystem",
        DisplayName = "External Vendors and Customers",
        Dependencies = new() { "Applications", "BusinessProcesses", "CloudTenancy", "Geography" }
    };

    public bool IsEnabled(ScenarioDefinition scenario)
        => scenario.Applications.IncludeApplications;

    public void Execute(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs)
        => _generator.GenerateEcosystem(world, context, catalogs);
}

public sealed class ObservedDataGenerationPlugin : IWorldGenerationPlugin
{
    private readonly IObservedDataGenerator _generator;

    public ObservedDataGenerationPlugin(IObservedDataGenerator generator)
    {
        _generator = generator;
    }

    public GenerationPluginManifest Manifest { get; } = new()
    {
        Capability = "ObservedViews",
        DisplayName = "Observed vs Ground Truth Views",
        Dependencies = new() { "Identity", "Applications", "Infrastructure", "ExternalEcosystem", "Repository" }
    };

    public bool IsEnabled(ScenarioDefinition scenario)
        => scenario.ObservedData.IncludeObservedViews;

    public void Execute(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs)
        => _generator.GenerateObservedData(world, context, catalogs);
}

public sealed class CmdbGenerationPlugin : IWorldGenerationPlugin
{
    private readonly ICmdbGenerator _generator;

    public CmdbGenerationPlugin(ICmdbGenerator generator)
    {
        _generator = generator;
    }

    public GenerationPluginManifest Manifest { get; } = new()
    {
        Capability = "ConfigurationManagement",
        DisplayName = "Configuration Management and CMDB",
        Dependencies = new() { "Identity", "Applications", "Infrastructure", "BusinessProcesses", "ApplicationTopology", "CloudTenancy", "Repository" }
    };

    public bool IsEnabled(ScenarioDefinition scenario)
        => scenario.Cmdb.IncludeConfigurationManagement;

    public void Execute(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs)
        => _generator.GenerateConfigurationManagement(world, context, catalogs);
}

public sealed class AnomalyGenerationPlugin : IWorldGenerationPlugin
{
    private readonly IEnumerable<IAnomalyInjector> _injectors;

    public AnomalyGenerationPlugin(IEnumerable<IAnomalyInjector> injectors)
    {
        _injectors = injectors;
    }

    public GenerationPluginManifest Manifest { get; } = new()
    {
        Capability = "Anomaly",
        DisplayName = "Anomaly Injection",
        Dependencies = new() { "Organization", "Identity", "Applications", "Infrastructure", "BusinessProcesses", "ApplicationTopology", "CloudTenancy", "ExternalEcosystem", "Repository", "ObservedViews", "ConfigurationManagement" }
    };

    public bool IsEnabled(ScenarioDefinition scenario) => scenario.Anomalies.Count > 0;

    public void Execute(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs)
    {
        foreach (var anomalyProfile in context.Scenario.Anomalies)
        {
            foreach (var injector in _injectors)
            {
                injector.Apply(world, context, catalogs, anomalyProfile);
            }
        }
    }
}
