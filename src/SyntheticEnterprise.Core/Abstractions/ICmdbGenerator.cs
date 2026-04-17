namespace SyntheticEnterprise.Core.Abstractions;

using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Models;

public interface ICmdbGenerator
{
    void GenerateConfigurationManagement(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs);
}
