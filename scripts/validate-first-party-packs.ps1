[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$modulePath = Join-Path $repoRoot 'src\SyntheticEnterprise.PowerShell\bin\Debug\net8.0\SyntheticEnterprise.PowerShell.dll'
$packRoot = Join-Path $repoRoot 'packs\first-party'

if (-not (Test-Path $modulePath)) {
    throw "PowerShell module build output was not found at '$modulePath'. Build the solution before validating first-party packs."
}

if (-not (Test-Path $packRoot)) {
    throw "Bundled pack root was not found at '$packRoot'."
}

Import-Module $modulePath -Force

$report = Test-SEGenerationPluginPackage -PluginRootPath $packRoot -ValidatePackContract

if ($report.HasErrors) {
    $messages = @()

    foreach ($message in $report.Messages) {
        $messages += $message
    }

    foreach ($issue in $report.PackContractIssues) {
        $severity = if ($issue.IsError) { 'error' } else { 'warning' }
        $messages += "[${severity}] $($issue.Capability): $($issue.RuleId): $($issue.Message)"
    }

    $detail = if ($messages.Count -gt 0) {
        $messages -join [Environment]::NewLine
    } else {
        'Unknown pack validation error.'
    }

    throw "Bundled first-party pack validation failed.`n$detail"
}

Write-Host "Bundled first-party packs validated successfully."
Write-Host "Pack count: $($report.PluginCount)"
Write-Host "Warnings: $($report.PackContractWarningCount)"
