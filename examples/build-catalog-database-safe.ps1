[CmdletBinding()]
param(
    [string]$CatalogRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\catalogs')).Path,
    [string]$OutputPath,
    [string]$OriginRoot = "D:\Dev\Codex\DataGen",
    [string]$RawNamesRoot = "F:\Dev\Names",
    [switch]$IncludeRawNamesCache
)

if (-not $OutputPath) {
    $OutputPath = Join-Path $CatalogRoot "catalogs.sqlite"
}

$modulePath = (Resolve-Path (Join-Path $PSScriptRoot '..\src\SyntheticEnterprise.PowerShell\bin\Debug\net8.0\SyntheticEnterprise.PowerShell.dll')).Path

Import-Module $modulePath -Force

New-SECatalogDatabase `
    -CatalogRootPath $CatalogRoot `
    -OutputPath $OutputPath `
    -OriginRoot $OriginRoot `
    -RawNamesRoot $RawNamesRoot `
    -IncludeRawNamesCache:$IncludeRawNamesCache
