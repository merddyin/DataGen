[CmdletBinding()]
param(
    [string[]]$ScenarioPath = @(),
    [int[]]$Seed = @(4242, 4242, 7777),
    [string]$CatalogRoot,
    [string]$OutputPath,
    [string]$JsonOutputPath,
    [ValidateSet('Markdown', 'Json', 'Both')]
    [string]$OutputFormat = 'Both',
    [ValidateSet('None', 'Warn', 'Fail')]
    [string]$FailOnStatus = 'None'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$modulePath = Join-Path $repoRoot 'src\SyntheticEnterprise.PowerShell\bin\Debug\net8.0\SyntheticEnterprise.PowerShell.dll'

if ($ScenarioPath.Count -eq 0) {
    $ScenarioPath = @(
        (Join-Path $repoRoot 'examples\regional_manufacturer.scenario.json'),
        (Join-Path $repoRoot 'examples\regional_manufacturer.scenario.json'),
        (Join-Path $repoRoot 'examples\regional_manufacturer.scenario.json')
    )
}

if (-not (Test-Path $modulePath)) {
    throw "PowerShell module build output was not found at '$modulePath'. Build the solution before running the realism review."
}

if ($ScenarioPath.Count -ne $Seed.Count) {
    throw "ScenarioPath count ($($ScenarioPath.Count)) must match Seed count ($($Seed.Count))."
}

Import-Module $modulePath -Force
$auditService = [SyntheticEnterprise.Core.Services.WorldQualityAuditService]::new()
$validationType = [SyntheticEnterprise.Core.Services.WorldQualityValidationService]
$validationResultType = [SyntheticEnterprise.Contracts.Abstractions.WorldQualityValidationScenarioResult]

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('# DataGen Realism Review')
$lines.Add('')
$lines.Add("Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')")
$lines.Add('')
$scenarioResults = [System.Collections.Generic.List[SyntheticEnterprise.Contracts.Abstractions.WorldQualityValidationScenarioResult]]::new()

for ($index = 0; $index -lt $ScenarioPath.Count; $index++) {
    $path = $ScenarioPath[$index]
    $currentSeed = $Seed[$index]

    if (-not (Test-Path $path)) {
        throw "Scenario path not found: '$path'."
    }

    $generationParams = @{
        ScenarioPath = $path
        Seed = $currentSeed
    }

    if (-not [string]::IsNullOrWhiteSpace($CatalogRoot)) {
        $generationParams.CatalogRootPath = $CatalogRoot
    }

    $result = New-SEEnterpriseWorld @generationParams
    $audit = $auditService.Audit($result.World)
    $quality = $result.Quality
    $validation = $validationType::EvaluateScenario($path, $currentSeed, $quality)
    $scenarioResults.Add($validation)

    $lines.Add("## $(Split-Path $path -Leaf) / seed $currentSeed")
    $lines.Add('')
    $lines.Add("| Validation | Value |")
    $lines.Add("| --- | --- |")
    $lines.Add("| status | $($validation.Status) |")
    $lines.Add("| messages | $(if($validation.Messages.Count -gt 0){ $validation.Messages -join '; ' } else { '(none)' }) |")
    $lines.Add('')
    $lines.Add('| Score | Value |')
    $lines.Add('| --- | ---: |')
    $lines.Add("| overall | $($quality.OverallScore) |")
    $lines.Add("| realism | $($quality.Realism.Score) |")
    $lines.Add("| completeness | $($quality.Completeness.Score) |")
    $lines.Add("| consistency | $($quality.Consistency.Score) |")
    $lines.Add("| exportability | $($quality.Exportability.Score) |")
    $lines.Add("| operational | $($quality.Operational.Score) |")
    $lines.Add('')
    $lines.Add('| Metric | Value |')
    $lines.Add('| --- | ---: |')
    foreach ($metricName in @(
        'group_count',
        'policy_count',
        'policy_setting_count',
        'file_share_count',
        'collaboration_site_count',
        'configuration_item_count',
        'duplicate_person_upns',
        'duplicate_account_upns',
        'numbered_business_unit_names',
        'numbered_department_names',
        'numbered_team_names',
        'generic_share_names',
        'generic_folder_names',
        'generic_channel_names',
        'business_process_configuration_items',
        'undersized_policy_surface',
        'office_region_country_mismatch',
        'office_phone_country_mismatch',
        'plugin_generated_record_count',
        'plugin_generated_capability_count'
    )) {
        $lines.Add("| $metricName | $($audit.Metrics[$metricName]) |")
    }
    $lines.Add('')

    $lines.Add('Score inputs:')
    foreach ($dimension in @($quality.Realism, $quality.Completeness, $quality.Consistency, $quality.Exportability, $quality.Operational)) {
        $lines.Add("- $($dimension.Name): $($dimension.Score)")
        foreach ($input in $dimension.Inputs) {
            $lines.Add("  - $($input.Key): observed=$($input.ObservedValue) target=$($input.TargetValue) penalty=$($input.Penalty) heuristic=$($input.Heuristic)")
        }
    }

    foreach ($sampleName in @(
        'business_units',
        'departments',
        'teams',
        'file_shares',
        'collaboration_sites',
        'document_libraries',
        'document_folders',
        'groups',
        'policies',
        'policy_settings',
        'configuration_items',
        'cmdb_sources',
        'applications',
        'servers',
        'offices',
        'identity_stores',
        'plugin_capabilities'
    )) {
        $values = $audit.Samples[$sampleName]
        $joined = if ($values.Count -gt 0) { $values -join ' | ' } else { '(none)' }
        $lines.Add("- ${sampleName}: $joined")
    }

    if ($result.Warnings.Count -gt 0) {
        $lines.Add('')
        $lines.Add('Warnings:')
        foreach ($warning in $result.Warnings) {
            $lines.Add("- $warning")
        }
    }

    $lines.Add('')
}

$content = $lines -join [Environment]::NewLine
$summary = $validationType::Summarize($scenarioResults)
$json = $summary | ConvertTo-Json -Depth 8
$writeMarkdown = $OutputFormat -eq 'Markdown' -or $OutputFormat -eq 'Both'
$writeJson = $OutputFormat -eq 'Json' -or $OutputFormat -eq 'Both'

if ($writeMarkdown -and -not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $outputDirectory = Split-Path -Parent $OutputPath
    if (-not [string]::IsNullOrWhiteSpace($outputDirectory) -and -not (Test-Path $outputDirectory)) {
        New-Item -ItemType Directory -Path $outputDirectory | Out-Null
    }

    Set-Content -Path $OutputPath -Value $content -Encoding UTF8
    Write-Host "Realism review written to '$OutputPath'."
}

if ($writeJson -and -not [string]::IsNullOrWhiteSpace($JsonOutputPath)) {
    $jsonDirectory = Split-Path -Parent $JsonOutputPath
    if (-not [string]::IsNullOrWhiteSpace($jsonDirectory) -and -not (Test-Path $jsonDirectory)) {
        New-Item -ItemType Directory -Path $jsonDirectory | Out-Null
    }

    Set-Content -Path $JsonOutputPath -Value $json -Encoding UTF8
    Write-Host "Realism validation JSON written to '$JsonOutputPath'."
}

if ($writeMarkdown -and [string]::IsNullOrWhiteSpace($OutputPath) -and $OutputFormat -ne 'Json') {
    $content
}

if ($writeJson -and [string]::IsNullOrWhiteSpace($JsonOutputPath) -and $OutputFormat -ne 'Markdown') {
    $json
}

Write-Host "Quality validation status: $($summary.Status) (pass=$($summary.PassCount), warn=$($summary.WarnCount), fail=$($summary.FailCount))"

switch ($FailOnStatus) {
    'Warn' {
        if ($summary.Status -in @('warn', 'fail')) {
            throw "Quality validation completed with status '$($summary.Status)'."
        }
    }
    'Fail' {
        if ($summary.Status -eq 'fail') {
            throw "Quality validation completed with status 'fail'."
        }
    }
}
