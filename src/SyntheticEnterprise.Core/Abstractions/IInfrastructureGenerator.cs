namespace SyntheticEnterprise.Core.Abstractions;

using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Models;

public interface IInfrastructureGenerator
{
    void GenerateInfrastructure(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs);
}
