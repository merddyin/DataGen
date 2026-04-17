namespace SyntheticEnterprise.Core.Abstractions;

using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Models;

public interface IObservedDataGenerator
{
    void GenerateObservedData(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs);
}
