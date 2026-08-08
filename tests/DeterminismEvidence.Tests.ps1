[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$WorkingRoot = 'E:\Codex\build\datagen-v093-round3-receipt-tests'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$script:ExecutedAssertionCount = 0
$script:SkippedCases = [Collections.Generic.List[string]]::new()

function Assert-Equal {
    param($Expected, $Actual, [string]$Because)

    $script:ExecutedAssertionCount++
    if ($Expected -cne $Actual) {
        throw "$Because Expected '$Expected', got '$Actual'."
    }
}

function Assert-True {
    param([bool]$Condition, [string]$Because)

    $script:ExecutedAssertionCount++
    if (-not $Condition) {
        throw $Because
    }
}

function Assert-ThrowsLike {
    param([scriptblock]$Action, [string]$Pattern, [string]$Because)

    $script:ExecutedAssertionCount++
    $message = $null
    try {
        & $Action
    }
    catch {
        $message = $_.Exception.Message
    }

    if ($null -eq $message) {
        throw "$Because Expected an exception matching '$Pattern'."
    }
    if ($message -notlike $Pattern) {
        throw "$Because Expected '$Pattern', got '$message'."
    }
}

function Add-SkippedCase {
    param([string]$Name, [string]$Reason)

    $script:SkippedCases.Add("$Name`: $Reason")
}

function Set-Sidecar {
    param([string]$Path, $Value)

    $json = (ConvertTo-Json -InputObject $Value -Depth 24).ReplaceLineEndings("`n") + "`n"
    [IO.File]::WriteAllText($Path, $json, [Text.UTF8Encoding]::new($false))
}

function Start-GenerationChild {
    param(
        [string]$PwshPath,
        [string]$DriverPath,
        [string]$CandidatePath,
        [string]$ScenarioPath,
        [string]$GeneratorPath,
        [string]$RepoRoot,
        [string]$InvocationContractPath,
        [string]$GitPath,
        [string]$DotNetPath,
        [string]$Mode,
        [string]$ApiToken,
        [string]$MutateScenario = 'false'
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $PwshPath
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in @(
        '-NoProfile',
        '-File', $DriverPath,
        '-CandidatePath', $CandidatePath,
        '-ScenarioPath', $ScenarioPath,
        '-GeneratorPath', $GeneratorPath,
        '-RepoRoot', $RepoRoot,
        '-InvocationContractPath', $InvocationContractPath,
        '-GitPath', $GitPath,
        '-DotNetPath', $DotNetPath,
        '-Mode', $Mode,
        '-ApiToken', $ApiToken,
        '-MutateScenario', $MutateScenario)) {
        $startInfo.ArgumentList.Add($argument)
    }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        throw "Unable to start child generation process for '$CandidatePath'."
    }

    return [pscustomobject]@{
        Process = $process
        StandardOutput = $process.StandardOutput.ReadToEndAsync()
        StandardError = $process.StandardError.ReadToEndAsync()
    }
}

function Complete-GenerationChild {
    param($Child, [string]$Label)

    $Child.Process.WaitForExit()
    $stdout = $Child.StandardOutput.GetAwaiter().GetResult()
    $stderr = $Child.StandardError.GetAwaiter().GetResult()
    Assert-True ($Child.Process.ExitCode -eq 0) "$Label failed with exit code $($Child.Process.ExitCode). stdout=$stdout stderr=$stderr"
}

function Complete-GenerationChildFailure {
    param($Child, [string]$Label, [string]$Pattern)

    $Child.Process.WaitForExit()
    $stdout = $Child.StandardOutput.GetAwaiter().GetResult()
    $stderr = $Child.StandardError.GetAwaiter().GetResult()
    Assert-True ($Child.Process.ExitCode -ne 0) "$Label unexpectedly succeeded. stdout=$stdout stderr=$stderr"
    Assert-True ($stderr -like $Pattern) "$Label did not report '$Pattern'. stderr=$stderr"
}

$resolvedRepoRoot = [IO.Path]::GetFullPath($RepoRoot)
$resolvedWorkingRoot = [IO.Path]::GetFullPath($WorkingRoot)
if (Test-Path -LiteralPath $resolvedWorkingRoot) {
    Remove-Item -LiteralPath $resolvedWorkingRoot -Recurse -Force
}
[IO.Directory]::CreateDirectory($resolvedWorkingRoot) | Out-Null

