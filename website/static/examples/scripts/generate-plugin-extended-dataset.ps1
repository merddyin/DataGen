param(
    [string]$ScenarioPath = (Join-Path $PSScriptRoot '..\scenarios\plugin-extended-dataset.json'),
    [string]$OutputRoot = (Join-Path $PWD 'out\plugin-extended-dataset'),
    [string]$PluginRootPath,
    [string]$PluginCapability,
    [int]$Seed = 7721
)

if ([string]::IsNullOrWhiteSpace($PluginRootPath) -or [string]::IsNullOrWhiteSpace($PluginCapability)) {
    throw 'Provide -PluginRootPath and -PluginCapability to run the plugin walkthrough script.'
}

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$world = New-SEEnterpriseWorld `
    -ScenarioPath $ScenarioPath `
    -Seed $Seed `
    -PluginRootPath $PluginRootPath `
    -EnablePluginCapability $PluginCapability

$world | Export-SEEnterpriseWorld -OutputPath (Join-Path $OutputRoot 'normalized') -Format Json -Profile Normalized -IncludeManifest -IncludeSummary -Overwrite | Out-Null
$world | Get-SEWorldSummary
