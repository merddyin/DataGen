[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string]$Version,

    [Parameter()]
    [string]$RepositoryRoot = (Join-Path $PSScriptRoot '..')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$propsPath = Join-Path $repositoryRoot 'Directory.Build.props'
if (-not (Test-Path -LiteralPath $propsPath -PathType Leaf)) {
    throw "Authoritative release properties were not found at '$propsPath'."
}

[xml]$props = Get-Content -LiteralPath $propsPath -Raw
$versionNode = $props.SelectSingleNode('/Project/PropertyGroup/Version')
$assemblyVersionNode = $props.SelectSingleNode('/Project/PropertyGroup/AssemblyVersion')
$fileVersionNode = $props.SelectSingleNode('/Project/PropertyGroup/FileVersion')
$informationalVersionNode = $props.SelectSingleNode('/Project/PropertyGroup/InformationalVersion')

if ($null -eq $versionNode -or $null -eq $assemblyVersionNode -or $null -eq $fileVersionNode -or $null -eq $informationalVersionNode) {
    throw "Directory.Build.props must define Version, AssemblyVersion, FileVersion, and InformationalVersion."
}

$authoritativeVersion = $versionNode.InnerText.Trim()
$authoritativeAssemblyVersion = $assemblyVersionNode.InnerText.Trim()
$authoritativeFileVersion = $fileVersionNode.InnerText.Trim()
$authoritativeInformationalVersion = $informationalVersionNode.InnerText.Trim()

$versionParts = $authoritativeVersion.Split('.')
$expectedAssemblyVersion = if ($versionParts.Count -eq 3) { "$authoritativeVersion.0" } else { $authoritativeVersion }
if ($authoritativeAssemblyVersion -ne $expectedAssemblyVersion -or $authoritativeFileVersion -ne $expectedAssemblyVersion) {
    throw "Directory.Build.props version markers are inconsistent: Version '$authoritativeVersion' requires AssemblyVersion and FileVersion '$expectedAssemblyVersion'."
}

if ($authoritativeInformationalVersion -ne $authoritativeVersion) {
    throw "Directory.Build.props InformationalVersion '$authoritativeInformationalVersion' must match Version '$authoritativeVersion'."
}

if ($Version -ne $authoritativeVersion) {
    throw "Requested release version '$Version' does not match authoritative Directory.Build.props Version '$authoritativeVersion' (AssemblyVersion '$authoritativeAssemblyVersion')."
}

Write-Host "Release version '$Version' matches Directory.Build.props ($authoritativeAssemblyVersion)." -ForegroundColor Green
