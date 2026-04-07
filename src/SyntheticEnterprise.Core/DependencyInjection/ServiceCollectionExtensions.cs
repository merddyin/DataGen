namespace SyntheticEnterprise.Core.DependencyInjection;

using Microsoft.Extensions.DependencyInjection;
using SyntheticEnterprise.Core.Abstractions;
using SyntheticEnterprise.Core.Catalogs;
using SyntheticEnterprise.Core.Generation;
using SyntheticEnterprise.Core.Generation.Geography;
using SyntheticEnterprise.Core.Generation.Identity;
using SyntheticEnterprise.Core.Generation.Infrastructure;
using SyntheticEnterprise.Core.Generation.Organization;
using SyntheticEnterprise.Core.Export;
using SyntheticEnterprise.Core.Generation.Repository;
using SyntheticEnterprise.Core.Scenarios;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSyntheticEnterpriseCore(this IServiceCollection services)
    {
        services.AddSingleton<ICatalogLoader, FileSystemCatalogLoader>();
        services.AddSingleton<IScenarioLoader, JsonScenarioLoader>();
        services.AddSingleton<IWorldGenerator, DefaultWorldGenerator>();
        services.AddSingleton<ICatalogContextResolver, CatalogContextResolver>();
        services.AddSingleton<IWorldCloner, WorldCloner>();
        services.AddSingleton<ILayerProcessor, LayerProcessor>();

        services.AddSingleton<IOrganizationGenerator, BasicOrganizationGenerator>();
        services.AddSingleton<IGeographyGenerator, BasicGeographyGenerator>();
        services.AddSingleton<IIdentityGenerator, BasicIdentityGenerator>();
        services.AddSingleton<IInfrastructureGenerator, BasicInfrastructureGenerator>();
        services.AddSingleton<IRepositoryGenerator, BasicRepositoryGenerator>();

        services.AddSingleton<IAnomalyInjector, BasicIdentityAnomalyInjector>();
        services.AddSingleton<IAnomalyInjector, BasicInfrastructureAnomalyInjector>();
        services.AddSingleton<IAnomalyInjector, BasicRepositoryAnomalyInjector>();
        services.AddSingleton<IExporter, FileBundleExporter>();
        services.AddSingleton<IIdFactory, PlaceholderIdFactory>();
        services.AddSingleton<IRandomSource, PlaceholderRandomSource>();
        services.AddSingleton<IClock, SystemClock>();

        return services;
    }
}
