[CmdletBinding()]
param(
    [Parameter()]
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string]$Version = '0.2.0',

    [Parameter()]
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [Parameter()]
    [string]$ProjectPath = 'src/SyntheticEnterprise.PowerShell/SyntheticEnterprise.PowerShell.csproj',

    [Parameter()]
    [string]$OutputRoot = 'artifacts/module',

    [Parameter()]
    [string]$ModuleName = 'SyntheticEnterprise.PowerShell',

    [Parameter()]
    [string]$PowerShellVersion = '7.4'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$projectFullPath = Join-Path $repoRoot $ProjectPath
$catalogDatabasePath = Join-Path $repoRoot 'catalogs\catalogs.sqlite'

if (-not (Test-Path $catalogDatabasePath)) {
    Write-Host "Seeded catalog database missing; generating it before module packaging..." -ForegroundColor Yellow
    & (Join-Path $PSScriptRoot 'build-catalog-artifact.ps1') -InstallToCatalogRoot
}

if (-not (Test-Path $projectFullPath)) {
    throw "Module project not found at '$projectFullPath'."
}

Write-Host "Building $ModuleName ($Configuration)..." -ForegroundColor Cyan
dotnet build $projectFullPath -c $Configuration -v minimal
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE."
}

$projectDirectory = Split-Path -Parent $projectFullPath
$targetFramework = 'net8.0'
$buildOutput = Join-Path $projectDirectory "bin\$Configuration\$targetFramework"

if (-not (Test-Path $buildOutput)) {
    throw "Expected build output was not found at '$buildOutput'."
}

$moduleStageRoot = Join-Path $repoRoot $OutputRoot
$moduleStagePath = Join-Path $moduleStageRoot $ModuleName
$versionedStagePath = Join-Path $moduleStagePath $Version
$publishStagePath = Join-Path (Join-Path $moduleStageRoot 'publish') $ModuleName
$zipPath = Join-Path $moduleStageRoot "$ModuleName-$Version.zip"

if (Test-Path $versionedStagePath) {
    Remove-Item $versionedStagePath -Recurse -Force
}

if (Test-Path $publishStagePath) {
    Remove-Item $publishStagePath -Recurse -Force
}

New-Item -ItemType Directory -Path $versionedStagePath -Force | Out-Null

Write-Host "Staging module files..." -ForegroundColor Cyan
Copy-Item (Join-Path $buildOutput '*') $versionedStagePath -Recurse -Force

foreach ($transientPath in @('ref', 'refint')) {
    $fullPath = Join-Path $versionedStagePath $transientPath
    if (Test-Path $fullPath) {
        Remove-Item $fullPath -Recurse -Force
    }
}

$moduleDll = Join-Path $versionedStagePath "$ModuleName.dll"
if (-not (Test-Path $moduleDll)) {
    throw "Expected module binary was not found at '$moduleDll'."
}

Write-Host "Discovering exported cmdlets..." -ForegroundColor Cyan
$importedModule = Import-Module $moduleDll -Force -PassThru
$cmdletsToExport = Get-Command -Module $importedModule.Name |
    Where-Object CommandType -eq 'Cmdlet' |
    Select-Object -ExpandProperty Name |
    Sort-Object -Unique

if (-not $cmdletsToExport) {
    throw "No cmdlets were discovered for module '$($importedModule.Name)'."
}

$manifestPath = Join-Path $versionedStagePath "$ModuleName.psd1"
$moduleGuid = '9c0e5d72-daa5-4f5c-9ce7-5d3d5072669f'

Write-Host "Creating module manifest..." -ForegroundColor Cyan
New-ModuleManifest `
    -Path $manifestPath `
    -RootModule "$ModuleName.dll" `
    -ModuleVersion $Version `
    -Guid $moduleGuid `
    -Author 'OpenAI / DataGen contributors' `
    -CompanyName 'DataGen' `
    -Copyright '(c) DataGen contributors' `
    -Description 'Synthetic enterprise data generation platform for labs, demos, exports, and downstream validation.' `
    -CompatiblePSEditions @('Core') `
    -PowerShellVersion $PowerShellVersion `
    -CmdletsToExport $cmdletsToExport `
    -FunctionsToExport @() `
    -AliasesToExport @() `
    -VariablesToExport @() `
    -Tags @('DataGen', 'SyntheticData', 'PowerShell', 'Enterprise') `
    -ProjectUri 'https://github.com/merddyin/DataGen' | Out-Null

New-Item -ItemType Directory -Path $publishStagePath -Force | Out-Null
foreach ($item in Get-ChildItem -Path $versionedStagePath -Force) {
    $destination = Join-Path $publishStagePath $item.Name
    if ($item.PSIsContainer) {
        Copy-Item $item.FullName $destination -Recurse -Force
    }
    else {
        Copy-Item $item.FullName $destination -Force
    }
}

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Write-Host "Compressing module package..." -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $moduleStagePath '*') -DestinationPath $zipPath -Force

Write-Host ''
Write-Host "Module package created:" -ForegroundColor Green
Write-Host "  Folder: $versionedStagePath"
Write-Host "  Gallery: $publishStagePath"
Write-Host "  Zip:    $zipPath"
