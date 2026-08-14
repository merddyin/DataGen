[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$SourceRoot,

    [Parameter(Mandatory)]
    [string]$OutputRoot,

    [Parameter()]
    [string]$GitPath = 'C:\Program Files\Git\cmd\git.exe',

    [Parameter()]
    [string]$DotNetPath = 'C:\Program Files\dotnet\dotnet.exe',

    [Parameter()]
    [string]$WindowsPowerShellPath = 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Test-PathContains {
    param([string]$ParentPath, [string]$ChildPath)

    $relativePath = [IO.Path]::GetRelativePath($ParentPath, $ChildPath)
    $relativePath -eq '.' -or (-not [IO.Path]::IsPathRooted($relativePath) -and -not $relativePath.StartsWith("..$([IO.Path]::DirectorySeparatorChar)") -and $relativePath -ne '..')
}

function Assert-SafeOutputRoot {
    param([string]$SourceRootPath, [string]$OutputRootPath)

    $source = [IO.Path]::GetFullPath($SourceRootPath).TrimEnd('\', '/')
    $output = [IO.Path]::GetFullPath($OutputRootPath).TrimEnd('\', '/')
    $filesystemRoot = [IO.Path]::GetPathRoot($output).TrimEnd('\', '/')
    if ($output -eq $filesystemRoot -or
        (Test-PathContains -ParentPath $source -ChildPath $output) -or
        (Test-PathContains -ParentPath $output -ChildPath $source)) {
        throw "Unsafe integration OutputRoot '$OutputRootPath'."
    }
}

function Invoke-Git {
    param([string]$RepositoryRoot, [string[]]$Arguments)

    $output = & $GitPath -C $RepositoryRoot @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed: $($output | Out-String)"
    }
    ($output | Out-String).Trim()
}

$sourceRoot = (Resolve-Path -LiteralPath $SourceRoot).Path
$outputRoot = [IO.Path]::GetFullPath($OutputRoot)
Assert-SafeOutputRoot -SourceRootPath $sourceRoot -OutputRootPath $outputRoot
if (Test-Path -LiteralPath $outputRoot) {
    throw "Integration OutputRoot '$outputRoot' must not already exist."
}
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

$candidateRoot = Join-Path $outputRoot 'clean-candidate'
$preflightOutputRoot = Join-Path $outputRoot 'preflight-output'
$publicCertificatePath = Join-Path $candidateRoot 'release-trust\datagen-release-preflight-attestation.cer'
$preflightLogPath = Join-Path $outputRoot 'preflight.log'
$preflightErrorPath = Join-Path $outputRoot 'preflight-error.log'
$testThumbprint = $null
$testCertificate = $null

try {
    New-Item -ItemType Directory -Path $candidateRoot -Force | Out-Null
    $sourceFiles = @(& $GitPath -C $sourceRoot ls-files --cached --others --exclude-standard)
    if ($LASTEXITCODE -ne 0 -or $sourceFiles.Count -eq 0) {
        throw 'Could not enumerate source files for the clean candidate integration fixture.'
    }
    foreach ($relativePath in $sourceFiles) {
        if ([string]::IsNullOrWhiteSpace($relativePath) -or [IO.Path]::IsPathRooted($relativePath) -or $relativePath -match '(^|[\\/])\.\.([\\/]|$)') {
            throw "Unsafe candidate source path '$relativePath'."
        }
        $sourcePath = Join-Path $sourceRoot $relativePath
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            continue
        }
        $destinationPath = Join-Path $candidateRoot $relativePath
        $destinationDirectory = Split-Path -Parent $destinationPath
        New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
        Copy-Item -LiteralPath $sourcePath -Destination $destinationPath
    }

    $certificateScript = @'
$ErrorActionPreference = 'Stop'
$certificate = New-SelfSignedCertificate -Type Custom -Subject 'CN=DataGen Clean Candidate Integration Attestation' -CertStoreLocation 'Cert:\CurrentUser\My' -KeyAlgorithm RSA -KeyLength 2048 -KeyUsage DigitalSignature -KeyExportPolicy NonExportable -HashAlgorithm SHA256 -NotAfter ([DateTime]::UtcNow.AddHours(2))
$certificate.Thumbprint
'@
    $testThumbprint = [string]@(& $WindowsPowerShellPath -NoLogo -NoProfile -NonInteractive -Command $certificateScript 2>&1 |
        ForEach-Object { $_.ToString().Trim() } |
        Where-Object { $_ -match '^[0-9A-F]{40}$' } |
        Select-Object -Last 1)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($testThumbprint)) {
        throw 'Could not create the clean-candidate ephemeral signing certificate.'
    }

    $certificateStore = [System.Security.Cryptography.X509Certificates.X509Store]::new('My', 'CurrentUser')
    $certificateStore.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)
    try {
        $testCertificate = @($certificateStore.Certificates.Find(
                [System.Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint,
                $testThumbprint,
                $false)) | Select-Object -First 1
    }
    finally {
        $certificateStore.Close()
    }
    if (-not $testCertificate -or -not $testCertificate.HasPrivateKey) {
        throw 'The clean-candidate ephemeral signing certificate is unavailable from CurrentUser certificate storage.'
    }
    [IO.Directory]::CreateDirectory((Split-Path -Parent $publicCertificatePath)) | Out-Null
    [IO.File]::WriteAllBytes(
        $publicCertificatePath,
        $testCertificate.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))

    & $GitPath -C $candidateRoot init --initial-branch=main | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not initialize the clean candidate repository.' }
    & $GitPath -C $candidateRoot config core.autocrlf false
    & $GitPath -C $candidateRoot config core.safecrlf false
    & $GitPath -C $candidateRoot config user.name 'DataGen Release Contract'
    & $GitPath -C $candidateRoot config user.email 'release-contract@datagen.invalid'
    & $GitPath -C $candidateRoot add --all
    & $GitPath -C $candidateRoot commit -m 'Clean candidate release preflight fixture' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not commit the clean candidate integration fixture.' }

    $candidateCommit = Invoke-Git -RepositoryRoot $candidateRoot -Arguments @('rev-parse', 'HEAD')
    $candidateTree = Invoke-Git -RepositoryRoot $candidateRoot -Arguments @('rev-parse', 'HEAD^{tree}')
    $preflightScript = Join-Path $candidateRoot 'scripts\invoke-release-preflight.ps1'
    $arguments = @(
        '-NoLogo', '-NoProfile', '-NonInteractive', '-File', $preflightScript,
        '-RepositoryRoot', $candidateRoot,
        '-OutputRoot', $preflightOutputRoot,
        '-CreateReleaseAttestation',
        '-SigningCertificateThumbprint', $testThumbprint,
        '-PublicCertificatePath', $publicCertificatePath,
        '-DotNetPath', ('"{0}"' -f $DotNetPath)
    )
    $process = Start-Process -FilePath "$PSHOME\pwsh.exe" -ArgumentList $arguments -PassThru -RedirectStandardOutput $preflightLogPath -RedirectStandardError $preflightErrorPath

    $snapshotBuildProperties = Join-Path $preflightOutputRoot 'source-snapshot\Directory.Build.props'
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(60)
    while (-not (Test-Path -LiteralPath $snapshotBuildProperties -PathType Leaf) -and -not $process.HasExited -and [DateTimeOffset]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 100
        $process.Refresh()
    }
    if (-not (Test-Path -LiteralPath $snapshotBuildProperties -PathType Leaf)) {
        $process.WaitForExit()
        throw "Preflight did not create its committed source snapshot. Exit $($process.ExitCode)."
    }

    $snapshotHashBeforeMutation = (Get-FileHash -LiteralPath $snapshotBuildProperties -Algorithm SHA256).Hash
    $liveBuildProperties = Join-Path $candidateRoot 'Directory.Build.props'
    $liveBytes = [IO.File]::ReadAllBytes($liveBuildProperties)
    try {
        [IO.File]::AppendAllText($liveBuildProperties, "`r`n<!-- transient adversarial mutation -->`r`n")
        Start-Sleep -Milliseconds 500
        if ((Get-FileHash -LiteralPath $snapshotBuildProperties -Algorithm SHA256).Hash -ne $snapshotHashBeforeMutation) {
            throw 'Transient live-checkout mutation changed the committed preflight snapshot.'
        }
    }
    finally {
        [IO.File]::WriteAllBytes($liveBuildProperties, $liveBytes)
    }

    $process.WaitForExit()
    if ($process.ExitCode -ne 0) {
        throw "Clean candidate preflight failed with exit $($process.ExitCode). See '$preflightLogPath' and '$preflightErrorPath'."
    }

    $attestationPath = Join-Path $preflightOutputRoot 'release-preflight-attestation.txt'
    $evidencePath = Join-Path $preflightOutputRoot 'release-preflight-evidence.json'
    $archivePath = Join-Path $preflightOutputRoot 'source-archive.tar'
    $manifestPath = Join-Path $preflightOutputRoot 'source-manifest.json'
    foreach ($expectedPath in @($attestationPath, $evidencePath, $archivePath, $manifestPath)) {
        if (-not (Test-Path -LiteralPath $expectedPath -PathType Leaf)) {
            throw "Clean candidate preflight did not produce '$expectedPath'."
        }
    }

    $evidence = Get-Content -LiteralPath $evidencePath -Raw | ConvertFrom-Json
    if ($evidence.SourceCommit -cne $candidateCommit -or $evidence.SourceTreeId -cne $candidateTree) {
        throw 'Signed release evidence is not bound to the clean candidate commit and tree.'
    }
    if ($evidence.SourceArchiveSha256 -cne (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()) {
        throw 'Signed release evidence archive hash does not match the retained committed-source archive.'
    }
    if ($evidence.SourceManifestSha256 -cne (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()) {
        throw 'Signed release evidence manifest hash does not match the retained snapshot manifest.'
    }
    if ((Get-FileHash -LiteralPath $snapshotBuildProperties -Algorithm SHA256).Hash -ne $snapshotHashBeforeMutation) {
        throw 'Committed preflight snapshot changed while source-dependent contracts executed.'
    }
    if ((Invoke-Git -RepositoryRoot $candidateRoot -Arguments @('status', '--porcelain=v1', '--untracked-files=all'))) {
        throw 'Clean candidate repository did not return to a clean state after the transient mutation.'
    }

    $secondArchivePath = Join-Path $outputRoot 'deterministic-source-archive.tar'
    & $GitPath -C $candidateRoot archive --format=tar --output=$secondArchivePath $candidateCommit
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the deterministic comparison archive.' }
    if ((Get-FileHash -LiteralPath $secondArchivePath -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash) {
        throw 'Repeated git archive output for the committed source object was not deterministic.'
    }

    $attestation = (Get-Content -LiteralPath $attestationPath -Raw).Trim()
    & "$PSHOME\pwsh.exe" -NoLogo -NoProfile -NonInteractive -File (Join-Path $candidateRoot 'scripts\assert-release-preflight-attestation.ps1') `
        -Attestation $attestation `
        -ExpectedVersion '0.11.0' `
        -ExpectedSourceCommit $candidateCommit `
        -ExpectedSourceTreeId $candidateTree `
        -PublicCertificatePath $publicCertificatePath
    if ($LASTEXITCODE -ne 0) {
        throw 'Clean candidate signed attestation did not validate against its committed pinned public certificate.'
    }

    Write-Host "Clean candidate signed preflight integration passed for commit $candidateCommit and tree $candidateTree." -ForegroundColor Green
}
finally {
    if ($testCertificate) {
        $testCertificate.Dispose()
    }
    if (-not [string]::IsNullOrWhiteSpace($testThumbprint)) {
        & 'C:\Windows\System32\certutil.exe' -user -delstore My $testThumbprint | Out-Null
    }
}
