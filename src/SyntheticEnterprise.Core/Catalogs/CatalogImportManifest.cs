namespace SyntheticEnterprise.Core.Catalogs;

internal sealed class CatalogImportManifest
{
    public string Version { get; init; } = "1";
    public List<CatalogImportDefinition> Tables { get; init; } = new();
}

internal sealed class CatalogImportDefinition
{
    public required string TableName { get; init; }
    public required string Strategy { get; init; }
    public List<string> SourceFiles { get; init; } = new();
    public Dictionary<string, string> Options { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
