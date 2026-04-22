[CmdletBinding()]
param(
    [Parameter()]
    [string]$CatalogRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\catalogs')).Path,

    [Parameter()]
    [string]$OutputPath = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..')).Path 'artifacts\catalog\catalogs.sqlite'),

    [Parameter()]
    [string]$InstallPath = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..')).Path 'catalogs\catalogs.sqlite'),

    [Parameter()]
    [string]$OriginRoot,

    [Parameter()]
    [string]$RawNamesRoot,

    [Parameter()]
    [switch]$IncludeRawNamesCache,

    [Parameter()]
    [switch]$IncludeUncuratedSources,

    [Parameter()]
    [string]$CompareTo,

    [Parameter()]
    [switch]$InstallToCatalogRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$toolProject = Join-Path $repoRoot 'src\SyntheticEnterprise.CatalogTool\SyntheticEnterprise.CatalogTool.csproj'

function Resolve-RepoAwarePath([string]$PathValue) {
    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $repoRoot $PathValue))
}

$resolvedCatalogRoot = (Resolve-Path $CatalogRoot).Path
$resolvedOutputPath = Resolve-RepoAwarePath $OutputPath
$resolvedInstallPath = Resolve-RepoAwarePath $InstallPath

New-Item -ItemType Directory -Path ([System.IO.Path]::GetDirectoryName($resolvedOutputPath)) -Force | Out-Null

$buildArguments = @(
    'run',
    '--project', $toolProject,
    '--',
    'build',
    '--catalog-root', $resolvedCatalogRoot,
    '--output', $resolvedOutputPath
)

if ($OriginRoot -and (Test-Path $OriginRoot)) {
    $buildArguments += @('--origin-root', (Resolve-Path $OriginRoot).Path)
}

if ($RawNamesRoot -and (Test-Path $RawNamesRoot)) {
    $buildArguments += @('--raw-names-root', (Resolve-Path $RawNamesRoot).Path)
}

if ($IncludeRawNamesCache.IsPresent) {
    $buildArguments += '--include-raw-names-cache'
}

if ($IncludeUncuratedSources.IsPresent) {
    $buildArguments += '--include-uncurated-sources'
}

dotnet @buildArguments
if ($LASTEXITCODE -ne 0) {
    throw "Catalog build failed with exit code $LASTEXITCODE."
}

if ($CompareTo) {
    $resolvedComparePath = (Resolve-Path $CompareTo).Path
    $compareArguments = @(
        'run',
        '--project', $toolProject,
        '--',
        'compare',
        '--left', $resolvedOutputPath,
        '--right', $resolvedComparePath
    )

    dotnet @compareArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Generated catalog did not match comparison target '$resolvedComparePath'."
    }
}

if ($InstallToCatalogRoot.IsPresent) {
    New-Item -ItemType Directory -Path ([System.IO.Path]::GetDirectoryName($resolvedInstallPath)) -Force | Out-Null
    Copy-Item $resolvedOutputPath $resolvedInstallPath -Force
}

Write-Host "Catalog artifact generated at $resolvedOutputPath" -ForegroundColor Green
if ($InstallToCatalogRoot.IsPresent) {
    Write-Host "Installed working catalog to $resolvedInstallPath" -ForegroundColor Green
}
