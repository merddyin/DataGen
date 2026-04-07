namespace SyntheticEnterprise.Core.Abstractions;

using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Models;

public interface IIdentityGenerator
{
    void GenerateIdentity(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs);
}
