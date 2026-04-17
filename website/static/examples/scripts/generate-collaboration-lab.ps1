param(
    [string]$ScenarioPath = (Join-Path $PSScriptRoot '..\scenarios\collaboration-and-repositories.json'),
    [string]$OutputRoot = (Join-Path $PWD 'out\collaboration-lab'),
    [int]$Seed = 6422
)

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$world = New-SEEnterpriseWorld -ScenarioPath $ScenarioPath -Seed $Seed
$world | Export-SEEnterpriseWorld -OutputPath (Join-Path $OutputRoot 'normalized') -Format Json -Profile Normalized -IncludeManifest -IncludeSummary -Overwrite | Out-Null
$world | Get-SEWorldSummary
