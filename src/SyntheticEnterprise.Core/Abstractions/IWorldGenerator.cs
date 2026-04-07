namespace SyntheticEnterprise.Core.Abstractions;

using SyntheticEnterprise.Contracts.Abstractions;

public interface IWorldGenerator
{
    GenerationResult Generate(GenerationContext context, CatalogSet catalogs);
}
