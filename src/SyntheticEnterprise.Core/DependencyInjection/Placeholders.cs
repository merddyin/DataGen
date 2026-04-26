namespace SyntheticEnterprise.Core.DependencyInjection;

using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Core.Abstractions;

internal sealed class PlaceholderCatalogLoader : ICatalogLoader
{
    public CatalogSet LoadDefault() => new();
    public CatalogSet LoadFromPath(string rootPath) => new();
}

internal sealed class PlaceholderScenarioLoader : IScenarioLoader
{
    public ScenarioDefinition LoadFromJson(string json) => throw new NotImplementedException();
    public ScenarioDefinition LoadFromPath(string path) => throw new NotImplementedException();
}

internal sealed class PlaceholderOrganizationGenerator : IOrganizationGenerator
{
    public void GenerateOrganizations(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs) { }
}

internal sealed class PlaceholderGeographyGenerator : IGeographyGenerator
{
    public void GenerateOffices(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs) { }
}

internal sealed class PlaceholderIdentityGenerator : IIdentityGenerator
{
    public void GenerateIdentity(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs) { }
}

internal sealed class PlaceholderInfrastructureGenerator : IInfrastructureGenerator
{
    public void GenerateInfrastructure(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs) { }
}

internal sealed class PlaceholderRepositoryGenerator : IRepositoryGenerator
{
    public void GenerateRepositories(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs) { }
}

internal sealed class PlaceholderAnomalyInjector : IAnomalyInjector
{
    public void Apply(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs, AnomalyProfile profile) { }
}

internal sealed class PlaceholderExporter : IExporter
{
    public ExportResult Export(GenerationResult result, ExportOptions options)
        => throw new NotImplementedException();
}

internal sealed class PlaceholderIdFactory : IIdFactory
{
    private int _counter;
    public string Next(string entityType) => $"{entityType}-{Interlocked.Increment(ref _counter):D6}";
}

internal sealed class PlaceholderRandomSource : IRandomSource
{
    private Random _random = new();
    public void Reseed(int? seed) => _random = seed is int value ? new Random(value) : new Random();
    public int Next() => _random.Next();
    public int Next(int maxValue) => _random.Next(maxValue);
    public int Next(int minValue, int maxValue) => _random.Next(minValue, maxValue);
    public double NextDouble() => _random.NextDouble();
}

internal sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
