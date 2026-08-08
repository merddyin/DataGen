[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateCount(2, 2)]
    [string[]]$CandidatePath,

    [Parameter(Mandatory)]
    [string]$ScenarioPath,

    [Parameter(Mandatory)]
    [int]$Seed,

    [Parameter(Mandatory)]
    [DateTimeOffset]$GeneratedAt,

    [Parameter(Mandatory)]
    [string]$OutputPath,

    [Parameter(Mandatory)]
    [string]$ExpectedGenerationScriptPath,

    [Parameter(Mandatory)]
    [string]$ExpectedInvocationInputDigest,

    [string[]]$ExpectedGenerationArgumentList = @(),

    [string[]]$SensitiveGenerationArgumentName = @(),

    [string[]]$SensitiveGenerationArgumentPattern = @(),

    [int[]]$SensitiveGenerationArgumentIndex = @(),

    [Parameter(Mandatory)]
    [ValidateCount(2, 2)]
    [string[]]$ExpectedInvocationContractPath,

    [Parameter(Mandatory)]
    [string]$GitPath,

    [Parameter(Mandatory)]
    [string]$DotNetPath,

    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),

    [switch]$FailOnMismatch
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Import-Module (Join-Path $PSScriptRoot 'DeterminismEvidence.psm1') -Force

function Get-RequiredProperty {
    param(
        [Parameter(Mandatory)]$Object,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Context
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) {
        throw "$Context is missing required property '$Name'."
    }

    return ,$property.Value
}

function Assert-ExpectedValue {
    param(
        [Parameter(Mandatory)][string]$Context,
        [Parameter(Mandatory)]$Expected,
        [Parameter(Mandatory)]$Actual
    )

    if ([string]$Expected -cne [string]$Actual) {
        throw "$Context mismatch. Expected '$Expected', got '$Actual'."
    }
}

function Assert-Sha256 {
    param([Parameter(Mandatory)][string]$Value, [Parameter(Mandatory)][string]$Context)

    if ($Value -cnotmatch '^[0-9a-f]{64}$') {
        throw "$Context must be a lowercase SHA-256 value."
    }
}

function Test-PathWithinRoot {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Root)

    $comparison = if ([OperatingSystem]::IsWindows()) {
        [StringComparison]::OrdinalIgnoreCase
    }
    else {
        [StringComparison]::Ordinal
    }
    $rootPrefix = [IO.Path]::TrimEndingDirectorySeparator($Root) + [IO.Path]::DirectorySeparatorChar
    return $Path.StartsWith($rootPrefix, $comparison)
}

$resolvedRepoRoot = [IO.Path]::GetFullPath($RepoRoot)
$resolvedScenarioPath = [IO.Path]::GetFullPath($ScenarioPath)
if (-not (Test-Path -LiteralPath $resolvedScenarioPath -PathType Leaf)) {
    throw "Scenario '$resolvedScenarioPath' does not exist."
}

$resolvedCandidatePaths = @(
    foreach ($path in $CandidatePath) {
        if (-not (Test-Path -LiteralPath $path -PathType Container)) {
            throw "Candidate root '$path' does not exist."
        }
        $resolvedPath = [IO.Path]::GetFullPath($path)
        Assert-NoReparsePointTree -RootPath $resolvedPath
        $resolvedPath
    }
)
$rootComparer = if ([OperatingSystem]::IsWindows()) {
    [StringComparer]::OrdinalIgnoreCase
}
else {
    [StringComparer]::Ordinal
}
$distinctRoots = [Collections.Generic.HashSet[string]]::new($rootComparer)
foreach ($path in $resolvedCandidatePaths) {
    if (-not $distinctRoots.Add([IO.Path]::TrimEndingDirectorySeparator($path))) {
        throw 'Determinism evidence requires two distinct resolved candidate roots.'
    }
}

$resolvedOutputPath = [IO.Path]::GetFullPath($OutputPath)
foreach ($candidateRoot in $resolvedCandidatePaths) {
    if (Test-PathWithinRoot -Path $resolvedOutputPath -Root $candidateRoot) {
        throw 'The receipt output must be outside both candidate roots so it cannot mutate a validated payload.'
    }
}

