param(
    [string]$ScenarioPath = (Join-Path $PSScriptRoot '..\scenarios\general-enterprise-lab.json'),
    [string]$OutputRoot = (Join-Path $PWD 'out\general-enterprise-lab'),
    [int]$Seed = 4242
)

# Import the DataGen module before running this script.

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$world = New-SEEnterpriseWorld -ScenarioPath $ScenarioPath -Seed $Seed
$summary = $world | Get-SEWorldSummary
$snapshotPath = Join-Path $OutputRoot 'general-enterprise.seworld'
$exportPath = Join-Path $OutputRoot 'normalized'

$world | Save-SEEnterpriseWorld -Path $snapshotPath -Compress
$world | Export-SEEnterpriseWorld -OutputPath $exportPath -Format Json -Profile Normalized -IncludeManifest -IncludeSummary -Overwrite | Out-Null

$summary
