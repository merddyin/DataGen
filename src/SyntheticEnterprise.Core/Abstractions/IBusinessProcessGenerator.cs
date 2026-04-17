namespace SyntheticEnterprise.Core.Abstractions;

using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Models;

public interface IBusinessProcessGenerator
{
    void GenerateBusinessProcesses(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs);
}