$scenarioPath = Join-Path $resolvedWorkingRoot 'representative.scenario.json'
$generatorPath = Join-Path $resolvedWorkingRoot 'fake-generation.ps1'
$driverPath = Join-Path $resolvedWorkingRoot 'run-generation-child.ps1'
[IO.File]::WriteAllText($scenarioPath, '{"scenario":"round-3"}', [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText(
    $generatorPath,
    @'
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputPath,
    [Parameter(Mandatory)][string]$ScenarioPath,
    [Parameter(Mandatory)][int]$Seed,
    [Parameter(Mandatory)][DateTimeOffset]$GeneratedAt,
    [Parameter(Mandatory)][string]$Mode,
    [Parameter(Mandatory)][string]$ApiToken,
    [Parameter(Mandatory)][string]$MutateScenario
)
[IO.Directory]::CreateDirectory($OutputPath) | Out-Null
$payload = [ordered]@{
    scenarioSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $ScenarioPath).Hash.ToLowerInvariant()
    seed = $Seed
    generatedAtUtc = $GeneratedAt.ToUniversalTime().ToString('O', [Globalization.CultureInfo]::InvariantCulture)
}
$json = (ConvertTo-Json -InputObject $payload -Compress) + "`n"
[IO.File]::WriteAllText((Join-Path $OutputPath 'payload.json'), $json, [Text.UTF8Encoding]::new($false))
if ($MutateScenario -eq 'true') {
    [IO.File]::AppendAllText($ScenarioPath, 'mutated', [Text.UTF8Encoding]::new($false))
}
'@,
    [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText(
    $driverPath,
    @'
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CandidatePath,
    [Parameter(Mandatory)][string]$ScenarioPath,
    [Parameter(Mandatory)][string]$GeneratorPath,
    [Parameter(Mandatory)][string]$RepoRoot,
    [Parameter(Mandatory)][string]$InvocationContractPath,
    [Parameter(Mandatory)][string]$GitPath,
    [Parameter(Mandatory)][string]$DotNetPath,
    [Parameter(Mandatory)][string]$Mode,
    [Parameter(Mandatory)][string]$ApiToken,
    [Parameter(Mandatory)][string]$MutateScenario
)
$ErrorActionPreference = 'Stop'
$invokePath = Join-Path $RepoRoot 'scripts\invoke-deterministic-generation.ps1'
$parameters = @{
    CandidatePath = $CandidatePath
    ScenarioPath = $ScenarioPath
    Seed = 1130
    GeneratedAt = [DateTimeOffset]'2026-07-22T00:00:00Z'
    GenerationScriptPath = $GeneratorPath
    GenerationArgumentList = [string[]]@('-Mode', $Mode, '-ApiToken', $ApiToken, '-MutateScenario', $MutateScenario)
    SensitiveGenerationArgumentName = [string[]]@('ApiToken')
    InvocationContractPath = $InvocationContractPath
    GitPath = $GitPath
    DotNetPath = $DotNetPath
    RepoRoot = $RepoRoot
}
& $invokePath @parameters | Out-Null
'@,
    [Text.UTF8Encoding]::new($false))

$modulePath = Join-Path $resolvedRepoRoot 'scripts\DeterminismEvidence.psm1'
$contractPath = Join-Path $resolvedRepoRoot 'scripts\new-generation-invocation-contract.ps1'
$receiptPath = Join-Path $resolvedRepoRoot 'scripts\new-determinism-receipt.ps1'
Import-Module $modulePath -Force

$runA = Join-Path $resolvedWorkingRoot 'candidate-a'
$runB = Join-Path $resolvedWorkingRoot 'candidate-b'
$generatedAt = [DateTimeOffset]'2026-07-22T00:00:00Z'
$generationArguments = @('-Mode', 'stable', '-ApiToken', 'dictionary-password-1', '-MutateScenario', 'false')
$sensitiveArgumentNames = @('ApiToken')
$pwshPath = (Get-Command pwsh -ErrorAction Stop).Source
$gitPath = (Get-Command git -ErrorAction Stop).Source
$dotnetPath = (Get-Command dotnet -ErrorAction Stop).Source
Assert-True ([IO.Path]::IsPathFullyQualified($gitPath)) 'GitPath must be fully qualified.'
Assert-True ([IO.Path]::IsPathFullyQualified($dotnetPath)) 'DotNetPath must be fully qualified.'

$contractAPath = Join-Path $resolvedWorkingRoot 'candidate-a.contract.json'
$contractBPath = Join-Path $resolvedWorkingRoot 'candidate-b.contract.json'
$contractParameters = @{
    ScenarioPath = $scenarioPath
    ExpectedGenerationScriptPath = $generatorPath
    Seed = 1130
    GeneratedAt = $generatedAt
    GenerationArgumentList = $generationArguments
    SensitiveGenerationArgumentName = $sensitiveArgumentNames
    RepoRoot = $resolvedRepoRoot
    GitPath = $gitPath
    DotNetPath = $dotnetPath
}
$contractA = & $contractPath @contractParameters -ChallengeLabel 'candidate-1' -OutputPath $contractAPath
$contractB = & $contractPath @contractParameters -ChallengeLabel 'candidate-2' -OutputPath $contractBPath
Assert-Equal 'datagen-generation-parent-contract-v1' $contractA.schemaVersion 'The parent contract must be versioned.'
Assert-True ($contractA.challenge.id -cne $contractB.challenge.id) 'Parent challenges must be distinct.'

$childA = Start-GenerationChild -PwshPath $pwshPath -DriverPath $driverPath -CandidatePath $runA -ScenarioPath $scenarioPath -GeneratorPath $generatorPath -RepoRoot $resolvedRepoRoot -InvocationContractPath $contractAPath -GitPath $gitPath -DotNetPath $dotnetPath -Mode 'stable' -ApiToken 'dictionary-password-1'
$childB = Start-GenerationChild -PwshPath $pwshPath -DriverPath $driverPath -CandidatePath $runB -ScenarioPath $scenarioPath -GeneratorPath $generatorPath -RepoRoot $resolvedRepoRoot -InvocationContractPath $contractBPath -GitPath $gitPath -DotNetPath $dotnetPath -Mode 'stable' -ApiToken 'dictionary-password-2'
Complete-GenerationChild -Child $childA -Label 'candidate-a child generation'
Complete-GenerationChild -Child $childB -Label 'candidate-b child generation'

Assert-Equal $contractA.expected.invocation.argumentDigestSha256 $contractB.expected.invocation.argumentDigestSha256 'Sensitive dictionary values must not affect the invocation digest.'
Assert-True $contractA.expected.invocation.sensitiveInputsExcludedFromReproducibility 'The contract must disclose excluded sensitive inputs.'
$contractTextA = Get-Content -LiteralPath $contractAPath -Raw
$contractTextB = Get-Content -LiteralPath $contractBPath -Raw
Assert-True (-not $contractTextA.Contains('dictionary-password-1', [StringComparison]::Ordinal)) 'Parent contract A must not persist the sensitive input.'
Assert-True (-not $contractTextB.Contains('dictionary-password-2', [StringComparison]::Ordinal)) 'Parent contract B must not persist the sensitive input.'

$positiveReceiptPath = Join-Path $resolvedWorkingRoot 'positive-receipt.json'
$receiptArguments = @{
    ScenarioPath = $scenarioPath
    Seed = 1130
    GeneratedAt = $generatedAt
    RepoRoot = $resolvedRepoRoot
    GitPath = $gitPath
    DotNetPath = $dotnetPath
    ExpectedInvocationContractPath = @($contractAPath, $contractBPath)
    ExpectedGenerationScriptPath = $generatorPath
    ExpectedInvocationInputDigest = $contractA.expected.invocation.argumentDigestSha256
    ExpectedGenerationArgumentList = $generationArguments
    SensitiveGenerationArgumentName = $sensitiveArgumentNames
    FailOnMismatch = $true
}
$positive = & $receiptPath @receiptArguments -CandidatePath @($runA, $runB) -OutputPath $positiveReceiptPath
Assert-Equal $true $positive.passed 'Distinct child-process generations with equal payloads must pass.'
Assert-Equal 2 @($positive.candidates).Count 'The receipt must describe both logical candidates.'
Assert-Equal 'candidate-1' $positive.candidates[0].label 'Candidate roots must use logical labels.'
Assert-True ($positive.candidates[0].run.process.id -ne $positive.candidates[1].run.process.id) 'Positive runs must use distinct child-process PIDs.'
$processIdentityA = "$($positive.candidates[0].run.process.id):$($positive.candidates[0].run.process.startTimeUtcTicks)"
$processIdentityB = "$($positive.candidates[1].run.process.id):$($positive.candidates[1].run.process.startTimeUtcTicks)"
Assert-True ($processIdentityA -cne $processIdentityB) 'Positive runs must have distinct PID/start identities.'
Assert-True ($positive.candidates[0].run.invocationId -cne $positive.candidates[1].run.invocationId) 'Positive runs must have distinct run IDs.'

$sidecarName = Get-GenerationProvenanceSidecarName
$sidecarAPath = Join-Path $runA $sidecarName
$sidecarBPath = Join-Path $runB $sidecarName
$sidecarAText = Get-Content -LiteralPath $sidecarAPath -Raw
$sidecarBText = Get-Content -LiteralPath $sidecarBPath -Raw
Assert-True (-not $sidecarAText.Contains('dictionary-password-1', [StringComparison]::Ordinal)) 'Sensitive generator arguments must not appear in sidecar A.'
Assert-True (-not $sidecarBText.Contains('dictionary-password-2', [StringComparison]::Ordinal)) 'Sensitive generator arguments must not appear in sidecar B.'

$portableJson = Get-Content -LiteralPath $positiveReceiptPath -Raw
$portableText = $portableJson.Replace('\\', '\')
Assert-True (-not $portableText.Contains($runA, [StringComparison]::OrdinalIgnoreCase)) 'The receipt must not contain candidate A absolute root.'
Assert-True (-not $portableText.Contains($runB, [StringComparison]::OrdinalIgnoreCase)) 'The receipt must not contain candidate B absolute root.'
Assert-True (-not $portableText.Contains('dictionary-password-', [StringComparison]::Ordinal)) 'The receipt must not contain a redacted generator argument value.'

$stableRead = Read-StableJsonEvidenceFile -Path $sidecarAPath
$manualSidecarHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stableRead.bytes)).ToLowerInvariant()
Assert-Equal $manualSidecarHash $stableRead.sha256 'Sidecar parsing and hashing must use the same byte buffer.'

