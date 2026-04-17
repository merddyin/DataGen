namespace SyntheticEnterprise.Core.Abstractions;

using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Models;

public interface ICloudTenantGenerator
{
    void GenerateTenants(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs);
}
