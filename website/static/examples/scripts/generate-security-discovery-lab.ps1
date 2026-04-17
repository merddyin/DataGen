param(
    [string]$ScenarioPath = (Join-Path $PSScriptRoot '..\scenarios\security-discovery-lab.json'),
    [string]$OutputRoot = (Join-Path $PWD 'out\security-discovery-lab'),
    [int]$Seed = 8891
)

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$world = New-SEEnterpriseWorld -ScenarioPath $ScenarioPath -Seed $Seed
$world | Export-SEEnterpriseWorld -OutputPath (Join-Path $OutputRoot 'normalized') -Format Json -Profile Normalized -IncludeManifest -IncludeSummary -Overwrite | Out-Null
$world | Get-SEWorldSummary
