namespace SyntheticEnterprise.Core.Abstractions;

using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Models;

public interface IGeographyGenerator
{
    void GenerateOffices(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs);
}