$expectedWrapperPath = Join-Path $PSScriptRoot 'invoke-deterministic-generation.ps1'
$expectedWrapperName = [IO.Path]::GetFileName($expectedWrapperPath)
$expectedWrapperSha256 = Get-FileSha256Hex -Path $expectedWrapperPath
$expectedGenerationScript = Get-GenerationScriptIdentity -Path $ExpectedGenerationScriptPath -RepoRoot $resolvedRepoRoot
$expectedInvocation = Get-GenerationInvocationIdentity `
    -Seed $Seed `
    -GeneratedAt $GeneratedAt `
    -GenerationArgumentList $ExpectedGenerationArgumentList `
    -SensitiveGenerationArgumentName $SensitiveGenerationArgumentName `
    -SensitiveGenerationArgumentPattern $SensitiveGenerationArgumentPattern `
    -SensitiveGenerationArgumentIndex $SensitiveGenerationArgumentIndex
Assert-Sha256 -Value $ExpectedInvocationInputDigest -Context 'Expected invocation input digest'
Assert-ExpectedValue `
    -Context 'Expected invocation input digest contract' `
    -Expected $expectedInvocation.argumentDigestSha256 `
    -Actual $ExpectedInvocationInputDigest
$expectedInputs = [ordered]@{
    scenarioName = [IO.Path]::GetFileName($resolvedScenarioPath)
    scenarioSha256 = Get-FileSha256Hex -Path $resolvedScenarioPath
    seed = $Seed
    generatedAtUtc = $GeneratedAt.ToUniversalTime().ToString('O', [Globalization.CultureInfo]::InvariantCulture)
}
$currentEnvironment = Get-GenerationEvidenceEnvironment `
    -RepoRoot $resolvedRepoRoot `
    -WrapperPath $expectedWrapperPath `
    -GenerationScriptPath $ExpectedGenerationScriptPath `
    -ScenarioPath $resolvedScenarioPath `
    -GitPath $GitPath `
    -DotNetPath $DotNetPath `
    -Seed $Seed `
    -GeneratedAt $GeneratedAt `
    -GenerationArgumentList $ExpectedGenerationArgumentList `
    -SensitiveGenerationArgumentName $SensitiveGenerationArgumentName `
    -SensitiveGenerationArgumentPattern $SensitiveGenerationArgumentPattern `
    -SensitiveGenerationArgumentIndex $SensitiveGenerationArgumentIndex
$expectedSource = $currentEnvironment.source
$expectedRuntime = $currentEnvironment.runtime
$contractReads = @(
    for ($contractIndex = 0; $contractIndex -lt 2; $contractIndex++) {
        $read = Read-StableJsonEvidenceFile -Path $ExpectedInvocationContractPath[$contractIndex] -MaximumBytes 4MB
        $contract = $read.value
        if ($contract.schemaVersion -cne 'datagen-generation-parent-contract-v1' -or
            $contract.trustBoundary -cne 'trusted-operator-unsigned-qa-evidence') {
            throw "Parent challenge contract $($contractIndex + 1) is invalid."
        }
        $expectedLabel = "candidate-$($contractIndex + 1)"
        Assert-ExpectedValue -Context "Parent challenge $expectedLabel label" -Expected $expectedLabel -Actual $contract.challenge.label
        $contractEnvironmentJson = ConvertTo-Json -InputObject $contract.expected -Depth 24 -Compress
        $currentEnvironmentJson = ConvertTo-Json -InputObject $currentEnvironment -Depth 24 -Compress
        Assert-ExpectedValue -Context "Parent challenge $expectedLabel environment" -Expected $currentEnvironmentJson -Actual $contractEnvironmentJson
        [pscustomobject]@{ value = $contract; sha256 = $read.sha256 }
    }
)
if ($contractReads[0].value.challenge.id -ceq $contractReads[1].value.challenge.id -or
    $contractReads[0].value.challenge.nonce -ceq $contractReads[1].value.challenge.nonce) {
    throw 'Parent challenges must be distinct.'
}
$sidecarName = Get-GenerationProvenanceSidecarName

$validatedCandidates = @(
    for ($index = 0; $index -lt $resolvedCandidatePaths.Count; $index++) {
        $candidateRoot = $resolvedCandidatePaths[$index]
        $label = "candidate-$($index + 1)"
        $context = "Generation provenance sidecar for $label"
        $sidecarPath = Join-Path $candidateRoot $sidecarName
        if (-not (Test-Path -LiteralPath $sidecarPath -PathType Leaf)) {
            throw "$context is missing: '$sidecarName'."
        }

        $sidecarRead = Read-StableJsonEvidenceFile -Path $sidecarPath
        $sidecar = $sidecarRead.value
        Assert-ExpectedValue -Context "$context schema version" -Expected '1.0.0' -Actual (Get-RequiredProperty -Object $sidecar -Name 'schemaVersion' -Context $context)
        Assert-ExpectedValue -Context "$context trust boundary" -Expected 'trusted-operator-unsigned-qa-evidence' -Actual (Get-RequiredProperty -Object $sidecar -Name 'trustBoundary' -Context $context)
        $parentChallenge = Get-RequiredProperty -Object $sidecar -Name 'parentChallenge' -Context $context
        $expectedParentContract = $contractReads[$index]
        foreach ($name in @('label', 'id', 'nonce')) {
            Assert-ExpectedValue -Context "$context parent challenge $name" -Expected $expectedParentContract.value.challenge.$name -Actual (Get-RequiredProperty -Object $parentChallenge -Name $name -Context "$context parent challenge")
        }
        Assert-ExpectedValue -Context "$context parent challenge contract hash" -Expected $expectedParentContract.sha256 -Actual (Get-RequiredProperty -Object $parentChallenge -Name 'contractSha256' -Context "$context parent challenge")
        $run = Get-RequiredProperty -Object $sidecar -Name 'run' -Context $context
        $tool = Get-RequiredProperty -Object $sidecar -Name 'tool' -Context $context
        $invocation = Get-RequiredProperty -Object $sidecar -Name 'invocation' -Context $context
        $runtime = Get-RequiredProperty -Object $sidecar -Name 'runtime' -Context $context
        $source = Get-RequiredProperty -Object $sidecar -Name 'source' -Context $context
        $inputs = Get-RequiredProperty -Object $sidecar -Name 'inputs' -Context $context
        $output = Get-RequiredProperty -Object $sidecar -Name 'output' -Context $context

        $runIdText = [string](Get-RequiredProperty -Object $run -Name 'invocationId' -Context "$context run")
        $runId = [Guid]::Empty
        if (-not [Guid]::TryParse($runIdText, [ref]$runId) -or $runId -eq [Guid]::Empty) {
            throw "$context run ID is not a non-empty GUID."
        }
        $startedAt = [DateTimeOffset](Get-RequiredProperty -Object $run -Name 'startedAtUtc' -Context "$context run")
        $completedAt = [DateTimeOffset](Get-RequiredProperty -Object $run -Name 'completedAtUtc' -Context "$context run")
        if ($completedAt -lt $startedAt) {
            throw "$context completion time precedes its start time."
        }
        $processIdentity = Get-RequiredProperty -Object $run -Name 'process' -Context "$context run"
        $processId = [int](Get-RequiredProperty -Object $processIdentity -Name 'id' -Context "$context process")
        $processName = [string](Get-RequiredProperty -Object $processIdentity -Name 'name' -Context "$context process")
        $processStartTime = [DateTimeOffset](Get-RequiredProperty -Object $processIdentity -Name 'startTimeUtc' -Context "$context process")
        $processStartTicks = [long](Get-RequiredProperty -Object $processIdentity -Name 'startTimeUtcTicks' -Context "$context process")
        if ($processId -le 0 -or [string]::IsNullOrWhiteSpace($processName) -or $processStartTicks -le 0 -or
            $processStartTime.ToUniversalTime().Ticks -ne $processStartTicks) {
            throw "$context process identity is invalid."
        }

        foreach ($hashName in @('wrapperSha256', 'generationScriptSha256')) {
            Assert-Sha256 -Value ([string](Get-RequiredProperty -Object $tool -Name $hashName -Context "$context tool")) -Context "$context tool $hashName"
        }
        foreach ($name in @('wrapperName', 'generationScriptLogicalIdentity')) {
            if ([string]::IsNullOrWhiteSpace([string](Get-RequiredProperty -Object $tool -Name $name -Context "$context tool"))) {
                throw "$context tool '$name' is empty."
            }
        }
        Assert-ExpectedValue -Context "$context expected wrapper name" -Expected $expectedWrapperName -Actual $tool.wrapperName
        Assert-ExpectedValue -Context "$context expected wrapper hash" -Expected $expectedWrapperSha256 -Actual $tool.wrapperSha256
        Assert-ExpectedValue -Context "$context expected generation tool logical identity" -Expected $expectedGenerationScript.logicalIdentity -Actual $tool.generationScriptLogicalIdentity
        Assert-ExpectedValue -Context "$context expected generation tool hash" -Expected $expectedGenerationScript.sha256 -Actual $tool.generationScriptSha256
        Assert-ExpectedValue -Context "$context expected generation tool byte count" -Expected $expectedGenerationScript.sizeBytes -Actual (Get-RequiredProperty -Object $tool -Name 'generationScriptSizeBytes' -Context "$context tool")
        $actualExecutables = Get-RequiredProperty -Object $tool -Name 'executables' -Context "$context tool"
        Assert-ExpectedValue `
            -Context "$context expected evidence executables" `
            -Expected (ConvertTo-Json -InputObject $currentEnvironment.executables -Depth 8 -Compress) `
            -Actual (ConvertTo-Json -InputObject $actualExecutables -Depth 8 -Compress)

        Assert-ExpectedValue -Context "$context invocation contract version" -Expected $expectedInvocation.contractVersion -Actual (Get-RequiredProperty -Object $invocation -Name 'contractVersion' -Context "$context invocation")
        Assert-ExpectedValue -Context "$context expected invocation input digest" -Expected $ExpectedInvocationInputDigest -Actual (Get-RequiredProperty -Object $invocation -Name 'argumentDigestSha256' -Context "$context invocation")
        $expectedSafeArguments = ConvertTo-Json -InputObject @($expectedInvocation.safeArguments) -Depth 8 -Compress
        $actualSafeArguments = ConvertTo-Json -InputObject @((Get-RequiredProperty -Object $invocation -Name 'safeArguments' -Context "$context invocation")) -Depth 8 -Compress
        Assert-ExpectedValue -Context "$context expected invocation input safe argument contract" -Expected $expectedSafeArguments -Actual $actualSafeArguments
        Assert-ExpectedValue `
            -Context "$context expected invocation input redaction contract" `
            -Expected (@($expectedInvocation.sensitiveArgumentNames) -join "`n") `
            -Actual (@((Get-RequiredProperty -Object $invocation -Name 'sensitiveArgumentNames' -Context "$context invocation")) -join "`n")
        foreach ($name in @('sensitiveArgumentPatterns', 'sensitiveArgumentIndexes', 'sensitiveInputsExcludedFromReproducibility', 'sensitiveInputCount')) {
            Assert-ExpectedValue `
                -Context "$context expected invocation input $name" `
                -Expected (ConvertTo-Json -InputObject $expectedInvocation.$name -Compress) `
                -Actual (ConvertTo-Json -InputObject (Get-RequiredProperty -Object $invocation -Name $name -Context "$context invocation") -Compress)
        }

        foreach ($name in @('version', 'branch', 'commit', 'dirty', 'treeSha256', 'treeFileCount', 'treeTotalBytes', 'inclusionSet')) {
            Assert-ExpectedValue `
                -Context "$context expected final source $name" `
                -Expected $expectedSource.$name `
                -Actual (Get-RequiredProperty -Object $source -Name $name -Context "$context source")
        }
        foreach ($name in @('framework', 'os', 'architecture', 'powershell', 'dotnetSdk')) {
            Assert-ExpectedValue `
                -Context "$context expected final runtime $name" `
                -Expected $expectedRuntime.$name `
                -Actual (Get-RequiredProperty -Object $runtime -Name $name -Context "$context runtime")
        }
        foreach ($name in @('scenarioName', 'scenarioSha256', 'seed', 'generatedAtUtc')) {
            $actualInput = Get-RequiredProperty -Object $inputs -Name $name -Context "$context inputs"
            if ($name -eq 'generatedAtUtc') {
                $actualInput = ([DateTimeOffset]$actualInput).ToUniversalTime().ToString('O', [Globalization.CultureInfo]::InvariantCulture)
            }
            Assert-ExpectedValue `
                -Context "$context expected input $name" `
                -Expected $expectedInputs[$name] `
                -Actual $actualInput
        }

        $payload = Get-CanonicalFileInventory -RootPath $candidateRoot -ExcludeRelativePath @($sidecarName) -RejectReparsePoints
        Assert-ExpectedValue -Context "$context payload hash" -Expected $payload.aggregateSha256 -Actual (Get-RequiredProperty -Object $output -Name 'payloadSha256' -Context "$context output")
        Assert-ExpectedValue -Context "$context payload file count" -Expected $payload.fileCount -Actual (Get-RequiredProperty -Object $output -Name 'fileCount' -Context "$context output")
        Assert-ExpectedValue -Context "$context payload byte count" -Expected $payload.totalBytes -Actual (Get-RequiredProperty -Object $output -Name 'totalBytes' -Context "$context output")

        [pscustomobject][ordered]@{
            label = $label
            run = [ordered]@{
                invocationId = $runId.ToString('D')
                startedAtUtc = $startedAt.ToUniversalTime().ToString('O', [Globalization.CultureInfo]::InvariantCulture)
                completedAtUtc = $completedAt.ToUniversalTime().ToString('O', [Globalization.CultureInfo]::InvariantCulture)
                process = [ordered]@{
                    id = $processId
                    name = $processName
                    startTimeUtc = $processStartTime.ToUniversalTime().ToString('O', [Globalization.CultureInfo]::InvariantCulture)
                    startTimeUtcTicks = $processStartTicks
                }
            }
            tool = $tool
            invocation = $invocation
            parentChallenge = [ordered]@{
                label = $parentChallenge.label
                id = $parentChallenge.id
                contractSha256 = $parentChallenge.contractSha256
            }
            sidecarSha256 = $sidecarRead.sha256
            payload = [ordered]@{
                fileCount = $payload.fileCount
                totalBytes = $payload.totalBytes
                aggregateSha256 = $payload.aggregateSha256
                files = $payload.files
            }
        }
    }
)

