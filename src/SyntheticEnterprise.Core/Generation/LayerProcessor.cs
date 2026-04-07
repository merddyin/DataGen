namespace SyntheticEnterprise.Core.Generation;

using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Core.Abstractions;

public sealed class LayerProcessor : ILayerProcessor
{
    private readonly IIdentityGenerator _identityGenerator;
    private readonly IInfrastructureGenerator _infrastructureGenerator;
    private readonly IRepositoryGenerator _repositoryGenerator;
    private readonly IEnumerable<IAnomalyInjector> _anomalyInjectors;
    private readonly IWorldCloner _worldCloner;
    private readonly ICatalogContextResolver _catalogContextResolver;

    public LayerProcessor(
        IIdentityGenerator identityGenerator,
        IInfrastructureGenerator infrastructureGenerator,
        IRepositoryGenerator repositoryGenerator,
        IEnumerable<IAnomalyInjector> anomalyInjectors,
        IWorldCloner worldCloner,
        ICatalogContextResolver catalogContextResolver)
    {
        _identityGenerator = identityGenerator;
        _infrastructureGenerator = infrastructureGenerator;
        _repositoryGenerator = repositoryGenerator;
        _anomalyInjectors = anomalyInjectors;
        _worldCloner = worldCloner;
        _catalogContextResolver = catalogContextResolver;
    }

    public GenerationResult AddIdentityLayer(GenerationResult input, LayerProcessingOptions? options = null)
    {
        options ??= new LayerProcessingOptions();
        var result = _worldCloner.Clone(input);
        var mode = options.IdentityMode;

        if (mode == LayerRegenerationMode.SkipIfPresent && HasIdentityData(result))
        {
            return AppendWarning(result, "Identity layer already present; skipped.");
        }

        if (mode == LayerRegenerationMode.ReplaceLayer)
        {
            ClearIdentityData(result);
        }

        var catalogs = _catalogContextResolver.Resolve(result);
        var context = BuildContext(result);
        _identityGenerator.GenerateIdentity(result.World, context, catalogs);

        result = UpdateCatalogMetadata(result, catalogs);
        MarkLayerApplied(result, "Identity");
        return RefreshStatistics(result);
    }

    public GenerationResult AddInfrastructureLayer(GenerationResult input, LayerProcessingOptions? options = null)
    {
        options ??= new LayerProcessingOptions();
        var result = _worldCloner.Clone(input);
        var mode = options.InfrastructureMode;

        if (mode == LayerRegenerationMode.SkipIfPresent && HasInfrastructureData(result))
        {
            return AppendWarning(result, "Infrastructure layer already present; skipped.");
        }

        if (mode == LayerRegenerationMode.ReplaceLayer)
        {
            ClearInfrastructureData(result);
        }

        var catalogs = _catalogContextResolver.Resolve(result);
        var context = BuildContext(result);
        _infrastructureGenerator.GenerateInfrastructure(result.World, context, catalogs);

        result = UpdateCatalogMetadata(result, catalogs);
        MarkLayerApplied(result, "Infrastructure");
        return RefreshStatistics(result);
    }

    public GenerationResult AddRepositoryLayer(GenerationResult input, LayerProcessingOptions? options = null)
    {
        options ??= new LayerProcessingOptions();
        var result = _worldCloner.Clone(input);
        var mode = options.RepositoryMode;

        if (mode == LayerRegenerationMode.SkipIfPresent && HasRepositoryData(result))
        {
            return AppendWarning(result, "Repository layer already present; skipped.");
        }

        if (mode == LayerRegenerationMode.ReplaceLayer)
        {
            ClearRepositoryData(result);
        }

        var catalogs = _catalogContextResolver.Resolve(result);
        var context = BuildContext(result);
        _repositoryGenerator.GenerateRepositories(result.World, context, catalogs);

        result = UpdateCatalogMetadata(result, catalogs);
        MarkLayerApplied(result, "Repository");
        return RefreshStatistics(result);
    }

    public GenerationResult ApplyAnomalyProfiles(GenerationResult input, LayerProcessingOptions? options = null)
    {
        options ??= new LayerProcessingOptions();
        var result = _worldCloner.Clone(input);
        var catalogs = _catalogContextResolver.Resolve(result);
        var context = BuildContext(result);

        foreach (var anomalyProfile in context.Scenario.Anomalies)
        {
            var profileKey = $"{anomalyProfile.Category}:{anomalyProfile.Name}";
            if (options.ApplyAnomaliesIdempotently &&
                result.WorldMetadata is not null &&
                result.WorldMetadata.AppliedAnomalyProfiles.Contains(profileKey))
            {
                result = AppendWarning(result, $"Anomaly profile already applied; skipped: {profileKey}");
                continue;
            }

            foreach (var injector in _anomalyInjectors)
            {
                injector.Apply(result.World, context, catalogs, anomalyProfile);
            }

            if (result.WorldMetadata is not null)
            {
                result.WorldMetadata.AppliedAnomalyProfiles.Add(profileKey);
            }
        }

        result = UpdateCatalogMetadata(result, catalogs);
        return RefreshStatistics(result);
    }

