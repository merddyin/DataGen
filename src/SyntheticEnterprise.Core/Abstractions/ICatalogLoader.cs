namespace SyntheticEnterprise.Core.Abstractions;

using SyntheticEnterprise.Contracts.Abstractions;

public interface ICatalogLoader
{
    CatalogSet LoadFromPath(string rootPath);
    CatalogSet LoadDefault();
}
