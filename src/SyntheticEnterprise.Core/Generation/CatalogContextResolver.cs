namespace SyntheticEnterprise.Core.Generation;

using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Core.Abstractions;

public sealed class CatalogContextResolver : ICatalogContextResolver
{
    private readonly ICatalogLoader _catalogLoader;

    public CatalogContextResolver(ICatalogLoader catalogLoader)
    {
        _catalogLoader = catalogLoader;
    }

    public CatalogSet Resolve(GenerationResult input)
    {
        if (input.Catalogs.CsvCatalogs.Count > 0 || input.Catalogs.JsonCatalogs.Count > 0)
        {
            return input.Catalogs;
        }

        var path = input.WorldMetadata?.CatalogRootPath;
        if (!string.IsNullOrWhiteSpace(path))
        {
            try
            {
                return _catalogLoader.LoadFromPath(path);
            }
            catch
            {
                return new CatalogSet();
            }
        }

        return _catalogLoader.LoadDefault();
    }
}
