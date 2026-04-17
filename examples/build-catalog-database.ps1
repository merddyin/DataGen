[CmdletBinding()]
param(
    [string]$CatalogRoot = (Resolve-Path "..\catalogs").Path,
    [string]$OutputPath,
    [string]$OriginRoot = "D:\Dev\Codex\DataGen",
    [string]$RawNamesRoot = "F:\Dev\Names",
    [switch]$IncludeRawNamesCache
)

if (-not $OutputPath) {
    $OutputPath = Join-Path $CatalogRoot "catalogs.sqlite"
}

$modulePath = (Resolve-Path "..\src\SyntheticEnterprise.PowerShell\bin\Debug\net8.0\SyntheticEnterprise.PowerShell.dll").Path
$corePath = (Resolve-Path "..\src\SyntheticEnterprise.PowerShell\bin\Debug\net8.0\SyntheticEnterprise.Core.dll").Path

Import-Module $modulePath -Force
$coreAssembly = [System.Reflection.Assembly]::LoadFrom($corePath)

$builderType = $coreAssembly.GetType("SyntheticEnterprise.Core.Catalogs.CatalogSqliteDatabaseBuilder", $true)
$buildMethod = $builderType.GetMethod("Build", [System.Reflection.BindingFlags] "Static, Public, NonPublic")
$sources = [System.Collections.Generic.List[string]]::new()
$sources.Add($CatalogRoot)
if (Test-Path $OriginRoot) {
    $sources.Add($OriginRoot)
}
if ($IncludeRawNamesCache -and (Test-Path $RawNamesRoot)) {
    $sources.Add($RawNamesRoot)
}

$null = $buildMethod.Invoke($null, [object[]]@([string]$outputPath, [string[]]$sources.ToArray()))
Write-Output $outputPath
