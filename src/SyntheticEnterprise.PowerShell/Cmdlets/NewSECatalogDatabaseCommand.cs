namespace SyntheticEnterprise.PowerShell.Cmdlets;

using System.Management.Automation;
using SyntheticEnterprise.Core.Catalogs;

[Cmdlet(VerbsCommon.New, "SECatalogDatabase")]
[OutputType(typeof(string))]
public sealed class NewSECatalogDatabaseCommand : PSCmdlet
{
    [Parameter(Mandatory = true)]
    public string CatalogRootPath { get; set; } = string.Empty;

    [Parameter(Mandatory = false)]
    public string? OutputPath { get; set; }

    [Parameter(Mandatory = false)]
    public string? OriginRoot { get; set; }

    [Parameter(Mandatory = false)]
    public string? RawNamesRoot { get; set; }

    [Parameter(Mandatory = false)]
    public SwitchParameter IncludeRawNamesCache { get; set; }

    [Parameter(Mandatory = false)]
    public SwitchParameter IncludeUncuratedSources { get; set; }

    protected override void ProcessRecord()
    {
        var catalogRoot = GetUnresolvedProviderPathFromPSPath(CatalogRootPath);
        var outputPath = string.IsNullOrWhiteSpace(OutputPath)
            ? Path.Combine(catalogRoot, "catalogs.sqlite")
            : GetUnresolvedProviderPathFromPSPath(OutputPath);

        var sourceRoots = new List<string>
        {
            catalogRoot
        };

        if (!string.IsNullOrWhiteSpace(OriginRoot))
        {
            var resolvedOriginRoot = GetUnresolvedProviderPathFromPSPath(OriginRoot);
            if (Directory.Exists(resolvedOriginRoot))
            {
                sourceRoots.Add(resolvedOriginRoot);
            }
        }

        if (IncludeRawNamesCache.IsPresent && !string.IsNullOrWhiteSpace(RawNamesRoot))
        {
            var resolvedRawNamesRoot = GetUnresolvedProviderPathFromPSPath(RawNamesRoot);
            if (Directory.Exists(resolvedRawNamesRoot))
            {
                sourceRoots.Add(resolvedRawNamesRoot);
            }
        }

        CatalogSqliteDatabaseBuilder.Build(outputPath, sourceRoots, IncludeUncuratedSources.IsPresent);
        WriteObject(outputPath);
    }
}