$sameRootAlias = Join-Path $runA '.'
Assert-ThrowsLike -Pattern '*distinct resolved candidate roots*' -Because 'An aliased root cannot prove independent runs.' -Action {
    & $receiptPath @receiptArguments -CandidatePath @($runA, $sameRootAlias) -OutputPath (Join-Path $resolvedWorkingRoot 'same-root.json') | Out-Null
}

$copiedRun = Join-Path $resolvedWorkingRoot 'candidate-a-copy'
Copy-Item -LiteralPath $runA -Destination $copiedRun -Recurse
Assert-ThrowsLike -Pattern '*parent challenge*' -Because 'An unchanged copied sidecar is not independent evidence.' -Action {
    & $receiptPath @receiptArguments -CandidatePath @($runA, $copiedRun) -OutputPath (Join-Path $resolvedWorkingRoot 'copied.json') | Out-Null
}

$copiedSidecarPath = Join-Path $copiedRun $sidecarName
$copiedSidecar = Get-Content -LiteralPath $copiedSidecarPath -Raw | ConvertFrom-Json
$copiedSidecar.run.invocationId = [Guid]::NewGuid().ToString('D')
$copiedSidecar.run.process.id = [int]$copiedSidecar.run.process.id + 1
Set-Sidecar -Path $copiedSidecarPath -Value $copiedSidecar
Assert-ThrowsLike -Pattern '*parent challenge*' -Because 'Changing copied GUID/PID values cannot replace the preissued parent challenge.' -Action {
    & $receiptPath @receiptArguments -CandidatePath @($runA, $copiedRun) -OutputPath (Join-Path $resolvedWorkingRoot 'copied-new-guid.json') | Out-Null
}

