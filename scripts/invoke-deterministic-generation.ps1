[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$CandidatePath,

    [Parameter(Mandatory)]
    [string]$ScenarioPath,

    [Parameter(Mandatory)]
    [int]$Seed,

    [Parameter(Mandatory)]
    [DateTimeOffset]$GeneratedAt,

    [Parameter(Mandatory)]
    [string]$GenerationScriptPath,

    [string[]]$GenerationArgumentList = @(),

    [string[]]$SensitiveGenerationArgumentName = @(),

    [string[]]$SensitiveGenerationArgumentPattern = @(),

    [int[]]$SensitiveGenerationArgumentIndex = @(),

    [Parameter(Mandatory)]
    [string]$InvocationContractPath,

    [Parameter(Mandatory)]
    [string]$GitPath,

    [Parameter(Mandatory)]
    [string]$DotNetPath,

    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Import-Module (Join-Path $PSScriptRoot 'DeterminismEvidence.psm1') -Force

function ConvertTo-GenerationArgumentBinding {
    param([string[]]$ArgumentList)

    $reservedNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($reservedName in @('OutputPath', 'ScenarioPath', 'Seed', 'GeneratedAt')) {
        [void]$reservedNames.Add($reservedName)
    }
    $named = [ordered]@{}
    $positional = [Collections.Generic.List[string]]::new()
    for ($index = 0; $index -lt $ArgumentList.Count; $index++) {
        $argument = [string]$ArgumentList[$index]
        $equalsMatch = [Text.RegularExpressions.Regex]::Match($argument, '^--?([^=]+)=(.*)$')
        if ($equalsMatch.Success) {
            $name = $equalsMatch.Groups[1].Value
            if ($reservedNames.Contains($name)) {
                throw "Generation argument '-$name' cannot override a standard invocation input."
            }
            if ($named.Contains($name)) {
                throw "Generation argument '-$name' is specified more than once."
            }
            $named[$name] = $equalsMatch.Groups[2].Value
            continue
        }

        $nameMatch = [Text.RegularExpressions.Regex]::Match($argument, '^--?(.+)$')
        if ($nameMatch.Success) {
            $name = $nameMatch.Groups[1].Value
            if ($reservedNames.Contains($name)) {
                throw "Generation argument '-$name' cannot override a standard invocation input."
            }
            if ($named.Contains($name)) {
                throw "Generation argument '-$name' is specified more than once."
            }
            if ($index + 1 -ge $ArgumentList.Count) {
                throw "Generation argument '-$name' has no value. Use '-$name=`$true' for a switch."
            }
            $index++
            $named[$name] = [string]$ArgumentList[$index]
            continue
        }

        $positional.Add($argument)
    }

    return [pscustomobject]@{
        Named = $named
        Positional = @($positional)
    }
}

$resolvedCandidatePath = [IO.Path]::GetFullPath($CandidatePath)
$resolvedScenarioPath = [IO.Path]::GetFullPath($ScenarioPath)
$resolvedGenerationScriptPath = [IO.Path]::GetFullPath($GenerationScriptPath)
$resolvedRepoRoot = [IO.Path]::GetFullPath($RepoRoot)
$resolvedInvocationContractPath = [IO.Path]::GetFullPath($InvocationContractPath)

if (-not (Test-Path -LiteralPath $resolvedScenarioPath -PathType Leaf)) {
    throw "Scenario '$resolvedScenarioPath' does not exist."
}
if (-not (Test-Path -LiteralPath $resolvedGenerationScriptPath -PathType Leaf)) {
    throw "Generation script '$resolvedGenerationScriptPath' does not exist."
}
if (Test-Path -LiteralPath $resolvedCandidatePath) {
    Assert-NoReparsePointTree -RootPath $resolvedCandidatePath
    if (@(Get-ChildItem -LiteralPath $resolvedCandidatePath -Force).Count -ne 0) {
        throw "Candidate root '$resolvedCandidatePath' must be empty before generation."
    }
}
else {
    [IO.Directory]::CreateDirectory($resolvedCandidatePath) | Out-Null
}

$invocationId = [Guid]::NewGuid().ToString('D')
$startedAt = [DateTimeOffset]::UtcNow
$process = Get-Process -Id $PID
$processStartTimeUtc = $process.StartTime.ToUniversalTime()
$wrapperPath = $MyInvocation.MyCommand.Path
$contractRead = Read-StableJsonEvidenceFile -Path $resolvedInvocationContractPath -MaximumBytes 4MB
$parentContract = $contractRead.value
if ($parentContract.schemaVersion -cne 'datagen-generation-parent-contract-v1') {
    throw 'Parent contract schema version is invalid.'
}
$beforeEnvironment = Get-GenerationEvidenceEnvironment `
    -RepoRoot $resolvedRepoRoot `
    -WrapperPath $wrapperPath `
    -GenerationScriptPath $resolvedGenerationScriptPath `
    -ScenarioPath $resolvedScenarioPath `
    -GitPath $GitPath `
    -DotNetPath $DotNetPath `
    -Seed $Seed `
    -GeneratedAt $GeneratedAt `
    -GenerationArgumentList $GenerationArgumentList `
    -SensitiveGenerationArgumentName $SensitiveGenerationArgumentName `
    -SensitiveGenerationArgumentPattern $SensitiveGenerationArgumentPattern `
    -SensitiveGenerationArgumentIndex $SensitiveGenerationArgumentIndex
$expectedEnvironmentJson = ConvertTo-Json -InputObject $parentContract.expected -Depth 24 -Compress
$beforeEnvironmentJson = ConvertTo-Json -InputObject $beforeEnvironment -Depth 24 -Compress
if ($beforeEnvironmentJson -cne $expectedEnvironmentJson) {
    throw 'Current generation environment does not match the preissued parent contract.'
}
$generationArgumentBinding = ConvertTo-GenerationArgumentBinding -ArgumentList $GenerationArgumentList
$namedGenerationArguments = $generationArgumentBinding.Named
$positionalGenerationArguments = $generationArgumentBinding.Positional

& $resolvedGenerationScriptPath `
    -OutputPath $resolvedCandidatePath `
    -ScenarioPath $resolvedScenarioPath `
    -Seed $Seed `
    -GeneratedAt $GeneratedAt `
    @namedGenerationArguments `
    @positionalGenerationArguments

$afterEnvironment = Get-GenerationEvidenceEnvironment `
    -RepoRoot $resolvedRepoRoot `
    -WrapperPath $wrapperPath `
    -GenerationScriptPath $resolvedGenerationScriptPath `
    -ScenarioPath $resolvedScenarioPath `
    -GitPath $GitPath `
    -DotNetPath $DotNetPath `
    -Seed $Seed `
    -GeneratedAt $GeneratedAt `
    -GenerationArgumentList $GenerationArgumentList `
    -SensitiveGenerationArgumentName $SensitiveGenerationArgumentName `
    -SensitiveGenerationArgumentPattern $SensitiveGenerationArgumentPattern `
    -SensitiveGenerationArgumentIndex $SensitiveGenerationArgumentIndex
$afterEnvironmentJson = ConvertTo-Json -InputObject $afterEnvironment -Depth 24 -Compress
if ($afterEnvironmentJson -cne $beforeEnvironmentJson) {
    throw 'Generation environment identity changed during generation; no success sidecar was emitted.'
}

$completedAt = [DateTimeOffset]::UtcNow
$sidecarName = Get-GenerationProvenanceSidecarName
$payload = Get-CanonicalFileInventory `
    -RootPath $resolvedCandidatePath `
    -ExcludeRelativePath @($sidecarName) `
    -RejectReparsePoints
if ($payload.fileCount -eq 0) {
    throw "Generation invocation '$invocationId' produced no payload files."
}

$sidecar = [ordered]@{
    schemaVersion = '1.0.0'
    trustBoundary = 'trusted-operator-unsigned-qa-evidence'
    parentChallenge = [ordered]@{
        label = $parentContract.challenge.label
        id = $parentContract.challenge.id
        nonce = $parentContract.challenge.nonce
        contractSha256 = $contractRead.sha256
    }
    run = [ordered]@{
        invocationId = $invocationId
        startedAtUtc = $startedAt.ToString('O', [Globalization.CultureInfo]::InvariantCulture)
        completedAtUtc = $completedAt.ToString('O', [Globalization.CultureInfo]::InvariantCulture)
        process = [ordered]@{
            id = $PID
            name = $process.ProcessName
            startTimeUtc = ([DateTimeOffset]$processStartTimeUtc).ToString('O', [Globalization.CultureInfo]::InvariantCulture)
            startTimeUtcTicks = $processStartTimeUtc.Ticks
        }
    }
    tool = [ordered]@{
        wrapperName = $beforeEnvironment.wrapper.logicalLabel
        wrapperSha256 = $beforeEnvironment.wrapper.sha256
        generationScriptLogicalIdentity = $beforeEnvironment.generator.logicalIdentity
        generationScriptSha256 = $beforeEnvironment.generator.sha256
        generationScriptSizeBytes = $beforeEnvironment.generator.sizeBytes
        executables = $beforeEnvironment.executables
    }
    invocation = $beforeEnvironment.invocation
    runtime = $beforeEnvironment.runtime
    source = $beforeEnvironment.source
    inputs = [ordered]@{
        scenarioName = [IO.Path]::GetFileName($resolvedScenarioPath)
        scenarioSha256 = $beforeEnvironment.scenario.sha256
        seed = $Seed
        generatedAtUtc = $GeneratedAt.ToUniversalTime().ToString('O', [Globalization.CultureInfo]::InvariantCulture)
    }
    output = [ordered]@{
        fileCount = $payload.fileCount
        totalBytes = $payload.totalBytes
        payloadSha256 = $payload.aggregateSha256
    }
}

$sidecarPath = Join-Path $resolvedCandidatePath $sidecarName
Write-CanonicalJsonFile -Value $sidecar -Path $sidecarPath -MaximumBytes 4MB

[pscustomobject]$sidecar
