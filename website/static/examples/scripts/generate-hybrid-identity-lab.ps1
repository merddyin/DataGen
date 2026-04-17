param(
    [string]$ScenarioPath = (Join-Path $PSScriptRoot '..\scenarios\hybrid-identity-lab.json'),
    [string]$OutputRoot = (Join-Path $PWD 'out\hybrid-identity-lab'),
    [int]$Seed = 5079
)

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$world = New-SEEnterpriseWorld -ScenarioPath $ScenarioPath -Seed $Seed
$world | Save-SEEnterpriseWorld -Path (Join-Path $OutputRoot 'hybrid-identity-lab.seworld') -Compress
$world | Export-SEEnterpriseWorld -OutputPath (Join-Path $OutputRoot 'normalized') -Format Json -Profile Normalized -IncludeManifest -IncludeSummary -Overwrite | Out-Null
$world | Get-SEWorldSummary
