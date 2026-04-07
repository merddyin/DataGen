namespace SyntheticEnterprise.Core.Abstractions;

using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Models;

public interface IAnomalyInjector
{
    void Apply(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs, AnomalyProfile profile);
}
