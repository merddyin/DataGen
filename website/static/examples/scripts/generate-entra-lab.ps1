param(
    [string]$ScenarioPath = (Join-Path $PSScriptRoot '..\scenarios\entra-lab.json'),
    [string]$OutputRoot = (Join-Path $PWD 'out\entra-lab'),
    [int]$Seed = 2307
)

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$world = New-SEEnterpriseWorld -ScenarioPath $ScenarioPath -Seed $Seed
$world | Export-SEEnterpriseWorld -OutputPath (Join-Path $OutputRoot 'normalized') -Format Json -Profile Normalized -IncludeManifest -IncludeSummary -Overwrite | Out-Null
$world | Get-SEWorldSummary
