namespace SyntheticEnterprise.Core.Generation;

using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Core.Abstractions;

public sealed class DefaultWorldGenerator : IWorldGenerator
{
    private readonly IOrganizationGenerator _organizationGenerator;
    private readonly IGeographyGenerator _geographyGenerator;
    private readonly IIdentityGenerator _identityGenerator;
    private readonly IInfrastructureGenerator _infrastructureGenerator;
    private readonly IRepositoryGenerator _repositoryGenerator;
    private readonly IEnumerable<IAnomalyInjector> _anomalyInjectors;

    public DefaultWorldGenerator(
        IOrganizationGenerator organizationGenerator,
        IGeographyGenerator geographyGenerator,
        IIdentityGenerator identityGenerator,
        IInfrastructureGenerator infrastructureGenerator,
        IRepositoryGenerator repositoryGenerator,
        IEnumerable<IAnomalyInjector> anomalyInjectors)
    {
        _organizationGenerator = organizationGenerator;
        _geographyGenerator = geographyGenerator;
        _identityGenerator = identityGenerator;
        _infrastructureGenerator = infrastructureGenerator;
        _repositoryGenerator = repositoryGenerator;
        _anomalyInjectors = anomalyInjectors;
    }

    public GenerationResult Generate(GenerationContext context, CatalogSet catalogs)
    {
        var world = new SyntheticEnterpriseWorld();

        _organizationGenerator.GenerateOrganizations(world, context, catalogs);
        _geographyGenerator.GenerateOffices(world, context, catalogs);
        _identityGenerator.GenerateIdentity(world, context, catalogs);
        _infrastructureGenerator.GenerateInfrastructure(world, context, catalogs);
        _repositoryGenerator.GenerateRepositories(world, context, catalogs);

        foreach (var anomalyProfile in context.Scenario.Anomalies)
        {
            foreach (var injector in _anomalyInjectors)
            {
                injector.Apply(world, context, catalogs, anomalyProfile);
            }
        }

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
                RepositoryCount = world.Databases.Count + world.FileShares.Count + world.CollaborationSites.Count
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
                AppliedLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Organization", "Geography", "Identity", "Infrastructure", "Repository" }
            }
        };
    }
}