if ($validatedCandidates[0].run.invocationId -ceq $validatedCandidates[1].run.invocationId) {
    throw 'Determinism evidence requires two distinct run IDs; copied sidecars are not independent evidence.'
}
if ($validatedCandidates[0].run.process.id -eq $validatedCandidates[1].run.process.id -and
    $validatedCandidates[0].run.process.startTimeUtcTicks -eq $validatedCandidates[1].run.process.startTimeUtcTicks) {
    throw 'Determinism evidence requires two distinct process identities; changing only a copied run ID is insufficient.'
}
if ($validatedCandidates[0].tool.generationScriptLogicalIdentity -cne $validatedCandidates[1].tool.generationScriptLogicalIdentity -or
    $validatedCandidates[0].tool.generationScriptSha256 -cne $validatedCandidates[1].tool.generationScriptSha256) {
    throw 'Determinism evidence requires both runs to use the same generation tool identity.'
}

$passed =
    $validatedCandidates[0].payload.fileCount -eq $validatedCandidates[1].payload.fileCount -and
    $validatedCandidates[0].payload.totalBytes -eq $validatedCandidates[1].payload.totalBytes -and
    $validatedCandidates[0].payload.aggregateSha256 -ceq $validatedCandidates[1].payload.aggregateSha256

$receipt = [ordered]@{
    schemaVersion = '2.0.0'
    trustBoundary = 'trusted-operator-unsigned-qa-evidence'
    passed = $passed
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O', [Globalization.CultureInfo]::InvariantCulture)
    inputs = $expectedInputs
    source = $expectedSource
    runtime = $expectedRuntime
    generationContract = [ordered]@{
        tool = $expectedGenerationScript
        executables = $currentEnvironment.executables
        invocation = $expectedInvocation
    }
    parentChallenges = @(
        for ($index = 0; $index -lt 2; $index++) {
            [ordered]@{
                label = $contractReads[$index].value.challenge.label
                id = $contractReads[$index].value.challenge.id
                contractSha256 = $contractReads[$index].sha256
            }
        }
    )
    limitations = [ordered]@{
        cryptographicTamperAttestation = $false
        sensitiveInputsExcludedFromReproducibility = $expectedInvocation.sensitiveInputsExcludedFromReproducibility
    }
    candidates = $validatedCandidates
}

Write-CanonicalJsonFile -Value $receipt -Path $resolvedOutputPath

if ($FailOnMismatch -and -not $passed) {
    throw "Determinism comparison failed. Receipt: '$resolvedOutputPath'."
}

[pscustomobject]$receipt
