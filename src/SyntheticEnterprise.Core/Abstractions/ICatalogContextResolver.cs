namespace SyntheticEnterprise.Core.Abstractions;

using SyntheticEnterprise.Contracts.Abstractions;

public interface ICatalogContextResolver
{
    CatalogSet Resolve(GenerationResult input);
}
