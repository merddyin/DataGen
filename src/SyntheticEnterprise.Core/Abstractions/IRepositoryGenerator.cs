namespace SyntheticEnterprise.Core.Abstractions;

using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Models;

public interface IRepositoryGenerator
{
    void GenerateRepositories(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs);
}
