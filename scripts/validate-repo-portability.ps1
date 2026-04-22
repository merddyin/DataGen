[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not (Test-Path (Join-Path $RepoRoot '.git'))) {
    throw "Repo root '$RepoRoot' does not appear to contain a .git directory."
}

$trackedFiles = git -C $RepoRoot ls-files
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to enumerate tracked files with git ls-files.'
}

$pathPattern = '(?<![A-Za-z0-9])[A-Za-z]:\\|/Users/|/home/|file://'
$findings = [System.Collections.Generic.List[object]]::new()

foreach ($relativePath in $trackedFiles) {
    if ([string]::IsNullOrWhiteSpace($relativePath)) {
        continue
    }

    $normalizedPath = $relativePath.Replace('\', '/', [StringComparison]::Ordinal)
    $shouldScan =
        $normalizedPath -eq 'README.md' -or
        $normalizedPath.StartsWith('.github/', [StringComparison]::Ordinal) -or
        $normalizedPath.StartsWith('scripts/', [StringComparison]::Ordinal) -or
        $normalizedPath.StartsWith('src/', [StringComparison]::Ordinal) -or
        $normalizedPath.StartsWith('website/', [StringComparison]::Ordinal) -or
        $normalizedPath.StartsWith('docs/', [StringComparison]::Ordinal) -or
        $normalizedPath.EndsWith('.scenario.json', [StringComparison]::Ordinal)

    if (-not $shouldScan) {
        continue
    }

    if ($normalizedPath.StartsWith('sdk/', [StringComparison]::Ordinal) -or
        $normalizedPath.StartsWith('tests/', [StringComparison]::Ordinal) -or
        $normalizedPath.StartsWith('examples/', [StringComparison]::Ordinal)) {
        continue
    }

    $fullPath = Join-Path $RepoRoot $relativePath
    if (-not (Test-Path $fullPath -PathType Leaf)) {
        continue
    }

    try {
        $content = [System.IO.File]::ReadAllText($fullPath)
    }
    catch {
        continue
    }

    if ($content.IndexOf([char]0) -ge 0) {
        continue
    }

    $lines = $content -split "`r?`n"
    for ($index = 0; $index -lt $lines.Length; $index++) {
        if ($lines[$index] -match $pathPattern) {
            $findings.Add([pscustomobject]@{
                Path = $relativePath
                Line = $index + 1
                Text = $lines[$index].Trim()
            })
        }
    }
}

if ($findings.Count -gt 0) {
    Write-Host 'Repo portability validation failed. Found machine-specific absolute paths:' -ForegroundColor Red
    foreach ($finding in $findings) {
        Write-Host (" - {0}:{1} :: {2}" -f $finding.Path, $finding.Line, $finding.Text)
    }

    throw "Found $($findings.Count) portability issue(s). Replace local absolute paths with repo-relative or environment-agnostic values."
}

Write-Host 'Repo portability validation passed.'
