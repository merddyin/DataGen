namespace SyntheticEnterprise.Contracts.Configuration;

public record GeneratorOptions
{
    public string? CatalogRootPath { get; init; }
    public int? Seed { get; init; }
    public bool StrictMode { get; init; } = false;
    public bool IncludeGroundTruthMetadata { get; init; } = true;
    public double DefaultAnomalyIntensity { get; init; } = 1.0;
    public ExportOptions Export { get; init; } = new();
}

public record ExportOptions
{
    public string Format { get; init; } = "Csv";
    public string? OutputPath { get; init; }
    public bool FlattenRelationships { get; init; } = true;
    public bool EmitLinkTables { get; init; } = true;
    public bool EmitManifest { get; init; } = true;
    public bool CreateSubfolderPerCompany { get; init; } = false;
}
