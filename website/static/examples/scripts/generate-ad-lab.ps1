param(
    [string]$ScenarioPath = (Join-Path $PSScriptRoot '..\scenarios\active-directory-lab.json'),
    [string]$OutputRoot = (Join-Path $PWD 'out\ad-lab'),
    [int]$Seed = 1401
)

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$scenario = Resolve-SEScenario -Path $ScenarioPath
$scenario | Test-SEScenario | Out-Host

$world = New-SEEnterpriseWorld -Scenario $scenario -Seed $Seed
$world | Save-SEEnterpriseWorld -Path (Join-Path $OutputRoot 'ad-lab.seworld') -Compress
$world | Export-SEEnterpriseWorld -OutputPath (Join-Path $OutputRoot 'normalized') -Format Json -Profile Normalized -IncludeManifest -IncludeSummary -Overwrite | Out-Null
$world | Get-SEWorldSummary
