namespace SyntheticEnterprise.Core.DependencyInjection;

using Microsoft.Extensions.DependencyInjection;
using SyntheticEnterprise.Core.Abstractions;
using SyntheticEnterprise.Core.Catalogs;
using SyntheticEnterprise.Core.Generation;
using SyntheticEnterprise.Core.Generation.Applications;
using SyntheticEnterprise.Core.Generation.Cmdb;
using SyntheticEnterprise.Core.Generation.Geography;
using SyntheticEnterprise.Core.Generation.Identity;
using SyntheticEnterprise.Core.Generation.Infrastructure;
using SyntheticEnterprise.Core.Generation.Organization;
using SyntheticEnterprise.Core.Generation.Observed;
using SyntheticEnterprise.Core.Plugins;
using SyntheticEnterprise.Core.Export;
using SyntheticEnterprise.Core.Generation.Repository;
using SyntheticEnterprise.Core.Scenarios;
using SyntheticEnterprise.Core.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSyntheticEnterpriseCore(this IServiceCollection services)
    {
        services.AddSingleton<ICatalogLoader, FileSystemCatalogLoader>();
        services.AddSingleton<IScenarioLoader, JsonScenarioLoader>();
        services.AddSingleton<IScenarioTemplateRegistry, ScenarioTemplateRegistry>();
        services.AddSingleton<IScenarioArchetypeRegistry, ScenarioTemplateRegistry>();
        services.AddSingleton<IScenarioPersonaRegistry, ScenarioTemplateRegistry>();
        services.AddSingleton<IScenarioOverlayService, ScenarioOverlayService>();
        services.AddSingleton<IScenarioPersonaPresetService, ScenarioPersonaPresetService>();
        services.AddSingleton<IScenarioDefaultsResolver, ScenarioDefaultsResolver>();
        services.AddSingleton<IFirstPartyPackPathResolver, FirstPartyPackPathResolver>();
        services.AddSingleton<IScenarioPackProfileResolver, ScenarioPackProfileResolver>();
        services.AddSingleton<IScenarioPluginProfileHydrator, ScenarioPluginProfileHydrator>();
        services.AddSingleton<IScenarioPluginContributionResolver, ScenarioPluginContributionResolver>();
        services.AddSingleton<IScenarioValidator, ScenarioValidator>();
        services.AddSingleton<IWorldGenerator, DefaultWorldGenerator>();
        services.AddSingleton<ICatalogContextResolver, CatalogContextResolver>();
        services.AddSingleton<IWorldCloner, WorldCloner>();
        services.AddSingleton<ILayerProcessor, LayerProcessor>();
        services.AddSingleton<IWorldLayerRemapService, WorldLayerRemapService>();
        services.AddSingleton<ILayerOwnershipRegistry, DefaultLayerOwnershipRegistry>();
        services.AddSingleton<IWorldOwnershipReconciliationService, WorldOwnershipReconciliationService>();
        services.AddSingleton<IWorldReferenceRepairService, WorldReferenceRepairService>();
        services.AddSingleton<IWorldInvariantValidator, WorldInvariantValidator>();
        services.AddSingleton<IWorldQualityAuditService, WorldQualityAuditService>();
        services.AddSingleton<ITemporalSimulationService, TemporalSimulationService>();

        services.AddSingleton<IOrganizationGenerator, BasicOrganizationGenerator>();
        services.AddSingleton<IGeographyGenerator, BasicGeographyGenerator>();
        services.AddSingleton<IIdentityGenerator, BasicIdentityGenerator>();
        services.AddSingleton<IApplicationGenerator, BasicApplicationGenerator>();
        services.AddSingleton<IBusinessProcessGenerator, BasicBusinessProcessGenerator>();
        services.AddSingleton<IApplicationTopologyGenerator, BasicApplicationTopologyGenerator>();
        services.AddSingleton<ICloudTenantGenerator, BasicCloudTenantGenerator>();
        services.AddSingleton<IExternalEcosystemGenerator, BasicExternalEcosystemGenerator>();
        services.AddSingleton<IObservedDataGenerator, BasicObservedDataGenerator>();
        services.AddSingleton<IInfrastructureGenerator, BasicInfrastructureGenerator>();
        services.AddSingleton<IRepositoryGenerator, BasicRepositoryGenerator>();
        services.AddSingleton<ICmdbGenerator, BasicCmdbGenerator>();

        services.AddSingleton<IAnomalyInjector, BasicIdentityAnomalyInjector>();
        services.AddSingleton<IAnomalyInjector, BasicInfrastructureAnomalyInjector>();
        services.AddSingleton<IAnomalyInjector, BasicRepositoryAnomalyInjector>();
        services.AddSingleton<IGenerationPluginExecutionPlanner, GenerationPluginExecutionPlanner>();
        services.AddSingleton<IExternalPluginScenarioBindingService, ExternalPluginScenarioBindingService>();
        services.AddSingleton<IExternalPluginCapabilityResolver, ExternalPluginCapabilityResolver>();
        services.AddSingleton<IExternalPluginOrchestrator, ExternalPluginOrchestrator>();
        services.AddSingleton<IExternalPluginTrustPolicy, AllowListExternalPluginTrustPolicy>();
        services.AddSingleton<IGenerationPluginSecurityPolicy, DataOnlyGenerationPluginSecurityPolicy>();
        services.AddSingleton<IGenerationPluginManifestValidator, GenerationPluginManifestValidator>();
        services.AddSingleton<IExternalGenerationPluginCatalog, FileSystemExternalGenerationPluginCatalog>();
        services.AddSingleton<IGenerationPluginRegistry, GenerationPluginRegistry>();
        services.AddSingleton<IGenerationPluginPackageValidator, GenerationPluginPackageValidator>();
        services.AddSingleton<IGenerationPluginRegistrationStore, JsonGenerationPluginRegistrationStore>();
        services.AddSingleton<IGenerationPluginRegistrationService, GenerationPluginRegistrationService>();
        services.AddSingleton<IGenerationPluginInstallationService, GenerationPluginInstallationService>();
        services.AddSingleton<IExternalPluginHostAdapter, RestrictedPowerShellExternalPluginHostAdapter>();
        services.AddSingleton<IExternalPluginHostAdapter, OutOfProcessAssemblyExternalPluginHostAdapter>();
        services.AddSingleton<IWorldGenerationPlugin, OrganizationGenerationPlugin>();
        services.AddSingleton<IWorldGenerationPlugin, GeographyGenerationPlugin>();
        services.AddSingleton<IWorldGenerationPlugin, IdentityGenerationPlugin>();
        services.AddSingleton<IWorldGenerationPlugin, ApplicationGenerationPlugin>();
        services.AddSingleton<IWorldGenerationPlugin, InfrastructureGenerationPlugin>();
        services.AddSingleton<IWorldGenerationPlugin, BusinessProcessGenerationPlugin>();
        services.AddSingleton<IWorldGenerationPlugin, ApplicationTopologyGenerationPlugin>();
        services.AddSingleton<IWorldGenerationPlugin, CloudTenancyGenerationPlugin>();
        services.AddSingleton<IWorldGenerationPlugin, ExternalEcosystemGenerationPlugin>();
        services.AddSingleton<IWorldGenerationPlugin, ObservedDataGenerationPlugin>();
        services.AddSingleton<IWorldGenerationPlugin, RepositoryGenerationPlugin>();
        services.AddSingleton<IWorldGenerationPlugin, CmdbGenerationPlugin>();
        services.AddSingleton<IWorldGenerationPlugin, AnomalyGenerationPlugin>();
        services.AddSingleton<IExporter, FileBundleExporter>();
        services.AddSingleton<IIdFactory, PlaceholderIdFactory>();
        services.AddSingleton<IRandomSource, PlaceholderRandomSource>();
        services.AddSingleton<IClock, SystemClock>();

        return services;
    }
}
