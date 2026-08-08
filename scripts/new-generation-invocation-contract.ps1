[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ScenarioPath,
    [Parameter(Mandatory)][string]$ExpectedGenerationScriptPath,
    [Parameter(Mandatory)][int]$Seed,
    [Parameter(Mandatory)][DateTimeOffset]$GeneratedAt,
    [string[]]$GenerationArgumentList = @(),
    [string[]]$SensitiveGenerationArgumentName = @(),
    [string[]]$SensitiveGenerationArgumentPattern = @(),
    [int[]]$SensitiveGenerationArgumentIndex = @(),
    [Parameter(Mandatory)][string]$ChallengeLabel,
    [Parameter(Mandatory)][string]$OutputPath,
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [Parameter(Mandatory)][string]$GitPath,
    [Parameter(Mandatory)][string]$DotNetPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'DeterminismEvidence.psm1') -Force

if (-not [IO.Path]::IsPathFullyQualified($GitPath) -or -not [IO.Path]::IsPathFullyQualified($DotNetPath)) {
    throw 'GitPath and DotNetPath must be fully qualified.'
}
if ($ChallengeLabel -cnotmatch '^candidate-[12]$') {
    throw "ChallengeLabel '$ChallengeLabel' must be candidate-1 or candidate-2."
}

$expected = Get-GenerationEvidenceEnvironment `
    -RepoRoot $RepoRoot `
    -WrapperPath (Join-Path $PSScriptRoot 'invoke-deterministic-generation.ps1') `
    -GenerationScriptPath $ExpectedGenerationScriptPath `
    -ScenarioPath $ScenarioPath `
    -GitPath $GitPath `
    -DotNetPath $DotNetPath `
    -Seed $Seed `
    -GeneratedAt $GeneratedAt `
    -GenerationArgumentList $GenerationArgumentList `
    -SensitiveGenerationArgumentName $SensitiveGenerationArgumentName `
    -SensitiveGenerationArgumentPattern $SensitiveGenerationArgumentPattern `
    -SensitiveGenerationArgumentIndex $SensitiveGenerationArgumentIndex

$contract = [ordered]@{
    schemaVersion = 'datagen-generation-parent-contract-v1'
    trustBoundary = 'trusted-operator-unsigned-qa-evidence'
    challenge = [ordered]@{
        label = $ChallengeLabel
        id = [Guid]::NewGuid().ToString('D')
        nonce = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLowerInvariant()
        issuedAtUtc = [DateTimeOffset]::UtcNow.ToString('O', [Globalization.CultureInfo]::InvariantCulture)
    }
    expected = $expected
}

Write-CanonicalJsonFile -Value $contract -Path $OutputPath -MaximumBytes 4MB
[pscustomobject]$contract