$payloadPath = Join-Path $runB 'payload.json'
$payloadBytes = [IO.File]::ReadAllBytes($payloadPath)
[IO.File]::AppendAllText($payloadPath, 'tampered', [Text.UTF8Encoding]::new($false))
Assert-ThrowsLike -Pattern '*payload hash*' -Because 'A sidecar must bind to its candidate payload.' -Action {
    & $receiptPath @receiptArguments -CandidatePath @($runA, $runB) -OutputPath (Join-Path $resolvedWorkingRoot 'payload-mismatch.json') | Out-Null
}
[IO.File]::WriteAllBytes($payloadPath, $payloadBytes)

$sidecarJson = Get-Content -LiteralPath $sidecarBPath -Raw
$sidecar = $sidecarJson | ConvertFrom-Json
$sidecar.source.treeSha256 = '0' * 64
Set-Sidecar -Path $sidecarBPath -Value $sidecar
Assert-ThrowsLike -Pattern '*source tree*' -Because 'A sidecar from another source tree must fail.' -Action {
    & $receiptPath @receiptArguments -CandidatePath @($runA, $runB) -OutputPath (Join-Path $resolvedWorkingRoot 'source-mismatch.json') | Out-Null
}
[IO.File]::WriteAllText($sidecarBPath, $sidecarJson, [Text.UTF8Encoding]::new($false))

