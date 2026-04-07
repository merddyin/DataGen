namespace SyntheticEnterprise.Core.Abstractions;

using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Models;

public interface IOrganizationGenerator
{
    void GenerateOrganizations(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs);
}
