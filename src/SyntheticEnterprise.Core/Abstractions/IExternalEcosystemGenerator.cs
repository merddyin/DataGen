namespace SyntheticEnterprise.Core.Abstractions;

using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Models;

public interface IExternalEcosystemGenerator
{
    void GenerateEcosystem(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs);
}