$sidecar = $sidecarJson | ConvertFrom-Json
$sidecar.source.version = '0.0.0-wrong'
Set-Sidecar -Path $sidecarBPath -Value $sidecar
Assert-ThrowsLike -Pattern '*source version*' -Because 'A sidecar from another version must fail.' -Action {
    & $receiptPath @receiptArguments -CandidatePath @($runA, $runB) -OutputPath (Join-Path $resolvedWorkingRoot 'version-mismatch.json') | Out-Null
}
[IO.File]::WriteAllText($sidecarBPath, $sidecarJson, [Text.UTF8Encoding]::new($false))

$sidecar = $sidecarJson | ConvertFrom-Json
$sidecar.runtime.dotnetSdk = '0.0.0-wrong'
Set-Sidecar -Path $sidecarBPath -Value $sidecar
Assert-ThrowsLike -Pattern '*runtime*' -Because 'A sidecar from another runtime must fail.' -Action {
    & $receiptPath @receiptArguments -CandidatePath @($runA, $runB) -OutputPath (Join-Path $resolvedWorkingRoot 'runtime-mismatch.json') | Out-Null
}
[IO.File]::WriteAllText($sidecarBPath, $sidecarJson, [Text.UTF8Encoding]::new($false))

$sidecar = $sidecarJson | ConvertFrom-Json
$sidecar.tool.generationScriptLogicalIdentity = 'external:fictitious.ps1'
$sidecar.tool.generationScriptSha256 = '0' * 64
Set-Sidecar -Path $sidecarBPath -Value $sidecar
Assert-ThrowsLike -Pattern '*expected generation tool*' -Because 'A fictitious or zero-hash generator identity must fail.' -Action {
    & $receiptPath @receiptArguments -CandidatePath @($runA, $runB) -OutputPath (Join-Path $resolvedWorkingRoot 'generator-mismatch.json') | Out-Null
}
[IO.File]::WriteAllText($sidecarBPath, $sidecarJson, [Text.UTF8Encoding]::new($false))

$runChangedArgs = Join-Path $resolvedWorkingRoot 'candidate-changed-args'
$changedChild = Start-GenerationChild -PwshPath $pwshPath -DriverPath $driverPath -CandidatePath $runChangedArgs -ScenarioPath $scenarioPath -GeneratorPath $generatorPath -RepoRoot $resolvedRepoRoot -InvocationContractPath $contractBPath -GitPath $gitPath -DotNetPath $dotnetPath -Mode 'changed' -ApiToken 'dictionary-password-2'
Complete-GenerationChildFailure -Child $changedChild -Label 'changed-argument child generation' -Pattern '*parent contract*'
Assert-True (-not (Test-Path -LiteralPath (Join-Path $runChangedArgs $sidecarName))) 'Changed public arguments must not emit a success sidecar.'