    private static GenerationContext BuildContext(GenerationResult input)
    {
        return new GenerationContext
        {
            Scenario = input.WorldMetadata?.Scenario ?? new SyntheticEnterprise.Contracts.Configuration.ScenarioDefinition(),
            Seed = input.WorldMetadata?.Seed,
            GeneratedAt = input.WorldMetadata?.GeneratedAt ?? DateTimeOffset.UtcNow,
            Metadata = new Dictionary<string, string?>
            {
                ["CatalogRootPath"] = input.WorldMetadata?.CatalogRootPath,
                ["GeneratedAt"] = input.WorldMetadata?.GeneratedAt.ToString("O")
            }
        };
    }

    private static GenerationResult UpdateCatalogMetadata(GenerationResult input, CatalogSet catalogs)
    {
        input = input with { Catalogs = catalogs };

        if (input.WorldMetadata is not null)
        {
            input.WorldMetadata.CatalogKeys.Clear();
            foreach (var key in catalogs.CsvCatalogs.Keys.Concat(catalogs.JsonCatalogs.Keys))
            {
                input.WorldMetadata.CatalogKeys.Add(key);
            }
        }

        return input;
    }

    private static bool HasIdentityData(GenerationResult input) =>
        input.World.Accounts.Count > 0 ||
        input.World.Groups.Count > 0 ||
        input.World.OrganizationalUnits.Count > 0 ||
        input.World.GroupMemberships.Count > 0;

    private static bool HasInfrastructureData(GenerationResult input) =>
        input.World.Devices.Count > 0 ||
        input.World.Servers.Count > 0 ||
        input.World.NetworkAssets.Count > 0 ||
        input.World.TelephonyAssets.Count > 0;

    private static bool HasRepositoryData(GenerationResult input) =>
        input.World.Databases.Count > 0 ||
        input.World.FileShares.Count > 0 ||
        input.World.CollaborationSites.Count > 0 ||
        input.World.RepositoryAccessGrants.Count > 0;

    private static void ClearIdentityData(GenerationResult input)
    {
        input.World.OrganizationalUnits.Clear();
        input.World.Accounts.Clear();
        input.World.Groups.Clear();
        input.World.GroupMemberships.Clear();
        input.World.IdentityAnomalies.Clear();
    }

    private static void ClearInfrastructureData(GenerationResult input)
    {
        input.World.Devices.Clear();
        input.World.Servers.Clear();
        input.World.NetworkAssets.Clear();
        input.World.TelephonyAssets.Clear();
        input.World.SoftwarePackages.Clear();
        input.World.DeviceSoftwareInstallations.Clear();
        input.World.ServerSoftwareInstallations.Clear();
        input.World.InfrastructureAnomalies.Clear();
    }

    private static void ClearRepositoryData(GenerationResult input)
    {
        input.World.Databases.Clear();
        input.World.FileShares.Clear();
        input.World.CollaborationSites.Clear();
        input.World.RepositoryAccessGrants.Clear();
        input.World.RepositoryAnomalies.Clear();
    }

    private static void MarkLayerApplied(GenerationResult input, string layer)
    {
        input.WorldMetadata?.AppliedLayers.Add(layer);
    }

    private static GenerationResult RefreshStatistics(GenerationResult input)
    {
        return input with
        {
            Statistics = new GenerationStatistics
            {
                CompanyCount = input.World.Companies.Count,
                OfficeCount = input.World.Offices.Count,
                PersonCount = input.World.People.Count,
                AccountCount = input.World.Accounts.Count,
                GroupCount = input.World.Groups.Count,
                ApplicationCount = input.World.Applications.Count,
                DeviceCount = input.World.Devices.Count + input.World.Servers.Count,
                RepositoryCount = input.World.Databases.Count + input.World.FileShares.Count + input.World.CollaborationSites.Count
            }
        };
    }

    private static GenerationResult AppendWarning(GenerationResult input, string warning)
    {
        var warnings = input.Warnings.ToList();
        warnings.Add(warning);
        return input with { Warnings = warnings };
    }
}
