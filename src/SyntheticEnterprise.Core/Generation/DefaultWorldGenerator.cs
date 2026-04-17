namespace SyntheticEnterprise.Core.Generation;

using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Core.Abstractions;
using SyntheticEnterprise.Core.Plugins;

public sealed class DefaultWorldGenerator : IWorldGenerator
{
    private readonly IReadOnlyDictionary<string, IWorldGenerationPlugin> _plugins;
    private readonly IGenerationPluginExecutionPlanner _planner;
    private readonly IExternalPluginOrchestrator _externalPluginOrchestrator;
    private readonly ILayerOwnershipRegistry _layerOwnershipRegistry;
    private readonly IWorldOwnershipReconciliationService _worldOwnershipReconciliationService;
    private readonly IWorldReferenceRepairService _worldReferenceRepairService;
    private readonly IWorldInvariantValidator _worldInvariantValidator;
    private readonly IWorldQualityAuditService _worldQualityAuditService;

    public DefaultWorldGenerator(
        IEnumerable<IWorldGenerationPlugin> plugins,
        IGenerationPluginExecutionPlanner planner,
        IExternalPluginOrchestrator externalPluginOrchestrator,
        ILayerOwnershipRegistry layerOwnershipRegistry,
        IWorldOwnershipReconciliationService worldOwnershipReconciliationService,
        IWorldReferenceRepairService worldReferenceRepairService,
        IWorldInvariantValidator worldInvariantValidator,
        IWorldQualityAuditService worldQualityAuditService)
    {
        _plugins = plugins.ToDictionary(plugin => plugin.Manifest.Capability, StringComparer.OrdinalIgnoreCase);
        _planner = planner;
        _externalPluginOrchestrator = externalPluginOrchestrator;
        _layerOwnershipRegistry = layerOwnershipRegistry;
        _worldOwnershipReconciliationService = worldOwnershipReconciliationService;
        _worldReferenceRepairService = worldReferenceRepairService;
        _worldInvariantValidator = worldInvariantValidator;
        _worldQualityAuditService = worldQualityAuditService;
    }

    public GenerationResult Generate(GenerationContext context, CatalogSet catalogs)
    {
        var world = new SyntheticEnterpriseWorld();
        var plan = _planner.BuildPlan(context.Scenario, _plugins.Values);
        var appliedPlugins = new List<string>();

        foreach (var manifest in plan.ActivePlugins)
        {
            if (_plugins.TryGetValue(manifest.Capability, out var plugin))
            {
                plugin.Execute(world, context, catalogs);
                appliedPlugins.Add(manifest.Capability);
            }
        }

        var warnings = new List<string>();
        var externalPluginResult = _externalPluginOrchestrator.Apply(world, context, catalogs);
        appliedPlugins.AddRange(externalPluginResult.AppliedCapabilities);
        warnings.AddRange(externalPluginResult.Warnings);
        var ownershipResult = _worldOwnershipReconciliationService.Reconcile(world);
        warnings.AddRange(ownershipResult.Warnings);
        var repairResult = _worldReferenceRepairService.Repair(world);
        warnings.AddRange(repairResult.Warnings);
        var invariantValidationResult = _worldInvariantValidator.Validate(world);
        if (invariantValidationResult.Errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, invariantValidationResult.Errors));
        }

        var qualityAuditResult = _worldQualityAuditService.Audit(world);
        warnings.AddRange(qualityAuditResult.Warnings);

        return new GenerationResult
        {
            World = world,
            Statistics = new GenerationStatistics
            {
                CompanyCount = world.Companies.Count,
                OfficeCount = world.Offices.Count,
                PersonCount = world.People.Count,
                AccountCount = world.Accounts.Count,
                GroupCount = world.Groups.Count,
                ApplicationCount = world.Applications.Count,
                DeviceCount = world.Devices.Count + world.Servers.Count,
                RepositoryCount = world.Databases.Count
                    + world.FileShares.Count
                    + world.CollaborationSites.Count
                    + world.CollaborationChannels.Count
                    + world.CollaborationChannelTabs.Count
                    + world.DocumentLibraries.Count
                    + world.SitePages.Count
                    + world.DocumentFolders.Count
            },
            Catalogs = catalogs,
            WorldMetadata = new WorldMetadata
            {
                Scenario = context.Scenario,
                Seed = context.Seed,
                GeneratedAt = context.GeneratedAt,
                CatalogRootPath = context.Metadata.TryGetValue("CatalogRootPath", out var p) ? p : null,
                CatalogKeys = new HashSet<string>(
                    catalogs.CsvCatalogs.Keys.Concat(catalogs.JsonCatalogs.Keys),
                    StringComparer.OrdinalIgnoreCase),
                AppliedLayers = new HashSet<string>(appliedPlugins, StringComparer.OrdinalIgnoreCase),
                OwnedArtifacts = _layerOwnershipRegistry.GetOwnedArtifacts().ToList()
            },
            Warnings = warnings
        };
    }
}