$mutableScenarioPath = Join-Path $resolvedWorkingRoot 'mutable.scenario.json'
[IO.File]::WriteAllText($mutableScenarioPath, '{"scenario":"mutable"}', [Text.UTF8Encoding]::new($false))
$mutableRun = Join-Path $resolvedWorkingRoot 'candidate-mutated-identity'
$mutableContractPath = Join-Path $resolvedWorkingRoot 'mutable.contract.json'
$mutableArguments = @('-Mode', 'stable', '-ApiToken', 'mutation-secret', '-MutateScenario', 'true')
$mutableContract = & $contractPath `
    -ScenarioPath $mutableScenarioPath `
    -ExpectedGenerationScriptPath $generatorPath `
    -Seed 1130 `
    -GeneratedAt $generatedAt `
    -GenerationArgumentList $mutableArguments `
    -SensitiveGenerationArgumentName $sensitiveArgumentNames `
    -ChallengeLabel 'candidate-1' `
    -OutputPath $mutableContractPath `
    -RepoRoot $resolvedRepoRoot `
    -GitPath $gitPath `
    -DotNetPath $dotnetPath
$mutationChild = Start-GenerationChild -PwshPath $pwshPath -DriverPath $driverPath -CandidatePath $mutableRun -ScenarioPath $mutableScenarioPath -GeneratorPath $generatorPath -RepoRoot $resolvedRepoRoot -InvocationContractPath $mutableContractPath -GitPath $gitPath -DotNetPath $dotnetPath -Mode 'stable' -ApiToken 'mutation-secret' -MutateScenario 'true'
Complete-GenerationChildFailure -Child $mutationChild -Label 'identity-mutation child generation' -Pattern '*identity changed*'
Assert-True (-not (Test-Path -LiteralPath (Join-Path $mutableRun $sidecarName))) 'A changed scenario identity must not emit a success sidecar.'

$patternSecretA = Get-GenerationInvocationIdentity -Seed 1130 -GeneratedAt $generatedAt -GenerationArgumentList @('-Password', 'dictionary-a') -SensitiveGenerationArgumentPattern @('^Password$')
$patternSecretB = Get-GenerationInvocationIdentity -Seed 1130 -GeneratedAt $generatedAt -GenerationArgumentList @('-Password', 'dictionary-b') -SensitiveGenerationArgumentPattern @('^Password$')
Assert-Equal $patternSecretA.argumentDigestSha256 $patternSecretB.argumentDigestSha256 'Pattern-selected sensitive values must not affect the digest.'
Assert-True (-not (ConvertTo-Json $patternSecretA -Depth 12).Contains('dictionary-a', [StringComparison]::Ordinal)) 'Pattern-selected sensitive values must not persist.'
$indexSecretA = Get-GenerationInvocationIdentity -Seed 1130 -GeneratedAt $generatedAt -GenerationArgumentList @('dictionary-a') -SensitiveGenerationArgumentIndex @(0)
$indexSecretB = Get-GenerationInvocationIdentity -Seed 1130 -GeneratedAt $generatedAt -GenerationArgumentList @('dictionary-b') -SensitiveGenerationArgumentIndex @(0)
Assert-Equal $indexSecretA.argumentDigestSha256 $indexSecretB.argumentDigestSha256 'Index-selected sensitive values must not affect the digest.'
$publicA = Get-GenerationInvocationIdentity -Seed 1130 -GeneratedAt $generatedAt -GenerationArgumentList @('-Mode', 'a')
$publicB = Get-GenerationInvocationIdentity -Seed 1130 -GeneratedAt $generatedAt -GenerationArgumentList @('-Mode', 'b')
Assert-True ($publicA.argumentDigestSha256 -cne $publicB.argumentDigestSha256) 'Public deterministic argument changes must affect the digest.'

$outsideDirectory = Join-Path $resolvedWorkingRoot 'outside-junction-target'
[IO.Directory]::CreateDirectory($outsideDirectory) | Out-Null
$junctionPath = Join-Path $runB 'linked-directory'
$junctionCreated = $false
try {
    New-Item -ItemType Junction -Path $junctionPath -Target $outsideDirectory -ErrorAction Stop | Out-Null
    $junctionCreated = $true
}
catch {
    Add-SkippedCase -Name 'directory-junction' -Reason $_.Exception.Message
}
if ($junctionCreated) {
    try {
        Assert-ThrowsLike -Pattern '*reparse point*' -Because 'A descendant directory junction must be rejected before inventory.' -Action {
            & $receiptPath @receiptArguments -CandidatePath @($runA, $runB) -OutputPath (Join-Path $resolvedWorkingRoot 'junction.json') | Out-Null
        }
    }
    finally {
        [IO.Directory]::Delete($junctionPath)
    }
}

$outsideFile = Join-Path $resolvedWorkingRoot 'outside-symlink-target.txt'
[IO.File]::WriteAllText($outsideFile, 'outside', [Text.UTF8Encoding]::new($false))
$symlinkPath = Join-Path $runB 'linked-file.txt'
$symlinkCreated = $false
try {
    New-Item -ItemType SymbolicLink -Path $symlinkPath -Target $outsideFile -ErrorAction Stop | Out-Null
    $symlinkCreated = $true
}
catch {
    Add-SkippedCase -Name 'file-symlink' -Reason $_.Exception.Message
}
if ($symlinkCreated) {
    try {
        Assert-ThrowsLike -Pattern '*reparse point*' -Because 'A descendant file symlink must be rejected before inventory.' -Action {
            & $receiptPath @receiptArguments -CandidatePath @($runA, $runB) -OutputPath (Join-Path $resolvedWorkingRoot 'symlink.json') | Out-Null
        }
    }
    finally {
        [IO.File]::Delete($symlinkPath)
    }
}

$rootJunction = Join-Path $resolvedWorkingRoot 'candidate-root-junction'
$rootJunctionCreated = $false
try {
    New-Item -ItemType Junction -Path $rootJunction -Target $runB -ErrorAction Stop | Out-Null
    $rootJunctionCreated = $true
}
catch {
    Add-SkippedCase -Name 'root-junction' -Reason $_.Exception.Message
}
if ($rootJunctionCreated) {
    try {
        Assert-ThrowsLike -Pattern '*reparse point*' -Because 'A candidate root junction must be rejected before inventory.' -Action {
            & $receiptPath @receiptArguments -CandidatePath @($runA, $rootJunction) -OutputPath (Join-Path $resolvedWorkingRoot 'root-junction.json') | Out-Null
        }
    }
    finally {
        [IO.Directory]::Delete($rootJunction)
    }
}

$stableRoot = Join-Path $resolvedWorkingRoot 'stable-read'
[IO.Directory]::CreateDirectory($stableRoot) | Out-Null
$stablePath = Join-Path $stableRoot 'payload.bin'
[IO.File]::WriteAllBytes($stablePath, [byte[]]::new(4096))
$writer = [IO.FileStream]::new($stablePath, [IO.FileMode]::Open, [IO.FileAccess]::Write, [IO.FileShare]::ReadWrite)
try {
    Assert-ThrowsLike -Pattern '*stable read*' -Because 'Inventory must reject a file with a concurrent write handle.' -Action {
        Get-CanonicalFileInventory -RootPath $stableRoot | Out-Null
    }
}
finally {
    $writer.Dispose()
}

$inventoryRoot = Join-Path $resolvedWorkingRoot 'inventory-order'
[IO.Directory]::CreateDirectory($inventoryRoot) | Out-Null
[IO.File]::WriteAllText((Join-Path $inventoryRoot 'a.txt'), 'a', [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $inventoryRoot 'b.txt'), 'b', [Text.UTF8Encoding]::new($false))
$forward = Get-CanonicalFileInventory -RootPath $inventoryRoot -RelativePaths @('b.txt', 'a.txt')
$reverse = Get-CanonicalFileInventory -RootPath $inventoryRoot -RelativePaths @('a.txt', 'b.txt')
Assert-Equal $forward.aggregateSha256 $reverse.aggregateSha256 'Canonical inventory hashing must ignore enumeration order.'
Assert-Equal 'a.txt' $forward.files[0].relativePath 'Canonical inventory paths must use ordinal order.'
Assert-ThrowsLike -Pattern '*file count*exceeds bound*' -Because 'Inventory file-count bounds must fail closed.' -Action {
    Get-CanonicalFileInventory -RootPath $inventoryRoot -MaximumFileCount 1 | Out-Null
}
Assert-ThrowsLike -Pattern '*path length*exceeds bound*' -Because 'Inventory path-length bounds must fail closed.' -Action {
    Get-CanonicalFileInventory -RootPath $inventoryRoot -MaximumPathLength 3 | Out-Null
}

$atomicPath = Join-Path $resolvedWorkingRoot 'atomic\evidence.json'
Write-CanonicalJsonFile -Value ([ordered]@{ value = 1 }) -Path $atomicPath
Write-CanonicalJsonFile -Value ([ordered]@{ value = 2 }) -Path $atomicPath
$atomicValue = Get-Content -LiteralPath $atomicPath -Raw | ConvertFrom-Json
Assert-Equal 2 $atomicValue.value 'Atomic publication must replace an existing receipt completely.'
Assert-Equal 0 @(Get-ChildItem -LiteralPath (Split-Path -Parent $atomicPath) -Filter '.evidence.json.*.tmp' -Force).Count 'Atomic publication must clean temporary files.'
Assert-ThrowsLike -Pattern '*JSON size*exceeds bound*' -Because 'Receipt-size bounds must fail closed.' -Action {
    Write-CanonicalJsonFile -Value ([ordered]@{ value = 'too-large' }) -Path $atomicPath -MaximumBytes 4
}

$miniRepo = Join-Path $resolvedWorkingRoot 'newline-source-repo'
[IO.Directory]::CreateDirectory($miniRepo) | Out-Null
& $gitPath -C $miniRepo init --quiet
if ($LASTEXITCODE -ne 0) { throw 'Unable to initialize newline source test repository.' }
[IO.Directory]::CreateDirectory((Join-Path $miniRepo '.beads')) | Out-Null
[IO.File]::WriteAllText((Join-Path $miniRepo '.beads\excluded.txt'), 'excluded', [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $miniRepo 'normal.txt'), 'included', [Text.UTF8Encoding]::new($false))
$newlineName = "line`nfeed.txt"
& $gitPath -C $miniRepo add -f -- .
if ($LASTEXITCODE -ne 0) { throw 'Unable to index newline source test files.' }
$miniSource = Get-DataGenSourceTreeIdentity -RepoRoot $miniRepo -GitPath $gitPath
$syntheticGitPaths = ConvertFrom-GitNullPathBytes -Bytes ([Text.UTF8Encoding]::new($false).GetBytes(".beads/excluded.txt`0.Beads/included.txt`0$newlineName`0"))
$syntheticSourcePaths = Select-DataGenSourcePath -Path $syntheticGitPaths
Assert-True (@($syntheticSourcePaths | Where-Object { $_ -ceq $newlineName }).Count -eq 1) 'NUL-delimited Git parsing must preserve newline filenames.'
Assert-True (@($syntheticSourcePaths | Where-Object { $_ -ceq '.Beads/included.txt' }).Count -eq 1) 'Source exclusion must remain case-sensitive.'
Assert-True (@($syntheticSourcePaths | Where-Object { $_ -ceq '.beads/excluded.txt' }).Count -eq 0) 'Synthetic lowercase .beads/ paths must be excluded.'
Assert-True (@($miniSource.files.relativePath | Where-Object { $_ -ceq 'normal.txt' }).Count -eq 1) 'NUL-delimited Git integration must retain ordinary source paths.'
Assert-True (@($miniSource.files.relativePath | Where-Object { $_.StartsWith('.beads/', [StringComparison]::Ordinal) }).Count -eq 0) 'Only the exact lowercase .beads/ prefix must be excluded.'

