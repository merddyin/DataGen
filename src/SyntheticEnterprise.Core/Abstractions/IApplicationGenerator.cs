namespace SyntheticEnterprise.Core.Abstractions;

using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Models;

public interface IApplicationGenerator
{
    void GenerateApplications(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs);
}
