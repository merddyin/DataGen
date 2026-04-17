namespace SyntheticEnterprise.Core.Abstractions;

using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Models;

public interface IApplicationTopologyGenerator
{
    void GenerateTopology(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs);
}