$miniRootJunction = Join-Path $resolvedWorkingRoot 'newline-source-root-junction'
$miniRootJunctionCreated = $false
try {
    New-Item -ItemType Junction -Path $miniRootJunction -Target $miniRepo -ErrorAction Stop | Out-Null
    $miniRootJunctionCreated = $true
}
catch {
    Add-SkippedCase -Name 'source-root-junction' -Reason $_.Exception.Message
}
if ($miniRootJunctionCreated) {
    try {
        Assert-ThrowsLike -Pattern '*Source root*reparse point*' -Because 'A source root junction must be rejected.' -Action {
            Get-DataGenSourceTreeIdentity -RepoRoot $miniRootJunction -GitPath $gitPath | Out-Null
        }
    }
    finally {
        [IO.Directory]::Delete($miniRootJunction)
    }
}

$source = Get-DataGenSourceTreeIdentity -RepoRoot $resolvedRepoRoot -GitPath $gitPath
Assert-Equal 'git-ls-files-z-v2:tracked+untracked-nonignored;exclude-prefix-ordinal=.beads/;path=/;order=ordinal' $source.inclusionSet 'Source-tree inclusion must be explicit and versioned.'
[string[]]$sortedSourcePaths = @($source.files.relativePath)
[Array]::Sort($sortedSourcePaths, [StringComparer]::Ordinal)
Assert-Equal (($source.files.relativePath) -join "`n") ($sortedSourcePaths -join "`n") 'Source-tree paths must be ordinally ordered.'
Assert-Equal 0 @($source.files.relativePath | Where-Object { $_ -like '.beads/*' }).Count 'Operational bead-store files must not affect source identity.'
$sourceCanonicalJson = ConvertTo-Json -InputObject @($source.files) -Depth 4 -Compress
$reconstructedSourceHash = [Convert]::ToHexString(
    [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($sourceCanonicalJson))).ToLowerInvariant()
Assert-Equal $source.aggregateSha256 $reconstructedSourceHash 'The source aggregate must be independently reconstructable from the documented inventory.'

[pscustomobject][ordered]@{
    passed = $true
    executedAssertionCount = $script:ExecutedAssertionCount
    skippedCaseCount = $script:SkippedCases.Count
    skippedCases = @($script:SkippedCases)
    positiveReceiptPath = $positiveReceiptPath
    childProcessIds = @($childA.Process.Id, $childB.Process.Id)
    sourceTreeSha256 = $source.aggregateSha256
    sourceTreeFileCount = $source.fileCount
}
