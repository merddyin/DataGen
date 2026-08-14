[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$SourceRoot,

    [Parameter(Mandatory)]
    [string]$OutputRoot,

    [Parameter()]
    [switch]$ConcurrentArtifactRegressionOnly,

    [Parameter()]
    [switch]$TimestampInvalidationRegressionOnly,

    [Parameter()]
    [switch]$PackageCleanupSafetyRegressionOnly,

    [Parameter()]
    [switch]$AdversarialRegressionOnly,

    [Parameter()]
    [switch]$TimestampOffsetRegressionOnly,

    [Parameter()]
    [switch]$WindowsPublisherMetadataRegressionOnly,

    [Parameter()]
    [switch]$WindowsPublisherMetadataPortableOnly,

    [Parameter()]
    [switch]$ReleaseVersionContractOnly,

    [Parameter()]
    [switch]$ReleaseArtifactContractOnly,

    [Parameter()]
    [switch]$ReleaseWorkflowContractOnly,

    [Parameter()]
    [switch]$InternalRun,

    [Parameter()]
    [string]$DotNetPath = 'C:\Program Files\dotnet\dotnet.exe'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $InternalRun.IsPresent) {
    $childArguments = @(
        '-NoLogo',
        '-NoProfile',
        '-NonInteractive',
        '-File', $PSCommandPath,
        '-SourceRoot', $SourceRoot,
        '-OutputRoot', $OutputRoot,
        '-InternalRun'
    )
    if ($ConcurrentArtifactRegressionOnly.IsPresent) {
        $childArguments += '-ConcurrentArtifactRegressionOnly'
    }
    if ($TimestampInvalidationRegressionOnly.IsPresent) {
        $childArguments += '-TimestampInvalidationRegressionOnly'
    }
    if ($PackageCleanupSafetyRegressionOnly.IsPresent) {
        $childArguments += '-PackageCleanupSafetyRegressionOnly'
    }
    if ($AdversarialRegressionOnly.IsPresent) {
        $childArguments += '-AdversarialRegressionOnly'
    }
    if ($TimestampOffsetRegressionOnly.IsPresent) {
        $childArguments += '-TimestampOffsetRegressionOnly'
    }
    if ($WindowsPublisherMetadataRegressionOnly.IsPresent) {
        $childArguments += '-WindowsPublisherMetadataRegressionOnly'
    }
    if ($WindowsPublisherMetadataPortableOnly.IsPresent) {
        $childArguments += '-WindowsPublisherMetadataPortableOnly'
    }
    if ($ReleaseVersionContractOnly.IsPresent) {
        $childArguments += '-ReleaseVersionContractOnly'
    }
    if ($ReleaseArtifactContractOnly.IsPresent) {
        $childArguments += '-ReleaseArtifactContractOnly'
    }
    if ($ReleaseWorkflowContractOnly.IsPresent) {
        $childArguments += '-ReleaseWorkflowContractOnly'
    }
    $childArguments += @('-DotNetPath', $DotNetPath)

    $childOutput = & "$PSHOME\pwsh.exe" @childArguments 2>&1
    $childExitCode = $LASTEXITCODE
    $childOutput | ForEach-Object { Write-Output $_ }
    if ($childExitCode -ne 0) {
        throw "Clean-checkout contract child exited with code $childExitCode."
    }

    exit 0
}

function Test-PathContains {
    param(
        [Parameter(Mandatory)]
        [string]$ParentPath,

        [Parameter(Mandatory)]
        [string]$ChildPath
    )

    $relativePath = [IO.Path]::GetRelativePath($ParentPath, $ChildPath)
    return $relativePath -eq '.' -or (-not [IO.Path]::IsPathRooted($relativePath) -and -not $relativePath.StartsWith("..$([IO.Path]::DirectorySeparatorChar)") -and $relativePath -ne '..')
}

function Assert-SafeOutputRoot {
    param(
        [Parameter(Mandatory)]
        [string]$SourceRootPath,

        [Parameter(Mandatory)]
        [string]$OutputRootPath
    )

    $sourceRootPath = [IO.Path]::GetFullPath($SourceRootPath).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $outputRootPath = [IO.Path]::GetFullPath($OutputRootPath).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $outputPathRoot = [IO.Path]::GetPathRoot($outputRootPath).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)

    if ([string]::IsNullOrWhiteSpace($outputRootPath) -or $outputRootPath -eq $outputPathRoot) {
        throw "Unsafe OutputRoot '$OutputRootPath': filesystem roots cannot be cleaned."
    }

    if ((Test-PathContains -ParentPath $sourceRootPath -ChildPath $outputRootPath) -or (Test-PathContains -ParentPath $outputRootPath -ChildPath $sourceRootPath)) {
        throw "Unsafe OutputRoot '$OutputRootPath': it overlaps SourceRoot '$SourceRootPath'."
    }

    $currentPath = $outputRootPath
    while ($true) {
        if (Test-Path -LiteralPath $currentPath) {
            $item = Get-Item -LiteralPath $currentPath -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Unsafe OutputRoot '$OutputRootPath': '$currentPath' is or is beneath a reparse point."
            }
        }

        $parentPath = [IO.Directory]::GetParent($currentPath)
        if ($null -eq $parentPath) {
            break
        }

        $currentPath = $parentPath.FullName
    }
}

$sourceRoot = (Resolve-Path -LiteralPath $SourceRoot).Path
$outputRoot = [IO.Path]::GetFullPath($OutputRoot)
Assert-SafeOutputRoot -SourceRootPath $sourceRoot -OutputRootPath $outputRoot

function Assert-ReleaseWorkflowVersionContract {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot,

        [Parameter(Mandatory)]
        [string]$ContractOutputRoot
    )

    $workflowPath = Join-Path $RepositoryRoot '.github\workflows\release-module.yml'
    $workflow = Get-Content -LiteralPath $workflowPath -Raw
    $resolveStepMatch = [regex]::Match(
        $workflow,
        '(?ms)^      - name: Resolve release version\r?\n(?<step>.*?)(?=^      - name: )'
    )
    if (-not $resolveStepMatch.Success) {
        throw "Release workflow contract could not locate the 'Resolve release version' step in '$workflowPath'."
    }

    $resolveStep = $resolveStepMatch.Groups['step'].Value
    $runMatch = [regex]::Match($resolveStep, '(?ms)^        run: \|\r?\n(?<run>.*)$')
    if (-not $runMatch.Success) {
        throw "Release workflow contract could not locate the PowerShell run block in '$workflowPath'."
    }

    $runSource = $runMatch.Groups['run'].Value
    foreach ($expectedEnvironmentMapping in @(
        'RELEASE_EVENT_NAME: ${{ github.event_name }}',
        'RELEASE_INPUT_VERSION: ${{ inputs.version }}',
        'RELEASE_REF: ${{ github.ref }}'
    )) {
        if (-not $resolveStep.Contains($expectedEnvironmentMapping, [StringComparison]::Ordinal)) {
            throw "Release workflow contract requires '$expectedEnvironmentMapping' in the version step environment."
        }
    }

    if ($runSource -match '\$\{\{') {
        throw 'Release workflow contract forbids direct GitHub expression interpolation in the version PowerShell run block.'
    }

    foreach ($expectedDataRead in @(
        '$env:RELEASE_EVENT_NAME',
        '$env:RELEASE_INPUT_VERSION',
        '$env:RELEASE_REF'
    )) {
        if (-not $runSource.Contains($expectedDataRead, [StringComparison]::Ordinal)) {
            throw "Release workflow contract requires the version run block to read '$expectedDataRead' as data."
        }
    }

    if ($runSource -notmatch '\$version\s+-notmatch\s+') {
        throw 'Release workflow contract requires numeric version validation after environment data is resolved.'
    }
    if ($runSource -notmatch '\.\\scripts\\assert-release-version\.ps1\s+-Version\s+\$version') {
        throw 'Release workflow contract requires the shared release-version assertion after numeric validation.'
    }
    if ($runSource -notmatch '\$env:RELEASE_EVENT_NAME\s+-ne\s+''workflow_dispatch''') {
        throw 'Release workflow contract requires the version step to reject non-manual events.'
    }
    if ($runSource -notmatch '\$env:RELEASE_REF\s+-ne\s+''refs/heads/main''') {
        throw 'Release workflow contract requires manual release dispatch from refs/heads/main.'
    }
    if ($runSource -notmatch '\$version\s*=\s*\[string\]\$env:RELEASE_INPUT_VERSION') {
        throw 'Release workflow contract requires the version to come only from the manual input.'
    }

    $probeRoot = Join-Path $ContractOutputRoot 'release-workflow-version-probes'
    New-Item -ItemType Directory -Path $probeRoot -Force | Out-Null
    $probeScriptPath = Join-Path $probeRoot 'resolve-release-version.ps1'
    Set-Content -LiteralPath $probeScriptPath -Value $runSource -Encoding utf8 -NoNewline

    $cases = @(
        [pscustomobject]@{
            Name = 'workflow-dispatch-quoted-semicolon-comment'
            EventName = 'workflow_dispatch'
            InputVersion = "0.10.0';`$version='0.10.0';#"
            Ref = 'refs/heads/main'
        },
        [pscustomobject]@{
            Name = 'automatic-tag-push'
            EventName = 'push'
            InputVersion = '0.11.0'
            Ref = 'refs/tags/v0.11.0'
        },
        [pscustomobject]@{
            Name = 'manual-non-main-ref'
            EventName = 'workflow_dispatch'
            InputVersion = '0.11.0'
            Ref = 'refs/heads/work/release-probe'
        }
    )

    foreach ($case in $cases) {
        $caseRoot = Join-Path $probeRoot $case.Name
        New-Item -ItemType Directory -Path $caseRoot -Force | Out-Null
        $githubOutputPath = Join-Path $caseRoot 'github-output.txt'
        $previousEventName = [Environment]::GetEnvironmentVariable('RELEASE_EVENT_NAME', 'Process')
        $previousInputVersion = [Environment]::GetEnvironmentVariable('RELEASE_INPUT_VERSION', 'Process')
        $previousRef = [Environment]::GetEnvironmentVariable('RELEASE_REF', 'Process')
        $previousGithubOutput = [Environment]::GetEnvironmentVariable('GITHUB_OUTPUT', 'Process')

        try {
            $env:RELEASE_EVENT_NAME = $case.EventName
            $env:RELEASE_INPUT_VERSION = $case.InputVersion
            $env:RELEASE_REF = $case.Ref
            $env:GITHUB_OUTPUT = $githubOutputPath

            Push-Location $RepositoryRoot
            try {
                $probeOutput = & "$PSHOME\pwsh.exe" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $probeScriptPath 2>&1 | Out-String
                $probeExitCode = $LASTEXITCODE
            }
            finally {
                Pop-Location
            }
        }
        finally {
            $env:RELEASE_EVENT_NAME = $previousEventName
            $env:RELEASE_INPUT_VERSION = $previousInputVersion
            $env:RELEASE_REF = $previousRef
            $env:GITHUB_OUTPUT = $previousGithubOutput
            $global:LASTEXITCODE = 0
        }

        if ($probeExitCode -eq 0) {
            throw "Release workflow accepted malicious $($case.Name) payload as a valid version. Output: $probeOutput"
        }
        if (Test-Path -LiteralPath $githubOutputPath) {
            $githubOutput = Get-Content -LiteralPath $githubOutputPath -Raw
            if (-not [string]::IsNullOrEmpty($githubOutput)) {
                throw "Release workflow wrote a release output before rejecting malicious $($case.Name) payload: $githubOutput"
            }
        }
    }
}

function Assert-ReleasePublicationAttestationContract {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot,

        [Parameter(Mandatory)]
        [string]$ContractOutputRoot
    )

    $attestationWriter = Join-Path $RepositoryRoot 'scripts\new-release-preflight-attestation.ps1'
    $attestationValidator = Join-Path $RepositoryRoot 'scripts\assert-release-preflight-attestation.ps1'
    $releasePreflight = Join-Path $RepositoryRoot 'scripts\invoke-release-preflight.ps1'
    $releaseTrustModule = Join-Path $RepositoryRoot 'scripts\release-trust\DataGen.ReleasePreflightAttestation.psm1'
    foreach ($requiredScript in @($attestationWriter, $attestationValidator, $releasePreflight, $releaseTrustModule)) {
        if (-not (Test-Path -LiteralPath $requiredScript -PathType Leaf)) {
            throw "Release publication attestation contract requires '$requiredScript'."
        }
    }

    $releasePreflightSource = Get-Content -LiteralPath $releasePreflight -Raw
    foreach ($requiredPreflightToken in @(
        '[switch]$CreateReleaseAttestation',
        "`$branch -cne 'main'",
        "'status', '--porcelain=v1', '--untracked-files=all'",
        "'windows-publisher-metadata-cross-filesystem'",
        "'WindowsPublisherMetadataRegressionOnly'",
        'new-release-preflight-attestation.ps1'
    )) {
        if (-not $releasePreflightSource.Contains($requiredPreflightToken, [StringComparison]::Ordinal)) {
            throw "Release preflight attestation contract requires '$requiredPreflightToken'."
        }
    }
    if ($releasePreflightSource -notmatch '\$RequireWindowsPublisherMetadataCrossFilesystemEvidence\.IsPresent\s+-or\s+\$CreateReleaseAttestation\.IsPresent') {
        throw 'Release attestation creation must make the real cross-filesystem publisher regression mandatory.'
    }
    $catalogContractSource = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'tests\CleanCheckoutCatalogContract.Tests.ps1') -Raw
    $forbiddenSnapshotProbeToken = "Join-Path `$sourceRoot " + "'artifacts'"
    if ($catalogContractSource.Contains($forbiddenSnapshotProbeToken, [StringComparison]::Ordinal)) {
        throw 'Cross-filesystem publisher probes must not derive a D: or G: operation root from the immutable source snapshot.'
    }
    foreach ($requiredProbeSafetyToken in @(
        'function Resolve-WindowsPublisherMetadataProbeRoot',
        'DataGenWindowsPublisherProof',
        'Get-Volume -DriveLetter $driveLetter',
        'Refusing to clean publisher metadata probe'
    )) {
        if (-not $catalogContractSource.Contains($requiredProbeSafetyToken, [StringComparison]::Ordinal)) {
            throw "Cross-filesystem publisher probe contract requires '$requiredProbeSafetyToken'."
        }
    }

    $probeRoot = Join-Path $ContractOutputRoot 'release-attestation-probes'
    New-Item -ItemType Directory -Path $probeRoot -Force | Out-Null
    $evidencePath = Join-Path $probeRoot 'release-preflight-evidence.json'
    $sourceCommit = '0123456789abcdef0123456789abcdef01234567'
    $sourceTreeId = '89abcdef0123456789abcdef0123456789abcdef'
    $completedAt = '2026-08-14T12:00:00Z'
    [ordered]@{
        Schema = 'datagen-release-preflight-evidence-v2'
        Version = '0.11.0'
        SourceCommit = $sourceCommit
        SourceTreeId = $sourceTreeId
        SourceArchiveSha256 = 'a' * 64
        SourceManifestSha256 = 'b' * 64
        Branch = 'main'
        CompletedAtUtc = $completedAt
        Workstation = 'release-contract-test'
        Volumes = [ordered]@{
            D = [ordered]@{ FileSystem = 'NTFS'; DriveType = 'Fixed' }
            G = [ordered]@{ FileSystem = 'ReFS'; DriveType = 'Fixed' }
        }
        Contracts = [ordered]@{
            ReleaseVersion = 'passed'
            WindowsPublisherMetadataPortable = 'passed'
            WindowsPublisherMetadataCrossFilesystem = 'passed'
        }
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $evidencePath -NoNewline
    $trustedCertificate = $null
    $wrongCertificate = $null
    $trustedThumbprint = $null
    $wrongThumbprint = $null
    $windowsPowerShell = 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe'
    $publicCertificatePath = Join-Path $probeRoot 'trusted-release-attestation.cer'
    try {
        function New-EphemeralAttestationCertificate {
            param([Parameter(Mandatory)][string]$Subject)

            $creationScript = @"
`$ErrorActionPreference = 'Stop'
`$certificate = New-SelfSignedCertificate -Type Custom -Subject '$Subject' -CertStoreLocation 'Cert:\CurrentUser\My' -KeyAlgorithm RSA -KeyLength 2048 -KeyUsage DigitalSignature -KeyExportPolicy NonExportable -HashAlgorithm SHA256 -NotAfter ([DateTime]::UtcNow.AddHours(1))
`$certificate.Thumbprint
"@
            $thumbprint = @(& $windowsPowerShell -NoLogo -NoProfile -NonInteractive -Command $creationScript 2>&1 | ForEach-Object { $_.ToString().Trim() } | Where-Object { $_ -match '^[0-9A-F]{40}$' } | Select-Object -Last 1)
            if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($thumbprint)) {
                throw "Could not create ephemeral test certificate '$Subject'."
            }
            return $thumbprint
        }

        $trustedThumbprint = New-EphemeralAttestationCertificate -Subject 'CN=DataGen Test Release Preflight Attestation Trusted'
        $wrongThumbprint = New-EphemeralAttestationCertificate -Subject 'CN=DataGen Test Release Preflight Attestation Wrong'
        $store = [System.Security.Cryptography.X509Certificates.X509Store]::new('My', 'CurrentUser')
        $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)
        try {
            $trustedCertificate = @($store.Certificates.Find([System.Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint, $trustedThumbprint, $false)) | Select-Object -First 1
            $wrongCertificate = @($store.Certificates.Find([System.Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint, $wrongThumbprint, $false)) | Select-Object -First 1
        }
        finally {
            $store.Close()
        }
        if (-not $trustedCertificate -or -not $wrongCertificate) {
            throw 'Ephemeral release-attestation certificates were not available from CurrentUser certificate storage.'
        }
        [IO.File]::WriteAllBytes($publicCertificatePath, $trustedCertificate.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))

        $writerOutput = & "$PSHOME\pwsh.exe" -NoLogo -NoProfile -NonInteractive -File $attestationWriter `
            -EvidencePath $evidencePath `
            -Version '0.11.0' `
            -SourceCommit $sourceCommit `
            -CompletedAtUtc $completedAt `
            -SigningCertificateThumbprint $trustedThumbprint `
            -PublicCertificatePath $publicCertificatePath 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "Release attestation writer failed: $($writerOutput | Out-String)"
        }
        $attestation = @($writerOutput | ForEach-Object { $_.ToString() } |
            Where-Object { $_.StartsWith('datagen-release-preflight-v3|', [StringComparison]::Ordinal) }) | Select-Object -Last 1
        if ([string]::IsNullOrWhiteSpace($attestation)) {
            throw 'Release attestation writer did not emit a signed durable workflow input value.'
        }

        Import-Module $releaseTrustModule -Force
        $parsedAttestation = ConvertFrom-ReleasePreflightAttestation -Attestation $attestation
        if ($parsedAttestation.SourceTreeId -cne $sourceTreeId -or
            $parsedAttestation.SourceArchiveSha256 -cne ('a' * 64) -or
            $parsedAttestation.SourceManifestSha256 -cne ('b' * 64) -or
            $parsedAttestation.DFileSystem -cne 'NTFS' -or
            $parsedAttestation.DResult -cne 'passed' -or
            $parsedAttestation.GFileSystem -cne 'ReFS' -or
            $parsedAttestation.GResult -cne 'passed') {
            throw 'Release attestation payload did not retain the required canonical source and D:/G: evidence claims.'
        }

        $malformedEvidencePath = Join-Path $probeRoot 'malformed-release-preflight-evidence.json'
        Set-Content -LiteralPath $malformedEvidencePath -Value '{"Schema":"datagen-release-preflight-evidence-v2","Version":"0.11.0"}' -NoNewline
        $malformedWriterOutput = & "$PSHOME\pwsh.exe" -NoLogo -NoProfile -NonInteractive -File $attestationWriter `
            -EvidencePath $malformedEvidencePath `
            -Version '0.11.0' `
            -SourceCommit $sourceCommit `
            -CompletedAtUtc $completedAt `
            -SigningCertificateThumbprint $trustedThumbprint `
            -PublicCertificatePath $publicCertificatePath 2>&1 | Out-String
        if ($LASTEXITCODE -eq 0 -or $malformedWriterOutput -notmatch 'evidence') {
            throw 'Release attestation writer accepted malformed release-preflight evidence.'
        }
        $mismatchedEvidencePath = Join-Path $probeRoot 'mismatched-release-preflight-evidence.json'
        $mismatchedEvidence = Get-Content -LiteralPath $evidencePath -Raw | ConvertFrom-Json -AsHashtable -Depth 8
        $mismatchedEvidence['SourceCommit'] = ('f' * 40)
        $mismatchedEvidence | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $mismatchedEvidencePath -NoNewline
        $mismatchedWriterOutput = & "$PSHOME\pwsh.exe" -NoLogo -NoProfile -NonInteractive -File $attestationWriter `
            -EvidencePath $mismatchedEvidencePath `
            -Version '0.11.0' `
            -SourceCommit $sourceCommit `
            -CompletedAtUtc $completedAt `
            -SigningCertificateThumbprint $trustedThumbprint `
            -PublicCertificatePath $publicCertificatePath 2>&1 | Out-String
        if ($LASTEXITCODE -eq 0 -or $mismatchedWriterOutput -notmatch 'evidence') {
            throw 'Release attestation writer accepted evidence whose source commit disagreed with the signing request.'
        }

        function Assert-EvidenceWriterRejectsJson {
            param(
                [Parameter(Mandatory)][string]$Name,
                [Parameter(Mandatory)][string]$Json
            )

            $invalidEvidencePath = Join-Path $probeRoot "$Name-release-preflight-evidence.json"
            [IO.File]::WriteAllText($invalidEvidencePath, $Json, [Text.UTF8Encoding]::new($false))
            $invalidWriterOutput = & "$PSHOME\pwsh.exe" -NoLogo -NoProfile -NonInteractive -File $attestationWriter `
                -EvidencePath $invalidEvidencePath `
                -Version '0.11.0' `
                -SourceCommit $sourceCommit `
                -CompletedAtUtc $completedAt `
                -SigningCertificateThumbprint $trustedThumbprint `
                -PublicCertificatePath $publicCertificatePath 2>&1 | Out-String
            if ($LASTEXITCODE -eq 0 -or $invalidWriterOutput -notmatch 'evidence') {
                throw "Release attestation writer accepted invalid evidence case '$Name'. Output: $invalidWriterOutput"
            }
        }

        function Add-EvidenceJsonProperty {
            param(
                [Parameter(Mandatory)][string]$Json,
                [string]$ObjectProperty,
                [Parameter(Mandatory)][string]$PropertyJson
            )

            $objectStart = if ([string]::IsNullOrWhiteSpace($ObjectProperty)) {
                0
            }
            else {
                $marker = '"' + $ObjectProperty + '": {'
                $markerIndex = $Json.IndexOf($marker, [StringComparison]::Ordinal)
                if ($markerIndex -lt 0) {
                    throw "Evidence fixture does not contain object '$ObjectProperty'."
                }
                $Json.IndexOf('{', $markerIndex)
            }
            return $Json.Insert($objectStart + 1, "`n    $PropertyJson,")
        }

        $validEvidenceJson = Get-Content -LiteralPath $evidencePath -Raw
        $wrongTypeEvidence = $validEvidenceJson -replace '"Workstation"\s*:\s*"[^"]+"', '"Workstation": 7'
        foreach ($invalidEvidence in @(
                [pscustomobject]@{ Name = 'extra-top-level'; Json = (Add-EvidenceJsonProperty -Json $validEvidenceJson -PropertyJson '"UnexpectedTopLevel": "forbidden"') },
                [pscustomobject]@{ Name = 'extra-volume-member'; Json = (Add-EvidenceJsonProperty -Json $validEvidenceJson -ObjectProperty 'D' -PropertyJson '"UnexpectedVolumeMember": "forbidden"') },
                [pscustomobject]@{ Name = 'extra-contract-member'; Json = (Add-EvidenceJsonProperty -Json $validEvidenceJson -ObjectProperty 'Contracts' -PropertyJson '"UnexpectedContractMember": "forbidden"') },
                [pscustomobject]@{ Name = 'duplicate-top-level'; Json = (Add-EvidenceJsonProperty -Json $validEvidenceJson -PropertyJson '"Schema": "datagen-release-preflight-evidence-v2"') },
                [pscustomobject]@{ Name = 'case-colliding-top-level'; Json = (Add-EvidenceJsonProperty -Json $validEvidenceJson -PropertyJson '"schema": "datagen-release-preflight-evidence-v2"') },
                [pscustomobject]@{ Name = 'wrong-top-level-type'; Json = $wrongTypeEvidence }
            )) {
            Assert-EvidenceWriterRejectsJson -Name $invalidEvidence.Name -Json $invalidEvidence.Json
        }

        $fourPartEvidencePath = Join-Path $probeRoot 'four-part-release-preflight-evidence.json'
        $fourPartEvidence = Get-Content -LiteralPath $evidencePath -Raw | ConvertFrom-Json -AsHashtable -Depth 8
        $fourPartEvidence['Version'] = '0.11.0.1'
        $fourPartEvidence | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $fourPartEvidencePath -NoNewline
        $fourPartWriterOutput = & "$PSHOME\pwsh.exe" -NoLogo -NoProfile -NonInteractive -File $attestationWriter `
            -EvidencePath $fourPartEvidencePath `
            -Version '0.11.0.1' `
            -SourceCommit $sourceCommit `
            -CompletedAtUtc $completedAt `
            -SigningCertificateThumbprint $trustedThumbprint `
            -PublicCertificatePath $publicCertificatePath 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "Four-part release attestation writer failed: $($fourPartWriterOutput | Out-String)"
        }
        $fourPartAttestation = @($fourPartWriterOutput | ForEach-Object { $_.ToString() } |
            Where-Object { $_.StartsWith('datagen-release-preflight-v3|', [StringComparison]::Ordinal) }) | Select-Object -Last 1
        if ([string]::IsNullOrWhiteSpace($fourPartAttestation)) {
            throw 'Release attestation writer did not emit a signed four-part-version workflow input value.'
        }

        function New-EquivalentNonCanonicalBase64UrlValue {
            param([Parameter(Mandatory)][string]$Value)

            $alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_'
            $unusedBitCount = switch ($Value.Length % 4) {
                2 { 4 }
                3 { 2 }
                default { 0 }
            }
            if ($unusedBitCount -eq 0) {
                throw "Fixture '$Value' has no unused base64url bits to mutate."
            }
            $lastIndex = $alphabet.IndexOf($Value[$Value.Length - 1])
            $canonicalStride = 1 -shl $unusedBitCount
            if ($lastIndex -lt 0 -or $lastIndex % $canonicalStride -ne 0) {
                throw 'Fixture is not a canonical base64url value with the expected unused-bit shape.'
            }
            return $Value.Substring(0, $Value.Length - 1) + $alphabet[$lastIndex + 1]
        }

        $fourPartEnvelopeMatch = [regex]::Match(
            $fourPartAttestation,
            '^datagen-release-preflight-v3\|payload=(?<payload>[A-Za-z0-9_-]+)\|signature=(?<signature>[A-Za-z0-9_-]+)$')
        if (-not $fourPartEnvelopeMatch.Success) {
            throw 'Four-part release attestation fixture is malformed.'
        }
        $nonCanonicalFourPartPayload = New-EquivalentNonCanonicalBase64UrlValue -Value $fourPartEnvelopeMatch.Groups['payload'].Value
        $nonCanonicalFourPartSignature = New-EquivalentNonCanonicalBase64UrlValue -Value $fourPartEnvelopeMatch.Groups['signature'].Value
        $nonCanonicalPayloadAttestation = "datagen-release-preflight-v3|payload=$nonCanonicalFourPartPayload|signature=$($fourPartEnvelopeMatch.Groups['signature'].Value)"
        $nonCanonicalSignatureAttestation = "datagen-release-preflight-v3|payload=$($fourPartEnvelopeMatch.Groups['payload'].Value)|signature=$nonCanonicalFourPartSignature"
        $attestationClaimArguments = @{
            SourceTreeId = $parsedAttestation.SourceTreeId
            SourceArchiveSha256 = $parsedAttestation.SourceArchiveSha256
            SourceManifestSha256 = $parsedAttestation.SourceManifestSha256
            DFileSystem = $parsedAttestation.DFileSystem
            DResult = $parsedAttestation.DResult
            GFileSystem = $parsedAttestation.GFileSystem
            GResult = $parsedAttestation.GResult
        }
        $wrongKeyPayload = New-ReleasePreflightAttestationPayload `
            -Version '0.11.0' `
            -SourceCommit $sourceCommit `
            @attestationClaimArguments `
            -CompletedAtUtc $completedAt `
            -EvidenceHash $parsedAttestation.EvidenceHash `
            -KeyId $parsedAttestation.KeyId
        $wrongKeyRsa = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($wrongCertificate)
        if (-not $wrongKeyRsa) {
            throw 'The ephemeral wrong-key certificate does not expose a private RSA key.'
        }
        try {
            $wrongKeySignature = $wrongKeyRsa.SignData(
                [Text.Encoding]::UTF8.GetBytes($wrongKeyPayload),
                [System.Security.Cryptography.HashAlgorithmName]::SHA256,
                [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
        }
        finally {
            $wrongKeyRsa.Dispose()
        }
        $wrongKeyAttestation = New-ReleasePreflightAttestationEnvelope -Payload $wrongKeyPayload -Signature $wrongKeySignature
        function New-TrustedAttestationEnvelope {
            param([Parameter(Mandatory)][string]$Payload)

            $trustedRsa = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($trustedCertificate)
            if (-not $trustedRsa) {
                throw 'The ephemeral trusted certificate does not expose a private RSA key.'
            }
            try {
                $signature = $trustedRsa.SignData(
                    [Text.Encoding]::UTF8.GetBytes($Payload),
                    [System.Security.Cryptography.HashAlgorithmName]::SHA256,
                    [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
                return New-ReleasePreflightAttestationEnvelope -Payload $Payload -Signature $signature
            }
            finally {
                $trustedRsa.Dispose()
            }
        }
        $missingClaimAttestation = New-TrustedAttestationEnvelope -Payload ($parsedAttestation.Payload -replace "`ng_result=passed$", '')
        $wrongClaimAttestation = New-TrustedAttestationEnvelope -Payload ($parsedAttestation.Payload -replace 'd_result=passed', 'd_result=failed')
        $cases = @(
            [pscustomobject]@{ Name = 'valid'; Attestation = $attestation; Version = '0.11.0'; Source = $sourceCommit; SourceTree = $sourceTreeId; Now = '2026-08-14T13:00:00Z'; PublicCertificatePath = $publicCertificatePath; ShouldPass = $true },
            [pscustomobject]@{ Name = 'valid-four-part-version'; Attestation = $fourPartAttestation; Version = '0.11.0.1'; Source = $sourceCommit; Now = '2026-08-14T13:00:00Z'; PublicCertificatePath = $publicCertificatePath; ShouldPass = $true },
            [pscustomobject]@{ Name = 'noncanonical-four-part-payload'; Attestation = $nonCanonicalPayloadAttestation; Version = '0.11.0.1'; Source = $sourceCommit; Now = '2026-08-14T13:00:00Z'; PublicCertificatePath = $publicCertificatePath; ShouldPass = $false },
            [pscustomobject]@{ Name = 'noncanonical-four-part-signature'; Attestation = $nonCanonicalSignatureAttestation; Version = '0.11.0.1'; Source = $sourceCommit; Now = '2026-08-14T13:00:00Z'; PublicCertificatePath = $publicCertificatePath; ShouldPass = $false },
            [pscustomobject]@{ Name = 'missing'; Attestation = ''; Version = '0.11.0'; Source = $sourceCommit; Now = '2026-08-14T13:00:00Z'; PublicCertificatePath = $publicCertificatePath; ShouldPass = $false },
            [pscustomobject]@{ Name = 'forged-evidence'; Attestation = (New-ReleasePreflightAttestationEnvelope -Payload (New-ReleasePreflightAttestationPayload -Version '0.11.0' -SourceCommit $sourceCommit @attestationClaimArguments -CompletedAtUtc $completedAt -EvidenceHash ('0' * 64) -KeyId $parsedAttestation.KeyId) -Signature $parsedAttestation.Signature); Version = '0.11.0'; Source = $sourceCommit; Now = '2026-08-14T13:00:00Z'; PublicCertificatePath = $publicCertificatePath; ShouldPass = $false },
            [pscustomobject]@{ Name = 'modified-source'; Attestation = (New-ReleasePreflightAttestationEnvelope -Payload (New-ReleasePreflightAttestationPayload -Version '0.11.0' -SourceCommit ('f' * 40) @attestationClaimArguments -CompletedAtUtc $completedAt -EvidenceHash $parsedAttestation.EvidenceHash -KeyId $parsedAttestation.KeyId) -Signature $parsedAttestation.Signature); Version = '0.11.0'; Source = $sourceCommit; Now = '2026-08-14T13:00:00Z'; PublicCertificatePath = $publicCertificatePath; ShouldPass = $false },
            [pscustomobject]@{ Name = 'modified-version'; Attestation = (New-ReleasePreflightAttestationPayload -Version '0.11.1' -SourceCommit $sourceCommit @attestationClaimArguments -CompletedAtUtc $completedAt -EvidenceHash $parsedAttestation.EvidenceHash -KeyId $parsedAttestation.KeyId | ForEach-Object { New-ReleasePreflightAttestationEnvelope -Payload $_ -Signature $parsedAttestation.Signature }); Version = '0.11.0'; Source = $sourceCommit; Now = '2026-08-14T13:00:00Z'; PublicCertificatePath = $publicCertificatePath; ShouldPass = $false },
            [pscustomobject]@{ Name = 'modified-completion'; Attestation = (New-ReleasePreflightAttestationPayload -Version '0.11.0' -SourceCommit $sourceCommit @attestationClaimArguments -CompletedAtUtc '2026-08-14T12:00:01Z' -EvidenceHash $parsedAttestation.EvidenceHash -KeyId $parsedAttestation.KeyId | ForEach-Object { New-ReleasePreflightAttestationEnvelope -Payload $_ -Signature $parsedAttestation.Signature }); Version = '0.11.0'; Source = $sourceCommit; Now = '2026-08-14T13:00:00Z'; PublicCertificatePath = $publicCertificatePath; ShouldPass = $false },
            [pscustomobject]@{ Name = 'wrong-key'; Attestation = $wrongKeyAttestation; Version = '0.11.0'; Source = $sourceCommit; Now = '2026-08-14T13:00:00Z'; PublicCertificatePath = $publicCertificatePath; ShouldPass = $false },
            [pscustomobject]@{ Name = 'missing-g-result-claim'; Attestation = $missingClaimAttestation; Version = '0.11.0'; Source = $sourceCommit; Now = '2026-08-14T13:00:00Z'; PublicCertificatePath = $publicCertificatePath; ShouldPass = $false },
            [pscustomobject]@{ Name = 'failed-d-result-claim'; Attestation = $wrongClaimAttestation; Version = '0.11.0'; Source = $sourceCommit; Now = '2026-08-14T13:00:00Z'; PublicCertificatePath = $publicCertificatePath; ShouldPass = $false },
            [pscustomobject]@{ Name = 'missing-signature'; Attestation = 'datagen-release-preflight-v3|payload=missing-signature'; Version = '0.11.0'; Source = $sourceCommit; Now = '2026-08-14T13:00:00Z'; PublicCertificatePath = $publicCertificatePath; ShouldPass = $false },
            [pscustomobject]@{ Name = 'missing-public-key'; Attestation = $attestation; Version = '0.11.0'; Source = $sourceCommit; Now = '2026-08-14T13:00:00Z'; PublicCertificatePath = (Join-Path $probeRoot 'missing-public-key.cer'); ShouldPass = $false },
            [pscustomobject]@{ Name = 'stale'; Attestation = $attestation; Version = '0.11.0'; Source = $sourceCommit; Now = '2026-08-15T12:00:01Z'; PublicCertificatePath = $publicCertificatePath; ShouldPass = $false }
        )
        foreach ($case in $cases) {
            $probeOutput = & "$PSHOME\pwsh.exe" -NoLogo -NoProfile -NonInteractive -File $attestationValidator `
                -Attestation $case.Attestation `
                -ExpectedVersion $case.Version `
                -ExpectedSourceCommit $case.Source `
                -ExpectedSourceTreeId $sourceTreeId `
                -NowUtc $case.Now `
                -MaximumAgeHours 24 `
                -PublicCertificatePath $case.PublicCertificatePath 2>&1 | Out-String
            $probeExitCode = $LASTEXITCODE
            if ($case.ShouldPass -and $probeExitCode -ne 0) {
                throw "Release attestation validator rejected valid case '$($case.Name)': $probeOutput"
            }
            if (-not $case.ShouldPass -and $probeExitCode -eq 0) {
                throw "Release attestation validator accepted invalid case '$($case.Name)'."
            }
        }
    }
    finally {
        $cleanupThumbprints = @($trustedThumbprint, $wrongThumbprint) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        if ($cleanupThumbprints.Count -gt 0) {
            $cleanupScript = "`$ErrorActionPreference = 'SilentlyContinue'; @('$($cleanupThumbprints -join "','")') | ForEach-Object { Remove-Item -LiteralPath ('Cert:\CurrentUser\My\\' + `$_) -Force }"
            & $windowsPowerShell -NoLogo -NoProfile -NonInteractive -Command $cleanupScript | Out-Null
        }
        foreach ($certificate in @($trustedCertificate, $wrongCertificate)) {
            if ($certificate) {
                $certificate.Dispose()
            }
        }
        Remove-Module DataGen.ReleasePreflightAttestation -Force -ErrorAction SilentlyContinue
    }

    $workflowPath = Join-Path $RepositoryRoot '.github\workflows\release-module.yml'
    $workflow = Get-Content -LiteralPath $workflowPath -Raw
    $pinnedPublicCertificatePath = Join-Path $RepositoryRoot 'release-trust\datagen-release-preflight-attestation.cer'
    if (-not (Test-Path -LiteralPath $pinnedPublicCertificatePath -PathType Leaf)) {
        throw 'Release publication attestation contract requires the pinned public certificate.'
    }
    $pinnedPublicCertificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($pinnedPublicCertificatePath)
    try {
        $pinnedRsa = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPublicKey($pinnedPublicCertificate)
        if (-not $pinnedRsa -or $pinnedPublicCertificate.HasPrivateKey) {
            throw 'Release publication attestation trust file must contain only an RSA public certificate.'
        }
        $pinnedRsa.Dispose()
    }
    finally {
        $pinnedPublicCertificate.Dispose()
    }
    if ($workflow -match '(?m)^  push:\s*$') {
        throw 'Release publication workflow must not run automatically for pushed tags.'
    }
    if ($workflow -notmatch '(?m)^  workflow_dispatch:\s*$') {
        throw 'Release publication workflow must be manually dispatched.'
    }
    $attestationInput = [regex]::Match(
        $workflow,
        '(?ms)^      publisher_metadata_attestation:\r?\n(?<input>.*?)(?=^      [a-zA-Z0-9_]+:|^env:)')
    if (-not $attestationInput.Success -or $attestationInput.Groups['input'].Value -notmatch '(?m)^        required:\s*true\s*$') {
        throw 'Release publication workflow requires a mandatory publisher_metadata_attestation input.'
    }
    $validationStep = [regex]::Match(
        $workflow,
        '(?ms)^      - name: Validate required workstation attestation\r?\n(?<step>.*?)(?=^      - name: )')
    if (-not $validationStep.Success) {
        throw 'Release publication workflow requires a workstation-attestation validation step.'
    }
    foreach ($mapping in @(
        'RELEASE_ATTESTATION: ${{ inputs.publisher_metadata_attestation }}',
        'RELEASE_VERSION: ${{ steps.version.outputs.value }}',
        'RELEASE_SOURCE_COMMIT: ${{ github.sha }}',
        'RELEASE_SOURCE_TREE: ${{ steps.version.outputs.source_tree }}',
        'RELEASE_TRUST_PUBLIC_CERTIFICATE: release-trust/datagen-release-preflight-attestation.cer'
    )) {
        if (-not $validationStep.Groups['step'].Value.Contains($mapping, [StringComparison]::Ordinal)) {
            throw "Release attestation validation step requires '$mapping'."
        }
    }
    if ($validationStep.Groups['step'].Value -notmatch '\.\\scripts\\assert-release-preflight-attestation\.ps1') {
        throw 'Release attestation validation step must invoke the shared validator.'
    }
    if ($validationStep.Groups['step'].Value -notmatch '-PublicCertificatePath\s+\$env:RELEASE_TRUST_PUBLIC_CERTIFICATE') {
        throw 'Release attestation validation step must verify against the tracked pinned public certificate.'
    }
    if ($validationStep.Groups['step'].Value -notmatch '-ExpectedSourceTreeId\s+\$env:RELEASE_SOURCE_TREE') {
        throw 'Release attestation validation step must bind the signed source tree to the checked-out committed tree.'
    }
    $generateStepIndex = $workflow.IndexOf('      - name: Generate seeded catalog artifact', [StringComparison]::Ordinal)
    $galleryStepIndex = $workflow.IndexOf('      - name: Create gallery package', [StringComparison]::Ordinal)
    $releaseStepIndex = $workflow.IndexOf('      - name: Publish GitHub release assets', [StringComparison]::Ordinal)
    if ($validationStep.Index -gt $generateStepIndex -or $validationStep.Index -gt $galleryStepIndex -or $validationStep.Index -gt $releaseStepIndex) {
        throw 'Release attestation must be validated before release artifacts are generated or published.'
    }
    if ($workflow -match "github\.event_name\s*==\s*'push'") {
        throw 'Release publication conditions must not retain an automatic push bypass.'
    }
}

function Assert-CiWorkflowPackageVersionContract {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot
    )

    $workflowPath = Join-Path $RepositoryRoot '.github\workflows\ci.yml'
    $workflow = Get-Content -LiteralPath $workflowPath -Raw

    $resolveStepMatch = [regex]::Match(
        $workflow,
        '(?ms)^      - name: Resolve package version\r?\n(?<step>.*?)(?=^      - name: )'
    )
    if (-not $resolveStepMatch.Success) {
        throw "CI workflow contract could not locate the 'Resolve package version' step in '$workflowPath'."
    }

    $resolveStep = $resolveStepMatch.Groups['step'].Value
    $runMatch = [regex]::Match($resolveStep, '(?ms)^        run: \|\r?\n(?<run>.*)$')
    if (-not $runMatch.Success) {
        throw "CI workflow contract could not locate the PowerShell run block in '$workflowPath'."
    }

    $runSource = $runMatch.Groups['run'].Value
    if ($runSource -match '\$\{\{') {
        throw 'CI workflow contract forbids direct GitHub expression interpolation in the package-version PowerShell run block.'
    }
    if ($resolveStep -match '(?i)(github\.run_number|GITHUB_RUN_NUMBER)') {
        throw 'CI workflow contract forbids a run-number source in the package-version resolution step.'
    }
    foreach ($requiredSourceMarker in @(
        "Get-Content -LiteralPath 'Directory.Build.props' -Raw",
        "SelectSingleNode('/Project/PropertyGroup/Version')",
        '$env:GITHUB_ENV',
        'DATAGEN_PACKAGE_VERSION='
    )) {
        if (-not $runSource.Contains($requiredSourceMarker, [StringComparison]::Ordinal)) {
            throw "CI workflow contract requires '$requiredSourceMarker' in the package-version run block."
        }
    }
    if ($runSource -notmatch '\$version\s+-notmatch\s+') {
        throw 'CI workflow contract requires numeric version validation before the package step.'
    }

    $resolveExecutableLines = @($runSource -split '\r?\n' |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ -and -not $_.StartsWith('#', [StringComparison]::Ordinal) })
    $versionAssignments = @($resolveExecutableLines | Where-Object { $_ -match '^\$version\s*=' })
    if ($versionAssignments.Count -ne 1 -or $versionAssignments[0] -notmatch '^\$version\s*=\s*\$versionNode\.InnerText\.Trim\(\)\s*$') {
        throw 'CI workflow contract requires a single authoritative version assignment derived from Directory.Build.props.'
    }
    if (-not ($resolveExecutableLines | Where-Object { $_ -match '^"DATAGEN_PACKAGE_VERSION=\$version"\s*\|\s*Out-File\s+-FilePath\s+\$env:GITHUB_ENV\b' })) {
        throw 'CI workflow contract requires DATAGEN_PACKAGE_VERSION to be exported directly from the authoritative version value.'
    }

    $packageStepMatch = [regex]::Match(
        $workflow,
        '(?ms)^      - name: Package PowerShell module\r?\n(?<step>.*?)(?=^      - name: )'
    )
    if (-not $packageStepMatch.Success) {
        throw "CI workflow contract could not locate the 'Package PowerShell module' step in '$workflowPath'."
    }

    $packageStep = $packageStepMatch.Groups['step'].Value
    if ($packageStep -match '\$\{\{') {
        throw 'CI workflow contract forbids direct GitHub expression interpolation in the package PowerShell run block.'
    }
    if ($packageStep -match '(?i)(github\.run_number|GITHUB_RUN_NUMBER)') {
        throw 'CI workflow contract forbids a run-number source in the package step.'
    }

    $packageScalarRunMatch = [regex]::Match($packageStep, '(?m)^        run:[ \t]+(?<run>[^|\r\n][^\r\n]*)\r?$')
    $packageBlockRunMatch = [regex]::Match($packageStep, '(?ms)^        run:[ \t]*\|[ \t]*\r?\n(?<run>.*)$')
    if ($packageScalarRunMatch.Success) {
        $packageRunSource = $packageScalarRunMatch.Groups['run'].Value
    }
    elseif ($packageBlockRunMatch.Success) {
        $packageRunSource = $packageBlockRunMatch.Groups['run'].Value
    }
    else {
        throw "CI workflow contract could not parse the package step run value in '$workflowPath'."
    }

    $packageExecutableLines = @($packageRunSource -split '\r?\n' |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ -and -not $_.StartsWith('#', [StringComparison]::Ordinal) })
    $directPackageCommands = @($packageExecutableLines | Where-Object {
        $_ -match '^\.\\scripts\\package-module\.ps1\s+-Version\s+\$env:DATAGEN_PACKAGE_VERSION\s+-Configuration\s+Release$'
    })
    if ($directPackageCommands.Count -ne 1) {
        throw 'CI workflow contract requires one direct executable package command using DATAGEN_PACKAGE_VERSION.'
    }

    $verifyStepMatch = [regex]::Match(
        $workflow,
        '(?ms)^      - name: Verify module package artifact\r?\n(?<step>.*?)(?=^      - name: )'
    )
    if (-not $verifyStepMatch.Success) {
        throw "CI workflow contract could not locate the 'Verify module package artifact' step in '$workflowPath'."
    }
    $verifyStep = $verifyStepMatch.Groups['step'].Value
    $verifyRunMatch = [regex]::Match($verifyStep, '(?ms)^        run: \|\r?\n(?<run>.*)$')
    if (-not $verifyRunMatch.Success) {
        throw "CI workflow contract could not locate the artifact-verification PowerShell run block in '$workflowPath'."
    }
    $verifyRunSource = $verifyRunMatch.Groups['run'].Value
    if ($verifyRunSource -match '\$\{\{') {
        throw 'CI workflow contract forbids direct GitHub expression interpolation in the artifact-verification PowerShell run block.'
    }
    if ($verifyRunSource -notmatch '(?m)^\s*\$packagePath\s*=\s*Join-Path\s+''artifacts/module''\s+"SyntheticEnterprise\.PowerShell-\$env:DATAGEN_PACKAGE_VERSION\.zip"\s*$') {
        throw 'CI workflow contract requires the expected package archive path to derive from DATAGEN_PACKAGE_VERSION.'
    }
    if ($verifyRunSource -notmatch '(?m)^\s*if\s*\(\s*-not\s*\(\s*Test-Path\s+-LiteralPath\s+\$packagePath\s+-PathType\s+Leaf\s*\)\s*\)\s*\{\s*$') {
        throw 'CI workflow contract requires an explicit package-file existence check before upload.'
    }

    $uploadStepMatch = [regex]::Match(
        $workflow,
        '(?ms)^      - name: Upload module package artifact\r?\n(?<step>.*?)(?=^      - name: )'
    )
    if (-not $uploadStepMatch.Success) {
        throw "CI workflow contract could not locate the 'Upload module package artifact' step in '$workflowPath'."
    }
    if ($uploadStepMatch.Groups['step'].Value -notmatch '(?m)^          if-no-files-found:\s*error\s*$') {
        throw 'CI workflow contract requires module artifact upload to fail when no files are found.'
    }

    if (-not ($resolveStepMatch.Index -lt $packageStepMatch.Index -and
        $packageStepMatch.Index -lt $verifyStepMatch.Index -and
        $verifyStepMatch.Index -lt $uploadStepMatch.Index)) {
        throw 'CI workflow contract requires version resolution, packaging, file verification, and upload in that order.'
    }
}

function Assert-CiWorkflowPackageVersionMutationContract {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot,

        [Parameter(Mandatory)]
        [string]$ContractOutputRoot
    )

    $workflowPath = Join-Path $RepositoryRoot '.github\workflows\ci.yml'
    $workflow = Get-Content -LiteralPath $workflowPath -Raw
    $newline = if ($workflow.Contains("`r`n", [StringComparison]::Ordinal)) { "`r`n" } else { "`n" }

    $resolveStepAnchor = "      - name: Resolve package version${newline}        shell: pwsh${newline}"
    $derivedVersionAnchor = '          $version = $versionNode.InnerText.Trim()' + $newline
    $packageCommandAnchor = '        run: .\scripts\package-module.ps1 -Version $env:DATAGEN_PACKAGE_VERSION -Configuration Release'
    foreach ($anchor in @($resolveStepAnchor, $derivedVersionAnchor, $packageCommandAnchor)) {
        if (-not $workflow.Contains($anchor, [StringComparison]::Ordinal)) {
            throw "CI workflow mutation contract could not locate fixture anchor '$anchor'."
        }
    }

    $runNumberEnvironment = @(
        '        env:',
        '          CI_RUN_NUMBER: ${{ github.run_number }}'
    ) -join $newline
    $runNumberWorkflow = $workflow.Replace(
        $resolveStepAnchor,
        $resolveStepAnchor + $runNumberEnvironment + $newline,
        [StringComparison]::Ordinal
    ).Replace(
        $derivedVersionAnchor,
        $derivedVersionAnchor + '          $version = "0.1.$env:CI_RUN_NUMBER"' + $newline,
        [StringComparison]::Ordinal
    )

    $literalOverrideWorkflow = $workflow.Replace(
        $derivedVersionAnchor,
        $derivedVersionAnchor + "          `$version = '9.9.9'" + $newline,
        [StringComparison]::Ordinal
    )

    $commentedCommandWorkflow = $workflow.Replace(
        $packageCommandAnchor,
        @(
            '        run: |',
            '          # .\scripts\package-module.ps1 -Version $env:DATAGEN_PACKAGE_VERSION -Configuration Release',
            "          Write-Host 'package deliberately skipped'"
        ) -join $newline,
        [StringComparison]::Ordinal
    )

    $mutations = @(
        [pscustomobject]@{
            Name = 'synthetic-run-number-version'
            Workflow = $runNumberWorkflow
            ExpectedRejection = '*run-number*'
        },
        [pscustomobject]@{
            Name = 'hard-coded-literal-version-override'
            Workflow = $literalOverrideWorkflow
            ExpectedRejection = '*single authoritative version assignment*'
        },
        [pscustomobject]@{
            Name = 'commented-package-command'
            Workflow = $commentedCommandWorkflow
            ExpectedRejection = '*direct executable package command*'
        }
    )

    $mutationRoot = Join-Path $ContractOutputRoot 'ci-workflow-version-mutations'
    $failures = [Collections.Generic.List[string]]::new()
    foreach ($mutation in $mutations) {
        $probeRoot = Join-Path $mutationRoot $mutation.Name
        $probeWorkflowRoot = Join-Path $probeRoot '.github\workflows'
        New-Item -ItemType Directory -Path $probeWorkflowRoot -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $probeWorkflowRoot 'ci.yml') -Value $mutation.Workflow -Encoding utf8 -NoNewline

        try {
            Assert-CiWorkflowPackageVersionContract -RepositoryRoot $probeRoot
            $failures.Add("CI workflow contract accepted prohibited mutation '$($mutation.Name)'.")
        }
        catch {
            if ($_.Exception.Message -notlike $mutation.ExpectedRejection) {
                $failures.Add("CI workflow mutation '$($mutation.Name)' was rejected for the wrong reason: $($_.Exception.Message)")
            }
        }
    }

    if ($failures.Count -gt 0) {
        throw ($failures -join [Environment]::NewLine)
    }
}

function Assert-ReleaseVersionContract {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot
    )

    $expectedVersion = '0.11.0'
    $expectedAssemblyVersion = '0.11.0.0'
    $propsPath = Join-Path $RepositoryRoot 'Directory.Build.props'
    $packageScriptPath = Join-Path $RepositoryRoot 'scripts\package-module.ps1'
    $websitePackagePath = Join-Path $RepositoryRoot 'website\package.json'
    $websiteLockPath = Join-Path $RepositoryRoot 'website\package-lock.json'

    $props = Get-Content -LiteralPath $propsPath -Raw
    foreach ($marker in @(
        "<Version>$expectedVersion</Version>",
        "<AssemblyVersion>$expectedAssemblyVersion</AssemblyVersion>",
        "<FileVersion>$expectedAssemblyVersion</FileVersion>",
        "<InformationalVersion>$expectedVersion</InformationalVersion>"
    )) {
        if ($props -notlike "*$marker*") {
            throw "Release version contract requires '$marker' in '$propsPath'."
        }
    }

    $packageScript = Get-Content -LiteralPath $packageScriptPath -Raw
    $expectedPackageDefault = "[string]`$Version = '$expectedVersion'"
    if (-not $packageScript.Contains($expectedPackageDefault, [StringComparison]::Ordinal)) {
        throw "Release version contract requires package-module.ps1 to default to '$expectedVersion'."
    }

    $websitePackage = Get-Content -LiteralPath $websitePackagePath -Raw | ConvertFrom-Json
    if ($websitePackage.version -ne $expectedVersion) {
        throw "Release version contract requires website/package.json version '$expectedVersion', found '$($websitePackage.version)'."
    }

    $websiteLock = Get-Content -LiteralPath $websiteLockPath -Raw | ConvertFrom-Json -AsHashtable
    if ($websiteLock['version'] -ne $expectedVersion -or $websiteLock['packages']['']['version'] -ne $expectedVersion) {
        throw "Release version contract requires website/package-lock.json root versions '$expectedVersion'."
    }

    Assert-ReleaseWorkflowVersionContract -RepositoryRoot $RepositoryRoot -ContractOutputRoot $outputRoot
    Assert-ReleasePublicationAttestationContract -RepositoryRoot $RepositoryRoot -ContractOutputRoot $outputRoot
    Assert-CiWorkflowPackageVersionContract -RepositoryRoot $RepositoryRoot
    Assert-CiWorkflowPackageVersionMutationContract -RepositoryRoot $RepositoryRoot -ContractOutputRoot $outputRoot
}

function Assert-NoDebugSymbolArtifacts {
    param(
        [Parameter(Mandatory)]
        [string]$PackageRoot,

        [Parameter(Mandatory)]
        [string]$Label
    )

    $debugSymbols = Get-ChildItem -LiteralPath $PackageRoot -File -Recurse |
        Where-Object { $_.Extension -in @('.pdb', '.mdb', '.dbg') }
    if ($debugSymbols) {
        throw "$Label contains debug-symbol artifacts: $($debugSymbols.FullName -join ', ')."
    }
}

function Assert-NoAbsoluteLocalPathsInSyntheticEnterpriseAssemblies {
    param(
        [Parameter(Mandatory)]
        [string]$PackageRoot,

        [Parameter(Mandatory)]
        [string]$Label
    )

    foreach ($assembly in Get-ChildItem -LiteralPath $PackageRoot -File -Filter 'SyntheticEnterprise.*.dll' -Recurse) {
        $contents = [Text.Encoding]::Latin1.GetString([IO.File]::ReadAllBytes($assembly.FullName))
        if ($contents -match '(?i)(?<![A-Za-z0-9])[A-Z]:[\\/]') {
            throw "$Label assembly '$($assembly.FullName)' embeds an absolute local path."
        }
    }
}

function Assert-SyntheticEnterpriseAssemblyVersions {
    param(
        [Parameter(Mandatory)]
        [string]$PackageRoot,

        [Parameter(Mandatory)]
        [string]$ExpectedAssemblyVersion,

        [Parameter(Mandatory)]
        [string]$Label
    )

    $requiredAssemblyNames = @(
        'SyntheticEnterprise.Contracts.dll',
        'SyntheticEnterprise.Core.dll',
        'SyntheticEnterprise.Exporting.dll',
        'SyntheticEnterprise.PluginHost.dll',
        'SyntheticEnterprise.PowerShell.dll'
    )
    $shippedAssemblies = @(Get-ChildItem -LiteralPath $PackageRoot -File -Filter 'SyntheticEnterprise.*.dll' -Recurse)
    $shippedAssemblyNames = @($shippedAssemblies | ForEach-Object Name)
    foreach ($requiredAssemblyName in $requiredAssemblyNames) {
        if ($shippedAssemblyNames -notcontains $requiredAssemblyName) {
            throw "$Label is missing required assembly '$requiredAssemblyName'."
        }
    }

    foreach ($assembly in $shippedAssemblies) {
        $assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($assembly.FullName).Version.ToString()
        if ($assemblyVersion -ne $ExpectedAssemblyVersion) {
            throw "$Label assembly '$($assembly.Name)' has version '$assemblyVersion', expected '$ExpectedAssemblyVersion'."
        }
    }
}

function Assert-ModuleManifestParity {
    param(
        [Parameter(Mandatory)]
        [hashtable]$ExpectedManifest,

        [Parameter(Mandatory)]
        [hashtable]$ActualManifest,

        [Parameter(Mandatory)]
        [string]$Label
    )

    foreach ($propertyName in @('ModuleVersion', 'RootModule', 'Guid', 'PowerShellVersion')) {
        if ([string]$ActualManifest[$propertyName] -ne [string]$ExpectedManifest[$propertyName]) {
            throw "$Label manifest property '$propertyName' differs from versioned staging."
        }
    }

    $expectedCmdlets = @($ExpectedManifest['CmdletsToExport'] | ForEach-Object { [string]$_ })
    $actualCmdlets = @($ActualManifest['CmdletsToExport'] | ForEach-Object { [string]$_ })
    if (($actualCmdlets -join "`n") -ne ($expectedCmdlets -join "`n")) {
        throw "$Label manifest CmdletsToExport differs from versioned staging."
    }
}

function Assert-ReleaseArtifactContract {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot,

        [Parameter(Mandatory)]
        [string]$ContractOutputRoot
    )

    [xml]$props = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'Directory.Build.props') -Raw
    $expectedVersion = $props.SelectSingleNode('/Project/PropertyGroup/Version').InnerText.Trim()
    $expectedAssemblyVersion = $props.SelectSingleNode('/Project/PropertyGroup/AssemblyVersion').InnerText.Trim()
    $packageScriptPath = Join-Path $RepositoryRoot 'scripts\package-module.ps1'
    $mismatchOutputRoot = Join-Path $ContractOutputRoot 'mismatch-rejection'
    $packageOutputRoot = Join-Path $ContractOutputRoot 'module'

    $mismatchOutput = & "$PSHOME\pwsh.exe" -NoLogo -NoProfile -NonInteractive -File $packageScriptPath `
        -Version '999.999.999' -OutputRoot $mismatchOutputRoot 2>&1 | Out-String
    $mismatchExitCode = $LASTEXITCODE
    if ($mismatchExitCode -eq 0 -or $mismatchOutput -notlike '*does not match authoritative Directory.Build.props Version*') {
        throw "package-module.ps1 accepted a mismatched requested version. Output: $mismatchOutput"
    }

    Set-Alias -Name dotnet -Value $DotNetPath -Scope Local
    & $packageScriptPath -Version $expectedVersion -Configuration Release -OutputRoot $packageOutputRoot

    $moduleName = 'SyntheticEnterprise.PowerShell'
    $versionedStagePath = Join-Path $packageOutputRoot "$moduleName\$expectedVersion"
    $publishStagePath = Join-Path $packageOutputRoot "publish\$moduleName"
    $zipPath = Join-Path $packageOutputRoot "$moduleName-$expectedVersion.zip"
    $buildPluginHostPath = Join-Path $packageOutputRoot 'build\bin\SyntheticEnterprise.PluginHost\release\SyntheticEnterprise.PluginHost.dll'
    $versionedPluginHostPath = Join-Path $versionedStagePath 'SyntheticEnterprise.PluginHost.dll'
    $publishedPluginHostPath = Join-Path $publishStagePath 'SyntheticEnterprise.PluginHost.dll'
    $versionedManifestPath = Join-Path $versionedStagePath "$moduleName.psd1"
    $publishedManifestPath = Join-Path $publishStagePath "$moduleName.psd1"
    $packagedCatalogPath = Join-Path $publishStagePath 'catalogs\catalogs.sqlite'

    foreach ($requiredPath in @($versionedStagePath, $publishStagePath, $zipPath, $buildPluginHostPath, $versionedPluginHostPath, $publishedPluginHostPath, $versionedManifestPath, $publishedManifestPath, $packagedCatalogPath)) {
        if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf) -and -not (Test-Path -LiteralPath $requiredPath -PathType Container)) {
            throw "Release artifact contract expected '$requiredPath'."
        }
    }

    $isolatedPluginHostHash = (Get-FileHash -LiteralPath $buildPluginHostPath -Algorithm SHA256).Hash
    foreach ($pluginHostPath in @($versionedPluginHostPath, $publishedPluginHostPath)) {
        if ((Get-FileHash -LiteralPath $pluginHostPath -Algorithm SHA256).Hash -ne $isolatedPluginHostHash) {
            throw "Packaged PluginHost '$pluginHostPath' does not match the current isolated build output."
        }
    }

    Assert-SyntheticEnterpriseAssemblyVersions -PackageRoot $versionedStagePath -ExpectedAssemblyVersion $expectedAssemblyVersion -Label 'Versioned module staging'
    Assert-SyntheticEnterpriseAssemblyVersions -PackageRoot $publishStagePath -ExpectedAssemblyVersion $expectedAssemblyVersion -Label 'Gallery publish staging'
    Assert-NoDebugSymbolArtifacts -PackageRoot $versionedStagePath -Label 'Versioned module staging'
    Assert-NoDebugSymbolArtifacts -PackageRoot $publishStagePath -Label 'Gallery publish staging'
    Assert-NoAbsoluteLocalPathsInSyntheticEnterpriseAssemblies -PackageRoot $versionedStagePath -Label 'Versioned module staging'
    Assert-NoAbsoluteLocalPathsInSyntheticEnterpriseAssemblies -PackageRoot $publishStagePath -Label 'Gallery publish staging'

    $versionedManifest = Import-PowerShellDataFile -LiteralPath $versionedManifestPath
    $publishedManifest = Import-PowerShellDataFile -LiteralPath $publishedManifestPath
    if ([string]$versionedManifest['ModuleVersion'] -ne $expectedVersion -or [string]$versionedManifest['RootModule'] -ne "$moduleName.dll" -or @($versionedManifest['CmdletsToExport']).Count -eq 0) {
        throw 'Versioned module manifest does not describe the expected functional module surface.'
    }
    Assert-ModuleManifestParity -ExpectedManifest $versionedManifest -ActualManifest $publishedManifest -Label 'Gallery publish staging'

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $archiveEntries = @($archive.Entries)
        $archiveNames = @($archiveEntries | ForEach-Object FullName)
        $duplicateNames = @($archiveNames | Group-Object { $_.ToLowerInvariant() } | Where-Object Count -gt 1)
        if ($duplicateNames) {
            throw "Release archive contains duplicate paths: $($duplicateNames.Name -join ', ')."
        }

        foreach ($archiveName in $archiveNames) {
            if ($archiveName.StartsWith('/') -or $archiveName -match '(^|/)\.\.(/|$)' -or -not $archiveName.StartsWith("$expectedVersion/")) {
                throw "Release archive contains unsafe path '$archiveName'."
            }
            if ($archiveName -match '(?i)\.(pdb|mdb|dbg)$') {
                throw "Release archive contains debug-symbol artifact '$archiveName'."
            }
        }
    }
    finally {
        $archive.Dispose()
    }

    $archiveExtractionRoot = Join-Path $ContractOutputRoot 'archive'
    Expand-Archive -LiteralPath $zipPath -DestinationPath $archiveExtractionRoot -Force
    $archiveModulePath = Join-Path $archiveExtractionRoot $expectedVersion
    $archivePluginHostPath = Join-Path $archiveModulePath 'SyntheticEnterprise.PluginHost.dll'
    $archiveManifestPath = Join-Path $archiveModulePath "$moduleName.psd1"
    if (-not (Test-Path -LiteralPath $archivePluginHostPath -PathType Leaf)) {
        throw 'Release archive is missing SyntheticEnterprise.PluginHost.dll.'
    }
    if ((Get-FileHash -LiteralPath $archivePluginHostPath -Algorithm SHA256).Hash -ne $isolatedPluginHostHash) {
        throw 'Release archive PluginHost does not match the current isolated build output.'
    }

    Assert-SyntheticEnterpriseAssemblyVersions -PackageRoot $archiveModulePath -ExpectedAssemblyVersion $expectedAssemblyVersion -Label 'Release archive'
    Assert-NoDebugSymbolArtifacts -PackageRoot $archiveModulePath -Label 'Release archive'
    Assert-NoAbsoluteLocalPathsInSyntheticEnterpriseAssemblies -PackageRoot $archiveModulePath -Label 'Release archive'
    $archiveManifest = Import-PowerShellDataFile -LiteralPath $archiveManifestPath
    Assert-ModuleManifestParity -ExpectedManifest $versionedManifest -ActualManifest $archiveManifest -Label 'Release archive'
}

if ($ReleaseWorkflowContractOnly.IsPresent) {
    Assert-ReleaseWorkflowVersionContract -RepositoryRoot $sourceRoot -ContractOutputRoot $outputRoot
    Assert-ReleasePublicationAttestationContract -RepositoryRoot $sourceRoot -ContractOutputRoot $outputRoot
    Write-Host 'Release workflow and attestation contract passed.' -ForegroundColor Green
    exit 0
}

Assert-ReleaseVersionContract -RepositoryRoot $sourceRoot
if ($ReleaseVersionContractOnly.IsPresent) {
    Write-Host 'Release version contract passed.' -ForegroundColor Green
    exit 0
}

if ($ReleaseArtifactContractOnly.IsPresent) {
    Assert-ReleaseArtifactContract -RepositoryRoot $sourceRoot -ContractOutputRoot $outputRoot
    Write-Host 'Release artifact contract passed.' -ForegroundColor Green
    exit 0
}

$snapshotRoot = Join-Path $outputRoot 'source'
$logsRoot = Join-Path $outputRoot 'logs'
$catalogArtifactPath = Join-Path $outputRoot 'catalog\catalogs.sqlite'
$catalogSourceMutationPath = Join-Path $snapshotRoot 'catalogs\company_suffixes.csv'

if (Test-Path -LiteralPath $outputRoot) {
    Remove-Item -LiteralPath $outputRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $outputRoot, $logsRoot -Force | Out-Null

function Invoke-LoggedCommand {
    param(
        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter(Mandatory)]
        [scriptblock]$Command
    )

    $logPath = Join-Path $logsRoot "$Name.log"
    & $Command *>&1 | Tee-Object -FilePath $logPath
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE. See '$logPath'."
    }
}

function Assert-UnsafeOutputRootIsRejected {
    param(
        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter(Mandatory)]
        [string]$ValidationSourceRoot,

        [Parameter(Mandatory)]
        [string]$UnsafeOutputRoot,

        [Parameter(Mandatory)]
        [string[]]$SentinelPaths
    )

    $commandOutput = & "$PSHOME\pwsh.exe" -NoLogo -NoProfile -NonInteractive -File $PSCommandPath `
        -SourceRoot $ValidationSourceRoot `
        -OutputRoot $UnsafeOutputRoot 2>&1 | Out-String
    $commandExitCode = $LASTEXITCODE
    $global:LASTEXITCODE = 0

    if ($commandExitCode -eq 0) {
        throw "Cleanup safety case '$Name' unexpectedly completed."
    }

    if ($commandOutput -notmatch 'Unsafe OutputRoot') {
        throw "Cleanup safety case '$Name' did not reject the path before attempting work. Output: $commandOutput"
    }

    foreach ($sentinelPath in $SentinelPaths) {
        if (-not (Test-Path -LiteralPath $sentinelPath)) {
            throw "Cleanup safety case '$Name' removed '$sentinelPath'."
        }
    }
}

function Assert-UnsafePackageOutputRootIsRejected {
    param(
        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter(Mandatory)]
        [string]$UnsafeOutputRoot,

        [Parameter(Mandatory)]
        [string[]]$SentinelPaths
    )

    $packageScriptPath = Join-Path $snapshotRoot 'scripts\package-module.ps1'
    $commandOutput = & "$PSHOME\pwsh.exe" -NoLogo -NoProfile -NonInteractive -File $packageScriptPath `
        -ProjectPath 'missing\cleanup-safety-probe.csproj' `
        -OutputRoot $UnsafeOutputRoot 2>&1 | Out-String
    $commandExitCode = $LASTEXITCODE
    $global:LASTEXITCODE = 0

    if ($commandExitCode -eq 0) {
        throw "Package cleanup safety case '$Name' unexpectedly completed."
    }

    if ($commandOutput -notmatch 'Unsafe package OutputRoot') {
        throw "Package cleanup safety case '$Name' was not rejected before project/build work. Output: $commandOutput"
    }

    foreach ($sentinelPath in $SentinelPaths) {
        if (-not (Test-Path -LiteralPath $sentinelPath)) {
            throw "Package cleanup safety case '$Name' removed '$sentinelPath'."
        }
    }
}

function Invoke-PackageCleanupSafetyRegression {
    $packageSafetyRoot = Join-Path $outputRoot 'package-cleanup-safety'
    New-Item -ItemType Directory -Path $packageSafetyRoot -Force | Out-Null

    $availableDriveLetter = @('W', 'V', 'U', 'T', 'S') |
        Where-Object { -not (Test-Path -LiteralPath "$($_):\") } |
        Select-Object -First 1
    if (-not $availableDriveLetter) {
        throw 'No drive letter is available for the package filesystem-root safety probe.'
    }

    $substTargetRoot = Join-Path $packageSafetyRoot 'subst-root'
    $substSentinelPath = Join-Path $substTargetRoot 'filesystem-root-sentinel.txt'
    New-Item -ItemType Directory -Path $substTargetRoot -Force | Out-Null
    Set-Content -LiteralPath $substSentinelPath -Value 'filesystem root must survive' -NoNewline
    & subst.exe "$availableDriveLetter`:" $substTargetRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to create temporary subst drive '$availableDriveLetter`:'."
    }

    try {
        Assert-UnsafePackageOutputRootIsRejected `
            -Name 'filesystem-root' `
            -UnsafeOutputRoot "$availableDriveLetter`:\" `
            -SentinelPaths @($substSentinelPath)
    }
    finally {
        & subst.exe "$availableDriveLetter`:" /D
        $global:LASTEXITCODE = 0
    }

    $sourceRootSentinelPath = Join-Path $snapshotRoot 'package-source-root-sentinel.txt'
    Set-Content -LiteralPath $sourceRootSentinelPath -Value 'source root must survive package validation' -NoNewline
    Assert-UnsafePackageOutputRootIsRejected `
        -Name 'source-root' `
        -UnsafeOutputRoot $snapshotRoot `
        -SentinelPaths @($sourceRootSentinelPath)

    $sourceAncestorSentinelPath = Join-Path $outputRoot 'package-source-ancestor-sentinel.txt'
    Set-Content -LiteralPath $sourceAncestorSentinelPath -Value 'source ancestor must survive package validation' -NoNewline
    Assert-UnsafePackageOutputRootIsRejected `
        -Name 'source-ancestor' `
        -UnsafeOutputRoot $outputRoot `
        -SentinelPaths @($sourceAncestorSentinelPath, $snapshotRoot)

    $sourceDescendantRoot = Join-Path $snapshotRoot 'artifacts\module'
    $sourceDescendantSentinelPath = Join-Path $sourceDescendantRoot 'source-descendant-sentinel.txt'
    New-Item -ItemType Directory -Path $sourceDescendantRoot -Force | Out-Null
    Set-Content -LiteralPath $sourceDescendantSentinelPath -Value 'source descendant must survive package validation' -NoNewline
    Assert-UnsafePackageOutputRootIsRejected `
        -Name 'source-descendant' `
        -UnsafeOutputRoot $sourceDescendantRoot `
        -SentinelPaths @($sourceDescendantSentinelPath)

    $junctionSafetyRoot = Join-Path $packageSafetyRoot 'junction'
    $junctionTargetRoot = Join-Path $junctionSafetyRoot 'target'
    $junctionOutputRoot = Join-Path $junctionSafetyRoot 'output-link'
    $junctionSentinelPath = Join-Path $junctionTargetRoot 'junction-target-sentinel.txt'
    New-Item -ItemType Directory -Path $junctionTargetRoot -Force | Out-Null
    Set-Content -LiteralPath $junctionSentinelPath -Value 'junction target must survive package validation' -NoNewline
    New-Item -ItemType Junction -Path $junctionOutputRoot -Target $junctionTargetRoot | Out-Null
    Assert-UnsafePackageOutputRootIsRejected `
        -Name 'junction' `
        -UnsafeOutputRoot $junctionOutputRoot `
        -SentinelPaths @($junctionOutputRoot, $junctionSentinelPath)
}

function Invoke-ConcurrentCatalogBuildRegression {
    $concurrentRoot = Join-Path $outputRoot 'concurrent-distinct-artifacts'
    $cases = @(
        [pscustomobject]@{
            Name = 'core'
            ProjectPath = Join-Path $snapshotRoot 'tests\SyntheticEnterprise.Core.Tests\SyntheticEnterprise.Core.Tests.csproj'
            ArtifactsPath = Join-Path $concurrentRoot 'core\artifacts'
            CatalogPath = Join-Path $concurrentRoot 'core\catalog\catalogs.sqlite'
        },
        [pscustomobject]@{
            Name = 'integration'
            ProjectPath = Join-Path $snapshotRoot 'tests\SyntheticEnterprise.Integration.Tests\SyntheticEnterprise.Integration.Tests.csproj'
            ArtifactsPath = Join-Path $concurrentRoot 'integration\artifacts'
            CatalogPath = Join-Path $concurrentRoot 'integration\catalog\catalogs.sqlite'
        }
    )

    $jobs = foreach ($case in $cases) {
        Start-Job -Name $case.Name -ScriptBlock {
            param($ProjectPath, $ArtifactsPath, $CatalogPath)

            $commandOutput = dotnet build $ProjectPath -c Release -v minimal -m:8 `
                "/p:ArtifactsPath=$ArtifactsPath" `
                '/p:UseArtifactsOutput=true' `
                "/p:DataGenCatalogArtifactPath=$CatalogPath" *>&1

            [pscustomobject]@{
                ExitCode = $LASTEXITCODE
                Output = $commandOutput | Out-String
            }
        } -ArgumentList $case.ProjectPath, $case.ArtifactsPath, $case.CatalogPath
    }

    try {
        Wait-Job -Job $jobs | Out-Null
        $failures = @()
        foreach ($case in $cases) {
            $job = $jobs | Where-Object Name -eq $case.Name
            $result = Receive-Job -Job $job
            $logPath = Join-Path $logsRoot "concurrent-$($case.Name).log"
            [IO.File]::WriteAllText($logPath, $result.Output, [Text.UTF8Encoding]::new($false))

            if ($result.ExitCode -ne 0) {
                $failures += "$($case.Name) exited with code $($result.ExitCode); see '$logPath'"
            }

            if (-not (Test-Path -LiteralPath $case.CatalogPath -PathType Leaf)) {
                $failures += "$($case.Name) did not produce '$($case.CatalogPath)'"
            }

            $fingerprintPath = "$($case.CatalogPath).inputs.sha256"
            if (-not (Test-Path -LiteralPath $fingerprintPath -PathType Leaf)) {
                $failures += "$($case.Name) did not produce '$fingerprintPath'"
            }
        }

        if ($failures.Count -gt 0) {
            throw "Concurrent distinct-artifact catalog builds failed: $($failures -join '; ')"
        }
    }
    finally {
        Remove-Job -Job $jobs -Force -ErrorAction SilentlyContinue
    }
}

function Invoke-AdversarialReleaseRemediationRegression {
    $regressionRoot = Join-Path $outputRoot 'adversarial'
    $catalogRoot = Join-Path $regressionRoot 'catalog-inputs'
    $projectPath = Join-Path $sourceRoot 'tests\SyntheticEnterprise.Core.Tests\SyntheticEnterprise.Core.Tests.csproj'
    New-Item -ItemType Directory -Path $catalogRoot -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $catalogRoot 'catalog-import-manifest.json') -Value @'
{
  "version": "v094-adversarial",
  "tables": [
    { "tableName": "software_catalog", "strategy": "copy_csv", "sourceFiles": [ "software_catalog.csv" ] }
  ]
}
'@ -NoNewline
    Set-Content -LiteralPath (Join-Path $catalogRoot 'software_catalog.csv') -Value @'
Name,Category,Vendor,Version
Adversarial Catalog,Security,Contoso,1.0
'@ -NoNewline

    function Invoke-DirectCatalogBuild {
        param(
            [Parameter(Mandatory)]
            [string]$Name,

            [Parameter(Mandatory)]
            [string]$ArtifactPath,

            [Parameter(Mandatory)]
            [string]$BuildRoot,

            [string]$CatalogRoot = $catalogRoot,

            [string]$PowerShellExecutable,

            [switch]$AllowFailure
        )

        $arguments = @(
            'build', $projectPath,
            '-c', 'Release', '-v', 'minimal', '-m:1', '--no-restore',
            "/p:BaseOutputPath=$BuildRoot\",
            "/p:DataGenCatalogArtifactPath=$ArtifactPath",
            "/p:DataGenCatalogRoot=$CatalogRoot"
        )
        if ($PowerShellExecutable) {
            $arguments += "/p:DataGenPowerShellExecutable=$PowerShellExecutable"
        }

        $commandOutput = dotnet @arguments 2>&1 | Out-String
        $commandExitCode = $LASTEXITCODE
        [IO.File]::WriteAllText((Join-Path $logsRoot "$Name.log"), $commandOutput, [Text.UTF8Encoding]::new($false))
        if (-not $AllowFailure.IsPresent -and $commandExitCode -ne 0) {
            throw "$Name failed with exit code $commandExitCode. See '$logsRoot\\$Name.log'."
        }

        return [pscustomobject]@{
            ExitCode = $commandExitCode
            Output = $commandOutput
        }
    }

    $reproSourceOne = Join-Path $regressionRoot 'repro\source-one'
    $reproSourceTwo = Join-Path $regressionRoot 'repro\source-two'
    Copy-Item -LiteralPath $catalogRoot -Destination $reproSourceOne -Recurse
    Copy-Item -LiteralPath $catalogRoot -Destination $reproSourceTwo -Recurse
    $reproFirst = Join-Path $regressionRoot 'repro\first\catalogs.sqlite'
    $reproSecond = Join-Path $regressionRoot 'repro\second\catalogs.sqlite'
    Invoke-DirectCatalogBuild -Name 'adversarial-repro-first' -ArtifactPath $reproFirst -BuildRoot (Join-Path $regressionRoot 'repro\build-first') -CatalogRoot $reproSourceOne | Out-Null
    Invoke-DirectCatalogBuild -Name 'adversarial-repro-second' -ArtifactPath $reproSecond -BuildRoot (Join-Path $regressionRoot 'repro\build-second') -CatalogRoot $reproSourceTwo | Out-Null
    if ((Get-FileHash -LiteralPath $reproFirst -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $reproSecond -Algorithm SHA256).Hash) {
        throw 'Independent catalog builds from copied source roots did not produce byte-identical artifacts.'
    }

    $tamperArtifact = Join-Path $regressionRoot 'tamper\catalogs.sqlite'
    Invoke-DirectCatalogBuild -Name 'adversarial-tamper-initial' -ArtifactPath $tamperArtifact -BuildRoot (Join-Path $regressionRoot 'tamper\build') | Out-Null
    $originalTamperHash = (Get-FileHash -LiteralPath $tamperArtifact -Algorithm SHA256).Hash
    $originalTamperTimestamp = (Get-Item -LiteralPath $tamperArtifact).LastWriteTimeUtc
    [IO.File]::AppendAllText($tamperArtifact, 'tamper')
    [IO.File]::SetLastWriteTimeUtc($tamperArtifact, $originalTamperTimestamp)
    $tamperedHash = (Get-FileHash -LiteralPath $tamperArtifact -Algorithm SHA256).Hash
    Invoke-DirectCatalogBuild -Name 'adversarial-tamper-regenerate' -ArtifactPath $tamperArtifact -BuildRoot (Join-Path $regressionRoot 'tamper\build') | Out-Null
    $restoredTamperHash = (Get-FileHash -LiteralPath $tamperArtifact -Algorithm SHA256).Hash
    $integrityRecord = [xml](Get-Content -LiteralPath "$tamperArtifact.inputs.sha256" -Raw)
    if ($restoredTamperHash -ne $originalTamperHash -or $restoredTamperHash -eq $tamperedHash -or $integrityRecord.catalogIntegrity.artifactSha256 -ne $restoredTamperHash -or [string]::IsNullOrWhiteSpace($integrityRecord.catalogIntegrity.inputFingerprint)) {
        throw 'Catalog tampering with a preserved timestamp was not detected and repaired by the integrity receipt.'
    }

    $receiptPath = "$tamperArtifact.inputs.sha256"
    $receiptInputHash = $integrityRecord.catalogIntegrity.inputFingerprint
    $receiptArtifactHash = $integrityRecord.catalogIntegrity.artifactSha256
    $internalDtdReceipt = @"
<!DOCTYPE catalogIntegrity [
  <!ENTITY input '$receiptInputHash'>
  <!ENTITY artifact '$receiptArtifactHash'>
]>
<catalogIntegrity formatVersion="1" inputFingerprint="&input;" artifactSha256="&artifact;" />
"@
    Set-Content -LiteralPath $receiptPath -Value $internalDtdReceipt -NoNewline
    Invoke-DirectCatalogBuild -Name 'adversarial-internal-dtd-repair' -ArtifactPath $tamperArtifact -BuildRoot (Join-Path $regressionRoot 'tamper\build') | Out-Null
    if ((Get-Content -LiteralPath $receiptPath -Raw) -match '<!DOCTYPE') {
        throw 'A receipt with internal DTD entities was accepted instead of being repaired.'
    }

    $externalDtdPath = Join-Path $regressionRoot 'tamper\receipt.dtd'
    Set-Content -LiteralPath $externalDtdPath -Value '<!ELEMENT catalogIntegrity EMPTY>' -NoNewline
    $externalDtdUri = ([Uri]$externalDtdPath).AbsoluteUri
    $externalDtdReceipt = "<!DOCTYPE catalogIntegrity SYSTEM `"$externalDtdUri`"><catalogIntegrity formatVersion=`"1`" inputFingerprint=`"$receiptInputHash`" artifactSha256=`"$receiptArtifactHash`" />"
    Set-Content -LiteralPath $receiptPath -Value $externalDtdReceipt -NoNewline
    Invoke-DirectCatalogBuild -Name 'adversarial-external-dtd-repair' -ArtifactPath $tamperArtifact -BuildRoot (Join-Path $regressionRoot 'tamper\build') | Out-Null
    if ((Get-Content -LiteralPath $receiptPath -Raw) -match '<!DOCTYPE') {
        throw 'A receipt with an external DTD was accepted instead of being repaired.'
    }

    $arbitrarySourceRoot = Join-Path $sourceRoot ("src\_v094-contract-fingerprint-" + [Guid]::NewGuid().ToString('N'))
    $arbitrarySourcePath = Join-Path $arbitrarySourceRoot 'obj\arbitrary-source.cs'
    $fingerprintArtifact = Join-Path $regressionRoot 'fingerprint\catalogs.sqlite'
    try {
        Invoke-DirectCatalogBuild -Name 'adversarial-fingerprint-initial' -ArtifactPath $fingerprintArtifact -BuildRoot (Join-Path $regressionRoot 'fingerprint\build') | Out-Null
        $fingerprintBefore = ([xml](Get-Content -LiteralPath "$fingerprintArtifact.inputs.sha256" -Raw)).catalogIntegrity.inputFingerprint
        New-Item -ItemType Directory -Path (Split-Path -Parent $arbitrarySourcePath) -Force | Out-Null
        Set-Content -LiteralPath $arbitrarySourcePath -Value 'namespace AdversarialFingerprint; internal static class ArbitrarySource { }' -NoNewline
        Invoke-DirectCatalogBuild -Name 'adversarial-fingerprint-arbitrary-obj-source' -ArtifactPath $fingerprintArtifact -BuildRoot (Join-Path $regressionRoot 'fingerprint\build') | Out-Null
        $fingerprintAfter = ([xml](Get-Content -LiteralPath "$fingerprintArtifact.inputs.sha256" -Raw)).catalogIntegrity.inputFingerprint
        if ($fingerprintBefore -eq $fingerprintAfter) {
            throw 'An arbitrary source file beneath a directory named obj did not invalidate the catalog input fingerprint.'
        }
    }
    finally {
        if (Test-Path -LiteralPath $arbitrarySourceRoot) {
            Remove-Item -LiteralPath $arbitrarySourceRoot -Recurse -Force
        }
    }

    $sameDestinationArtifact = Join-Path $regressionRoot 'same-destination\catalogs.sqlite'
    $sameDestinationJobs = @()
    $sameDestinationJobs += Start-Job -Name 'same-destination-one' -ScriptBlock {
            param($ProjectPath, $ArtifactPath, $CatalogRoot, $BuildRoot)
            $output = dotnet build $ProjectPath -c Release -v minimal -m:1 --no-restore "/p:BaseOutputPath=$BuildRoot\" "/p:DataGenCatalogArtifactPath=$ArtifactPath" "/p:DataGenCatalogRoot=$CatalogRoot" 2>&1 | Out-String
            [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
        } -ArgumentList @($projectPath, $sameDestinationArtifact, $catalogRoot, (Join-Path $regressionRoot 'same-destination\build-one'))
    $sameDestinationJobs += Start-Job -Name 'same-destination-two' -ScriptBlock {
            param($ProjectPath, $ArtifactPath, $CatalogRoot, $BuildRoot)
            $output = dotnet build $ProjectPath -c Release -v minimal -m:1 --no-restore "/p:BaseOutputPath=$BuildRoot\" "/p:DataGenCatalogArtifactPath=$ArtifactPath" "/p:DataGenCatalogRoot=$CatalogRoot" 2>&1 | Out-String
            [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
        } -ArgumentList @($projectPath, $sameDestinationArtifact, $catalogRoot, (Join-Path $regressionRoot 'same-destination\build-two'))
    try {
        Wait-Job -Job $sameDestinationJobs | Out-Null
        foreach ($job in $sameDestinationJobs) {
            $result = Receive-Job -Job $job
            [IO.File]::WriteAllText((Join-Path $logsRoot "adversarial-$($job.Name).log"), $result.Output, [Text.UTF8Encoding]::new($false))
            if ($result.ExitCode -ne 0) {
                throw "Same-destination catalog build '$($job.Name)' exited with code $($result.ExitCode)."
            }
        }
    }
    finally {
        Remove-Job -Job $sameDestinationJobs -Force -ErrorAction SilentlyContinue
    }
    if (-not (Test-Path -LiteralPath $sameDestinationArtifact -PathType Leaf) -or -not (Test-Path -LiteralPath "$sameDestinationArtifact.inputs.sha256" -PathType Leaf)) {
        throw 'Same-destination catalog generation did not leave both artifact and integrity receipt.'
    }

    $childJunctionRoot = Join-Path $regressionRoot 'child-junction'
    $childJunctionOutputRoot = Join-Path $childJunctionRoot 'output'
    $childJunctionEscapeRoot = Join-Path $childJunctionRoot 'escaped'
    $childJunctionSentinel = Join-Path $childJunctionEscapeRoot 'sentinel.txt'
    New-Item -ItemType Directory -Path $childJunctionOutputRoot, $childJunctionEscapeRoot -Force | Out-Null
    Set-Content -LiteralPath $childJunctionSentinel -Value 'must survive child-junction validation' -NoNewline
    New-Item -ItemType Junction -Path (Join-Path $childJunctionOutputRoot 'build') -Target $childJunctionEscapeRoot | Out-Null
    $packageOutput = & "$PSHOME\pwsh.exe" -NoLogo -NoProfile -NonInteractive -File (Join-Path $sourceRoot 'scripts\package-module.ps1') -ProjectPath 'missing\child-junction-probe.csproj' -OutputRoot $childJunctionOutputRoot 2>&1 | Out-String
    $packageExitCode = $LASTEXITCODE
    [IO.File]::WriteAllText((Join-Path $logsRoot 'adversarial-child-junction.log'), $packageOutput, [Text.UTF8Encoding]::new($false))
    $escapedWrites = Get-ChildItem -LiteralPath $childJunctionEscapeRoot -Force | Where-Object Name -ne 'sentinel.txt'
    if ($packageExitCode -eq 0 -or $packageOutput -notmatch 'Unsafe package build path' -or $packageOutput -match 'Module project not found|Building ' -or -not (Test-Path -LiteralPath $childJunctionSentinel) -or $escapedWrites) {
        throw 'A child reparse point was not rejected before package project/build work or redirected a write outside OutputRoot.'
    }

    $deepJunctionRoot = Join-Path $regressionRoot 'deep-junction'
    $deepJunctionOutputRoot = Join-Path $deepJunctionRoot 'output'
    $deepJunctionEscapeRoot = Join-Path $deepJunctionRoot 'escaped'
    $deepJunctionSentinel = Join-Path $deepJunctionEscapeRoot 'sentinel.txt'
    New-Item -ItemType Directory -Path (Join-Path $deepJunctionOutputRoot 'build'), $deepJunctionEscapeRoot -Force | Out-Null
    Set-Content -LiteralPath $deepJunctionSentinel -Value 'must survive deep-junction validation' -NoNewline
    New-Item -ItemType Junction -Path (Join-Path $deepJunctionOutputRoot 'build\obj') -Target $deepJunctionEscapeRoot | Out-Null
    $deepPackageOutput = & "$PSHOME\pwsh.exe" -NoLogo -NoProfile -NonInteractive -File (Join-Path $sourceRoot 'scripts\package-module.ps1') -ProjectPath 'missing\deep-junction-probe.csproj' -OutputRoot $deepJunctionOutputRoot 2>&1 | Out-String
    $deepPackageExitCode = $LASTEXITCODE
    [IO.File]::WriteAllText((Join-Path $logsRoot 'adversarial-deep-junction.log'), $deepPackageOutput, [Text.UTF8Encoding]::new($false))
    $deepEscapedWrites = Get-ChildItem -LiteralPath $deepJunctionEscapeRoot -Force | Where-Object Name -ne 'sentinel.txt'
    if ($deepPackageExitCode -eq 0 -or $deepPackageOutput -notmatch 'Unsafe package staging path' -or $deepPackageOutput -match 'Module project not found|Building ' -or -not (Test-Path -LiteralPath $deepJunctionSentinel) -or $deepEscapedWrites) {
        throw 'A deeper build descendant reparse point was not rejected before package project/build work or redirected a write outside OutputRoot.'
    }

    $circularOutputRoot = Join-Path $sourceRoot ("src\\_v094-contract-circular-" + [Guid]::NewGuid().ToString('N'))
    $circularArtifact = Join-Path $circularOutputRoot 'catalogs.sqlite'
    try {
        Invoke-DirectCatalogBuild -Name 'adversarial-circular-initial' -ArtifactPath $circularArtifact -BuildRoot (Join-Path $regressionRoot 'circular\build') | Out-Null
        $circularArtifactTimestamp = (Get-Item -LiteralPath $circularArtifact).LastWriteTimeUtc
        $circularReceiptTimestamp = (Get-Item -LiteralPath "$circularArtifact.inputs.sha256").LastWriteTimeUtc
        Invoke-DirectCatalogBuild -Name 'adversarial-circular-noop' -ArtifactPath $circularArtifact -BuildRoot (Join-Path $regressionRoot 'circular\build') | Out-Null
        if ((Get-Item -LiteralPath $circularArtifact).LastWriteTimeUtc -ne $circularArtifactTimestamp -or (Get-Item -LiteralPath "$circularArtifact.inputs.sha256").LastWriteTimeUtc -ne $circularReceiptTimestamp) {
            throw 'A catalog artifact beneath src was included in its own fingerprint and regenerated on an unchanged second build.'
        }
    }
    finally {
        if (Test-Path -LiteralPath $circularOutputRoot) {
            Remove-Item -LiteralPath $circularOutputRoot -Recurse -Force
        }
    }

    $nativeFailure = Invoke-DirectCatalogBuild -Name 'adversarial-native-failure' -ArtifactPath (Join-Path $regressionRoot 'native-failure\catalogs.sqlite') -BuildRoot (Join-Path $regressionRoot 'native-failure\build') -PowerShellExecutable 'where.exe' -AllowFailure
    if ($nativeFailure.ExitCode -eq 0 -or $nativeFailure.Output -notmatch 'Catalog generator exited with code') {
        throw 'A catalog generator native failure did not propagate as a failed build.'
    }

    $targetsSource = Get-Content -LiteralPath (Join-Path $sourceRoot 'Directory.Build.targets') -Raw
    if (-not $targetsSource.Contains('Global\\')) {
        throw 'The catalog lock does not use the Windows Global namespace contract for cross-session coordination.'
    }
}

function Invoke-WindowsPublisherMetadataRegressionForDrives {
    param(
        [Parameter(Mandatory)]
        [string[]]$DriveLetters,

        [switch]$RequireCrossFilesystem
    )

    if ($env:OS -ne 'Windows_NT') {
        throw 'Windows publisher metadata regression requires Windows.'
    }

    foreach ($driveLetter in $DriveLetters) {
        $volume = Get-Volume -DriveLetter $driveLetter -ErrorAction Stop
        if ($RequireCrossFilesystem.IsPresent) {
            $expectedFileSystem = if ($driveLetter -eq 'G') { 'ReFS' } else { 'NTFS' }
            if ($volume.FileSystem -ne $expectedFileSystem) {
                throw "Windows publisher metadata regression requires $driveLetter`: to be $expectedFileSystem, found '$($volume.FileSystem)'."
            }
        }
        elseif ($volume.FileSystem -notin @('NTFS', 'ReFS')) {
            throw "Windows publisher metadata portable regression requires an NTFS or ReFS volume, found '$($volume.FileSystem)' on $driveLetter`: ."
        }
    }

    $currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $metadataAttributes = [IO.FileAttributes]::Hidden -bor [IO.FileAttributes]::Archive
    Write-Host 'Windows SACL and mandatory-label note: this workstation gate independently attempts SACL_SECURITY_INFORMATION (0x8) and LABEL_SECURITY_INFORMATION (0x10), because Windows defines the mandatory integrity label as an SACL ACE but exposes it through a separate request flag. Without SeSecurityPrivilege the operating system need not expose either surface to an ordinary token. The publisher preserves each only when it can read and reapply it; an unavailable surface does not expand DACL-granted access, but leaves auditing or mandatory-integrity enforcement unverified.' -ForegroundColor Yellow

    function Set-RestrictiveFileMetadata {
        param(
            [Parameter(Mandatory)]
            [string]$Path
        )

        $acl = Get-Acl -LiteralPath $Path
        $acl.SetAccessRuleProtection($true, $false)
        foreach ($rule in @($acl.Access)) {
            [void]$acl.RemoveAccessRule($rule)
        }
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($currentIdentity, 'FullControl', 'Allow'))
        $daclIsRestrictive = $true
        try {
            Set-Acl -LiteralPath $Path -AclObject $acl
        }
        catch [UnauthorizedAccessException] {
            $daclIsRestrictive = $false
        }
        [IO.File]::SetAttributes($Path, $metadataAttributes)
        $timestamps = [pscustomobject]@{
            CreationTimeUtc = [DateTime]::SpecifyKind([DateTime]'2020-01-02T03:04:05.0060000', [DateTimeKind]::Utc)
            LastAccessTimeUtc = [DateTime]::SpecifyKind([DateTime]'2020-01-02T03:04:06.0070000', [DateTimeKind]::Utc)
            LastWriteTimeUtc = [DateTime]::SpecifyKind([DateTime]'2020-01-02T03:04:07.0080000', [DateTimeKind]::Utc)
        }
        [IO.File]::SetCreationTimeUtc($Path, $timestamps.CreationTimeUtc)
        [IO.File]::SetLastAccessTimeUtc($Path, $timestamps.LastAccessTimeUtc)
        [IO.File]::SetLastWriteTimeUtc($Path, $timestamps.LastWriteTimeUtc)

        $finalAcl = Get-Acl -LiteralPath $Path

        return [pscustomobject]@{
            Owner = $finalAcl.Owner
            Group = $finalAcl.Group
            Dacl = $finalAcl.GetSecurityDescriptorSddlForm([Security.AccessControl.AccessControlSections]::Access)
            Attributes = [IO.File]::GetAttributes($Path)
            CreationTimeUtc = [IO.File]::GetCreationTimeUtc($Path)
            LastAccessTimeUtc = [IO.File]::GetLastAccessTimeUtc($Path)
            LastWriteTimeUtc = [IO.File]::GetLastWriteTimeUtc($Path)
            DaclIsRestrictive = $daclIsRestrictive
        }
    }

    function Assert-PublisherMetadata {
        param(
            [Parameter(Mandatory)]
            [string]$Path,

            [Parameter(Mandatory)]
            [object]$Metadata,

            [Parameter(Mandatory)]
            [string]$Label,

            [Parameter(Mandatory)]
            [string]$DriveLetter
        )

        $actualItem = Get-Item -LiteralPath $Path -Force
        # Snapshot timestamps before ACL inspection so an access-time-enabled filesystem cannot turn the assertion into its own mutation.
        $actualTimestamps = [pscustomobject]@{
            CreationTimeUtc = $actualItem.CreationTimeUtc
            LastAccessTimeUtc = $actualItem.LastAccessTimeUtc
            LastWriteTimeUtc = $actualItem.LastWriteTimeUtc
        }
        $actualAttributes = [IO.File]::GetAttributes($Path)
        $actualAcl = Get-Acl -LiteralPath $Path
        if ($actualAcl.Owner -ne $Metadata.Owner -or $actualAcl.Group -ne $Metadata.Group -or $actualAcl.GetSecurityDescriptorSddlForm([Security.AccessControl.AccessControlSections]::Access) -ne $Metadata.Dacl -or $actualAttributes -ne $Metadata.Attributes) {
            throw "Windows publisher metadata regression did not preserve owner, group, restrictive DACL, and attributes for the $Label on $DriveLetter`:. Expected owner '$($Metadata.Owner)', group '$($Metadata.Group)', DACL '$($Metadata.Dacl)', and attributes '$($Metadata.Attributes)'; actual owner '$($actualAcl.Owner)', group '$($actualAcl.Group)', DACL '$($actualAcl.GetSecurityDescriptorSddlForm([Security.AccessControl.AccessControlSections]::Access))', and attributes '$actualAttributes'."
        }

        # ReFS and NTFS both retain 100-ns FILETIME values locally, but allow a small filesystem/provider rounding window.
        $timestampTolerance = [TimeSpan]::FromSeconds(2)
        foreach ($timestamp in @('CreationTimeUtc', 'LastAccessTimeUtc', 'LastWriteTimeUtc')) {
            $actual = $actualTimestamps.$timestamp
            $expected = $Metadata.$timestamp
            if ([Math]::Abs(($actual - $expected).Ticks) -gt $timestampTolerance.Ticks) {
                throw "Windows publisher metadata regression did not preserve $timestamp for the $Label on $DriveLetter`:. Expected '$expected'; actual '$actual'; tolerance '$timestampTolerance'."
            }
        }
    }

    function Assert-WindowsPublisherMetadataOriginalPathChain {
        param(
            [Parameter(Mandatory)]
            [string]$Path,

            [Parameter(Mandatory)]
            [string]$DriveLetter,

            [Parameter(Mandatory)]
            [string]$ExpectedFileSystem
        )

        $driveRoot = [IO.Path]::GetFullPath("$DriveLetter`:\")
        $fullPath = [IO.Path]::GetFullPath($Path)
        if ([IO.Path]::GetPathRoot($fullPath) -ine $driveRoot -or
            -not (Test-PathContains -ParentPath $driveRoot -ChildPath $fullPath)) {
            throw "Publisher metadata path '$Path' must be contained by explicit drive root '$driveRoot'."
        }

        $volume = Get-Volume -DriveLetter $DriveLetter -ErrorAction Stop
        if ($volume.FileSystem -cne $ExpectedFileSystem) {
            throw "Publisher metadata path '$fullPath' requires $DriveLetter`: $ExpectedFileSystem, but Get-Volume reports '$($volume.FileSystem)'."
        }

        $currentPath = $fullPath.TrimEnd('\')
        while ($true) {
            $inspectionPath = if ($currentPath -ieq $driveRoot.TrimEnd('\')) { $driveRoot } else { $currentPath }
            if (Test-Path -LiteralPath $inspectionPath) {
                $item = Get-Item -LiteralPath $inspectionPath -Force -ErrorAction Stop
                if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                    throw "Publisher metadata path '$fullPath' is or is beneath original reparse point '$inspectionPath'."
                }
            }
            if ($currentPath -ieq $driveRoot.TrimEnd('\')) {
                break
            }
            $parent = [IO.Directory]::GetParent($currentPath)
            if ($null -eq $parent -or -not (Test-PathContains -ParentPath $driveRoot -ChildPath $parent.FullName)) {
                throw "Publisher metadata path '$fullPath' escaped explicit drive root '$driveRoot'."
            }
            $currentPath = $parent.FullName.TrimEnd('\')
        }
        return $fullPath
    }

    function Remove-PublisherMetadataProbe {
        param(
            [Parameter(Mandatory)]
            [string]$ProbeRoot,

            [Parameter(Mandatory)]
            [string]$ProbeBasePath,

            [Parameter(Mandatory)]
            [string]$DriveLetter,

            [Parameter(Mandatory)]
            [string]$ExpectedFileSystem
        )

        if (-not (Test-Path -LiteralPath $ProbeRoot)) {
            return
        }

        $validatedProbeRoot = Assert-WindowsPublisherMetadataProbePath `
            -ProbeRoot $ProbeRoot `
            -ProbeBasePath $ProbeBasePath `
            -DriveLetter $DriveLetter `
            -ExpectedFileSystem $ExpectedFileSystem
        $resolvedProbeRoot = $validatedProbeRoot.ProbeRoot

        $reparsePoint = Get-ChildItem -LiteralPath $resolvedProbeRoot -Recurse -Force | Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 } | Select-Object -First 1
        if ($reparsePoint) {
            throw "Refusing to clean publisher metadata probe '$resolvedProbeRoot' because it contains reparse point '$($reparsePoint.FullName)'."
        }
        Get-ChildItem -LiteralPath $resolvedProbeRoot -Recurse -Force | ForEach-Object { $_.Attributes = [IO.FileAttributes]::Normal }
        Remove-Item -LiteralPath $resolvedProbeRoot -Recurse -Force
        if (Test-Path -LiteralPath $resolvedProbeRoot) {
            throw "Windows publisher metadata regression did not clean probe '$resolvedProbeRoot'."
        }
    }

    function Assert-WindowsPublisherMetadataProbePath {
        param(
            [Parameter(Mandatory)]
            [string]$ProbeRoot,

            [Parameter(Mandatory)]
            [string]$ProbeBasePath,

            [Parameter(Mandatory)]
            [string]$DriveLetter,

            [Parameter(Mandatory)]
            [string]$ExpectedFileSystem
        )

        $expectedProbeBasePath = [IO.Path]::GetFullPath((Join-Path "$DriveLetter`:\" 'DataGenWindowsPublisherProof'))
        $originalProbeBasePath = Assert-WindowsPublisherMetadataOriginalPathChain `
            -Path $ProbeBasePath `
            -DriveLetter $DriveLetter `
            -ExpectedFileSystem $ExpectedFileSystem
        $originalProbeRoot = Assert-WindowsPublisherMetadataOriginalPathChain `
            -Path $ProbeRoot `
            -DriveLetter $DriveLetter `
            -ExpectedFileSystem $ExpectedFileSystem
        if ($originalProbeBasePath -ine $expectedProbeBasePath) {
            throw "Publisher metadata probe base must be the explicit per-drive path '$expectedProbeBasePath', not '$originalProbeBasePath'."
        }
        if (-not (Test-PathContains -ParentPath $originalProbeBasePath -ChildPath $originalProbeRoot) -or $originalProbeRoot -ieq $originalProbeBasePath) {
            throw "Refusing publisher metadata probe '$originalProbeRoot' outside its validated base '$originalProbeBasePath'."
        }

        $resolvedProbeBasePath = (Resolve-Path -LiteralPath $originalProbeBasePath -ErrorAction Stop).Path
        $resolvedProbeRoot = (Resolve-Path -LiteralPath $originalProbeRoot -ErrorAction Stop).Path
        if ($resolvedProbeBasePath -ine $originalProbeBasePath -or $resolvedProbeRoot -ine $originalProbeRoot) {
            throw "Publisher metadata probe path changed while resolving '$originalProbeRoot'."
        }
        $resolvedBaseDrive = (Split-Path -Path $resolvedProbeBasePath -Qualifier).TrimEnd(':', '\')
        $resolvedProbeDrive = (Split-Path -Path $resolvedProbeRoot -Qualifier).TrimEnd(':', '\')
        if ($resolvedBaseDrive -ine $DriveLetter -or $resolvedProbeDrive -ine $DriveLetter) {
            throw "Publisher metadata probe path must resolve on $DriveLetter`:, not '$resolvedProbeRoot'."
        }
        if (-not (Test-PathContains -ParentPath $resolvedProbeBasePath -ChildPath $resolvedProbeRoot) -or $resolvedProbeRoot -eq $resolvedProbeBasePath) {
            throw "Refusing publisher metadata probe '$resolvedProbeRoot' outside its validated base '$resolvedProbeBasePath'."
        }

        [pscustomobject]@{
            ProbeRoot = $resolvedProbeRoot
            ProbeBasePath = $resolvedProbeBasePath
        }
    }

    function Resolve-WindowsPublisherMetadataProbeRoot {
        param(
            [Parameter(Mandatory)]
            [string]$DriveLetter,

            [Parameter(Mandatory)]
            [string]$ExpectedFileSystem
        )

        $driveRoot = [IO.Path]::GetFullPath("$DriveLetter`:\")
        $probeBasePath = Join-Path $driveRoot 'DataGenWindowsPublisherProof'
        $probeBasePath = Assert-WindowsPublisherMetadataOriginalPathChain `
            -Path $probeBasePath `
            -DriveLetter $DriveLetter `
            -ExpectedFileSystem $ExpectedFileSystem
        if (Test-Path -LiteralPath $probeBasePath) {
            if (-not (Test-Path -LiteralPath $probeBasePath -PathType Container)) {
                throw "Publisher metadata probe base '$probeBasePath' must be a directory."
            }
        }
        else {
            New-Item -ItemType Directory -Path $probeBasePath -ErrorAction Stop | Out-Null
        }
        $probeBasePath = Assert-WindowsPublisherMetadataOriginalPathChain `
            -Path $probeBasePath `
            -DriveLetter $DriveLetter `
            -ExpectedFileSystem $ExpectedFileSystem
        $probeRoot = Join-Path $probeBasePath ("probe-" + [Guid]::NewGuid().ToString('N'))
        if (-not (Test-PathContains -ParentPath $probeBasePath -ChildPath $probeRoot)) {
            throw "Publisher metadata probe child '$probeRoot' escaped validated base '$probeBasePath'."
        }
        New-Item -ItemType Directory -Path $probeRoot -Force | Out-Null
        return Assert-WindowsPublisherMetadataProbePath `
            -ProbeRoot $probeRoot `
            -ProbeBasePath $probeBasePath `
            -DriveLetter $DriveLetter `
            -ExpectedFileSystem $ExpectedFileSystem
    }

    foreach ($driveLetter in $DriveLetters) {
        $expectedFileSystem = if ($RequireCrossFilesystem.IsPresent) {
            if ($driveLetter -eq 'G') { 'ReFS' } else { 'NTFS' }
        }
        else {
            (Get-Volume -DriveLetter $driveLetter -ErrorAction Stop).FileSystem
        }
        $validatedProbe = Resolve-WindowsPublisherMetadataProbeRoot -DriveLetter $driveLetter -ExpectedFileSystem $expectedFileSystem
        $probeBasePath = $validatedProbe.ProbeBasePath
        $probeRoot = $validatedProbe.ProbeRoot
        $artifactPath = Join-Path $probeRoot 'catalogs.sqlite'
        $fingerprintPath = "$artifactPath.inputs.sha256"
        $projectPath = Join-Path $probeRoot 'publisher-probe.proj'
        $publisherWriterPath = Join-Path $probeRoot 'publisher-writer.cmd'
        $buildRoot = Join-Path $probeRoot 'build'

        try {
            Set-Content -LiteralPath $artifactPath -Value 'obsolete artifact' -NoNewline
            Set-Content -LiteralPath $fingerprintPath -Value 'obsolete receipt' -NoNewline
            $expectedArtifactMetadata = Set-RestrictiveFileMetadata -Path $artifactPath
            $expectedFingerprintMetadata = Set-RestrictiveFileMetadata -Path $fingerprintPath
            if ($driveLetter -eq 'D' -and (-not $expectedArtifactMetadata.DaclIsRestrictive -or -not $expectedFingerprintMetadata.DaclIsRestrictive)) {
                throw 'Windows publisher metadata regression could not establish the required restrictive NTFS DACL fixture.'
            }
            $escapedTargetsPath = [Security.SecurityElement]::Escape((Join-Path $sourceRoot 'Directory.Build.targets'))
            $forceSecurityMetadataApplication = if (-not $RequireCrossFilesystem.IsPresent -or $driveLetter -eq 'D') { 'true' } else { 'false' }
            @'
@echo off
setlocal EnableDelayedExpansion
set "output="
:next
if "%~1"=="" goto write
if /I "%~1"=="-OutputPath" (
  set "output=%~2"
  shift
)
shift
goto next
:write
> "%output%" <nul set /p "=publisher metadata probe"
exit /b 0
'@ | Set-Content -LiteralPath $publisherWriterPath -NoNewline

            @"
<Project>
  <Import Project="$escapedTargetsPath" />
  <PropertyGroup>
    <GenerateSeededCatalogDatabase>true</GenerateSeededCatalogDatabase>
    <DataGenCatalogForceSecurityMetadataApplication>$forceSecurityMetadataApplication</DataGenCatalogForceSecurityMetadataApplication>
    <DataGenCatalogArtifactPath>$artifactPath</DataGenCatalogArtifactPath>
    <DataGenCatalogFingerprintPath>$fingerprintPath</DataGenCatalogFingerprintPath>
    <DataGenPowerShellExecutable>$publisherWriterPath</DataGenPowerShellExecutable>
  </PropertyGroup>
</Project>
"@ | Set-Content -LiteralPath $projectPath -NoNewline

            $buildOutput = & $DotNetPath msbuild $projectPath -target:GenerateSeededCatalogDatabase -nologo -v:minimal "/p:BaseOutputPath=$buildRoot\\" 2>&1 | Out-String
            $buildExitCode = $LASTEXITCODE
            if ($buildExitCode -ne 0) {
                throw "Windows publisher metadata regression failed on $driveLetter`: with exit code $buildExitCode. Output: $buildOutput"
            }

            foreach ($expected in @(
                [pscustomobject]@{ Path = $artifactPath; Metadata = $expectedArtifactMetadata; Label = 'artifact' },
                [pscustomobject]@{ Path = $fingerprintPath; Metadata = $expectedFingerprintMetadata; Label = 'receipt' }
            )) {
                Assert-PublisherMetadata -Path $expected.Path -Metadata $expected.Metadata -Label $expected.Label -DriveLetter $driveLetter
            }

            if (-not (Test-Path -LiteralPath $artifactPath -PathType Leaf) -or -not (Test-Path -LiteralPath $fingerprintPath -PathType Leaf)) {
                throw "Windows publisher metadata regression did not retain both artifact and receipt on $driveLetter`: ."
            }

            $receipt = [xml](Get-Content -LiteralPath $fingerprintPath -Raw)
            $artifactHash = (Get-FileHash -LiteralPath $artifactPath -Algorithm SHA256).Hash
            if ($receipt.catalogIntegrity.artifactSha256 -ne $artifactHash -or [string]::IsNullOrWhiteSpace($receipt.catalogIntegrity.inputFingerprint)) {
                throw "Windows publisher metadata regression did not retain an exact artifact/receipt integrity pair on $driveLetter`: ."
            }
        }
        finally {
            Remove-PublisherMetadataProbe -ProbeRoot $probeRoot -ProbeBasePath $probeBasePath -DriveLetter $driveLetter -ExpectedFileSystem $expectedFileSystem
        }
    }
}

function Invoke-WindowsPublisherMetadataPortableRegression {
    if ($env:OS -ne 'Windows_NT') {
        throw 'Windows publisher metadata regression requires Windows.'
    }

    $temporaryDrive = (Split-Path -Path ([IO.Path]::GetTempPath()) -Qualifier).TrimEnd(':', '\')
    if ([string]::IsNullOrWhiteSpace($temporaryDrive)) {
        throw 'Windows publisher metadata portable regression could not determine a writable temporary drive.'
    }

    Invoke-WindowsPublisherMetadataRegressionForDrives -DriveLetters @($temporaryDrive)
}

function Invoke-WindowsPublisherMetadataRegression {
    Invoke-WindowsPublisherMetadataRegressionForDrives -DriveLetters @('D', 'G') -RequireCrossFilesystem
}

function Invoke-WindowsPublisherMetadataProbeBaseReparseRegression {
    $driveRoot = 'D:\'
    $probeBasePath = Join-Path $driveRoot 'DataGenWindowsPublisherProof'
    $fixtureId = [Guid]::NewGuid().ToString('N')
    $savedBasePath = Join-Path $driveRoot "DataGenWindowsPublisherProof-saved-$fixtureId"
    $junctionTargetPath = Join-Path $driveRoot "DataGenWindowsPublisherProof-target-$fixtureId"
    $sentinelPath = Join-Path $junctionTargetPath 'sentinel.txt'
    $savedBase = $false
    $junctionCreated = $false
    $junctionSupported = $true

    try {
        if (Test-Path -LiteralPath $probeBasePath) {
            $existingBase = Get-Item -LiteralPath $probeBasePath -Force
            if (($existingBase.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Same-drive junction regression requires the existing probe base '$probeBasePath' not to be a reparse point."
            }
            if (@(Get-ChildItem -LiteralPath $probeBasePath -Force).Count -ne 0) {
                throw "Same-drive junction regression requires the managed probe base '$probeBasePath' to be empty."
            }
            Move-Item -LiteralPath $probeBasePath -Destination $savedBasePath
            $savedBase = $true
        }

        New-Item -ItemType Directory -Path $junctionTargetPath -Force | Out-Null
        Set-Content -LiteralPath $sentinelPath -Value 'same-drive junction target must remain untouched' -NoNewline
        $expectedTargetTimestamp = [DateTime]::SpecifyKind([DateTime]'2020-01-02T03:04:05', [DateTimeKind]::Utc)
        [IO.Directory]::SetLastWriteTimeUtc($junctionTargetPath, $expectedTargetTimestamp)
        try {
            New-Item -ItemType Junction -Path $probeBasePath -Target $junctionTargetPath -ErrorAction Stop | Out-Null
            $junctionCreated = $true
        }
        catch {
            $junctionSupported = $false
            Write-Warning "Same-drive junction regression skipped because junction creation is unavailable: $($_.Exception.Message)"
        }
        if (-not $junctionSupported) {
            return
        }

        $junctionItem = Get-Item -LiteralPath $probeBasePath -Force
        if (($junctionItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) {
            throw "Same-drive junction regression could not establish a reparse point at '$probeBasePath'."
        }

        $rejection = $null
        try {
            Invoke-WindowsPublisherMetadataRegressionForDrives -DriveLetters @('D') -RequireCrossFilesystem
        }
        catch {
            $rejection = $_.Exception.Message
        }
        if ([string]::IsNullOrWhiteSpace($rejection) -or $rejection -notmatch 'reparse') {
            throw "Publisher metadata regression did not reject the original same-drive junction probe base before traversal. Error: $rejection"
        }
        $targetChildren = @(Get-ChildItem -LiteralPath $junctionTargetPath -Force)
        if ($targetChildren.Count -ne 1 -or $targetChildren[0].FullName -ine $sentinelPath -or
            [IO.Directory]::GetLastWriteTimeUtc($junctionTargetPath) -ne $expectedTargetTimestamp) {
            throw 'Publisher metadata regression wrote through or cleaned through the rejected same-drive junction.'
        }
    }
    finally {
        if ($junctionCreated -and (Test-Path -LiteralPath $probeBasePath)) {
            $junctionItem = Get-Item -LiteralPath $probeBasePath -Force
            if (($junctionItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) {
                throw "Refusing fixture cleanup because '$probeBasePath' is no longer the junction created by the regression."
            }
            Remove-Item -LiteralPath $probeBasePath -Force
        }
        if (Test-Path -LiteralPath $junctionTargetPath) {
            $targetItem = Get-Item -LiteralPath $junctionTargetPath -Force
            if (($targetItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
                [IO.Path]::GetPathRoot($targetItem.FullName) -ine $driveRoot) {
                throw "Refusing fixture cleanup for unsafe junction target '$junctionTargetPath'."
            }
            Remove-Item -LiteralPath $junctionTargetPath -Recurse -Force
        }
        if ($savedBase) {
            if (Test-Path -LiteralPath $probeBasePath) {
                throw "Cannot restore saved probe base because '$probeBasePath' still exists."
            }
            Move-Item -LiteralPath $savedBasePath -Destination $probeBasePath
        }
    }
}

function Invoke-TimestampOffsetRegression {
    if (-not [IO.Path]::IsPathFullyQualified($DotNetPath) -or -not (Test-Path -LiteralPath $DotNetPath -PathType Leaf)) {
        throw "DotNetPath must identify an existing executable by full path: '$DotNetPath'."
    }

    $regressionRoot = Join-Path $outputRoot 'timestamp-offset'
    $catalogRoot = Join-Path $regressionRoot 'catalog-inputs'
    $buildRoot = Join-Path $regressionRoot 'build'
    $toolProjectPath = Join-Path $sourceRoot 'src\SyntheticEnterprise.CatalogTool\SyntheticEnterprise.CatalogTool.csproj'
    $toolPath = Join-Path $buildRoot 'Release\net8.0\SyntheticEnterprise.CatalogTool.dll'

    New-Item -ItemType Directory -Path $catalogRoot -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $catalogRoot 'catalog-import-manifest.json') -Value @'
{
  "version": "v094-timestamp-offset",
  "tables": [
    { "tableName": "software_catalog", "strategy": "copy_csv", "sourceFiles": [ "software_catalog.csv" ] }
  ]
}
'@ -NoNewline
    Set-Content -LiteralPath (Join-Path $catalogRoot 'software_catalog.csv') -Value @'
Name,Category,Vendor,Version
Timestamp Fixture,Security,Contoso,1.0
'@ -NoNewline

    $buildOutput = & $DotNetPath build $toolProjectPath -c Release -v minimal --no-restore "/p:BaseOutputPath=$buildRoot\" 2>&1 | Out-String
    $buildExitCode = $LASTEXITCODE
    [IO.File]::WriteAllText((Join-Path $logsRoot 'timestamp-offset-build.log'), $buildOutput, [Text.UTF8Encoding]::new($false))
    if ($buildExitCode -ne 0 -or -not (Test-Path -LiteralPath $toolPath -PathType Leaf)) {
        throw "Timestamp-offset test could not build the catalog tool. See '$logsRoot\timestamp-offset-build.log'."
    }

    function Invoke-CatalogTool {
        param(
            [Parameter(Mandatory)]
            [string]$Name,

            [Parameter(Mandatory)]
            [string[]]$Arguments
        )

        $commandOutput = & $DotNetPath $toolPath @Arguments 2>&1 | Out-String
        $commandExitCode = $LASTEXITCODE
        [IO.File]::WriteAllText((Join-Path $logsRoot "$Name.log"), $commandOutput, [Text.UTF8Encoding]::new($false))
        return [pscustomobject]@{
            ExitCode = $commandExitCode
            Output = $commandOutput
        }
    }

    $offsetless = Invoke-CatalogTool -Name 'timestamp-offsetless' -Arguments @(
        'build', '--catalog-root', $catalogRoot, '--output', (Join-Path $regressionRoot 'offsetless.sqlite'), '--build-timestamp-utc', '2026-08-09T15:30:00'
    )
    if ($offsetless.ExitCode -eq 0 -or $offsetless.Output -notmatch 'explicit UTC offset') {
        throw 'An offsetless ISO build timestamp was accepted instead of being rejected.'
    }

    function Assert-BuildTimestamp {
        param(
            [Parameter(Mandatory)]
            [string]$Name,

            [string]$Timestamp,

            [Parameter(Mandatory)]
            [string]$ExpectedUtcTimestamp
        )

        $databasePath = Join-Path $regressionRoot "$Name.sqlite"
        $buildArguments = @('build', '--catalog-root', $catalogRoot, '--output', $databasePath)
        if ($Timestamp) {
            $buildArguments += @('--build-timestamp-utc', $Timestamp)
        }

        $build = Invoke-CatalogTool -Name "$Name-build" -Arguments $buildArguments
        if ($build.ExitCode -ne 0) {
            throw "Timestamp case '$Name' failed. See '$logsRoot\$Name-build.log'."
        }

        $expectedDatabasePath = Join-Path $regressionRoot "$Name-expected.sqlite"
        $expected = Invoke-CatalogTool -Name "$Name-expected" -Arguments @(
            'build', '--catalog-root', $catalogRoot, '--output', $expectedDatabasePath, '--build-timestamp-utc', $ExpectedUtcTimestamp
        )
        if ($expected.ExitCode -ne 0) {
            throw "Timestamp case '$Name' could not build its UTC-normalized expected catalog. See '$logsRoot\$Name-expected.log'."
        }

        if ((Get-FileHash -LiteralPath $databasePath -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $expectedDatabasePath -Algorithm SHA256).Hash) {
            throw "Timestamp case '$Name' did not normalize to the equivalent UTC instant '$ExpectedUtcTimestamp'."
        }
    }

    Assert-BuildTimestamp -Name 'timestamp-z' -Timestamp '2026-08-09T15:30:00Z' -ExpectedUtcTimestamp '2026-08-09T15:30:00Z'
    Assert-BuildTimestamp -Name 'timestamp-positive-offset' -Timestamp '2026-08-09T15:30:00+02:00' -ExpectedUtcTimestamp '2026-08-09T13:30:00Z'
    Assert-BuildTimestamp -Name 'timestamp-negative-offset' -Timestamp '2026-08-09T15:30:00-05:00' -ExpectedUtcTimestamp '2026-08-09T20:30:00Z'

    $originalSourceDateEpoch = [Environment]::GetEnvironmentVariable('SOURCE_DATE_EPOCH')
    try {
        [Environment]::SetEnvironmentVariable('SOURCE_DATE_EPOCH', '1786289400')
        Assert-BuildTimestamp -Name 'timestamp-source-date-epoch' -ExpectedUtcTimestamp '2026-08-09T15:30:00Z'
    }
    finally {
        [Environment]::SetEnvironmentVariable('SOURCE_DATE_EPOCH', $originalSourceDateEpoch)
    }
}

if ($WindowsPublisherMetadataRegressionOnly.IsPresent) {
    try {
        Invoke-WindowsPublisherMetadataProbeBaseReparseRegression
        Invoke-WindowsPublisherMetadataRegression
    }
    finally {
        if (Test-Path -LiteralPath $outputRoot) {
            Remove-Item -LiteralPath $outputRoot -Recurse -Force
        }
    }
    $global:LASTEXITCODE = 0
    Write-Host 'Windows publisher metadata regression passed on ReFS and NTFS.' -ForegroundColor Green
    return
}

if ($WindowsPublisherMetadataPortableOnly.IsPresent) {
    Invoke-WindowsPublisherMetadataPortableRegression
    $global:LASTEXITCODE = 0
    Write-Host 'Windows publisher metadata portable regression passed.' -ForegroundColor Green
    return
}

if ($AdversarialRegressionOnly.IsPresent) {
    Invoke-AdversarialReleaseRemediationRegression
    $global:LASTEXITCODE = 0
    Write-Host 'Adversarial release-remediation catalog contract passed.' -ForegroundColor Green
    return
}

if ($TimestampOffsetRegressionOnly.IsPresent) {
    Invoke-TimestampOffsetRegression
    $global:LASTEXITCODE = 0
    Write-Host 'Timestamp explicit-offset catalog contract passed (5 cases).' -ForegroundColor Green
    return
}

$archivePath = Join-Path $outputRoot 'clean-source.tar'
Invoke-LoggedCommand -Name 'archive' -Command {
    $indexedTree = git -C $sourceRoot write-tree
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to write the indexed source tree.'
    }

    git -C $sourceRoot archive --format=tar --prefix=source/ --output=$archivePath $indexedTree.Trim()
}

Invoke-LoggedCommand -Name 'extract' -Command {
    tar -xf $archivePath -C $outputRoot
}

if (Test-Path -LiteralPath (Join-Path $snapshotRoot 'catalogs\catalogs.sqlite')) {
    throw 'The clean source snapshot unexpectedly contains the generated catalogs.sqlite artifact.'
}

$originalRepositoryRoot = [Environment]::GetEnvironmentVariable('DATAGEN_REPOSITORY_ROOT')
try {
    [Environment]::SetEnvironmentVariable('DATAGEN_REPOSITORY_ROOT', $null)

    if (-not $TimestampInvalidationRegressionOnly.IsPresent -and -not $ConcurrentArtifactRegressionOnly.IsPresent) {
        Invoke-PackageCleanupSafetyRegression
    }
    if ($PackageCleanupSafetyRegressionOnly.IsPresent) {
        Write-Host 'Package cleanup safety contract passed.' -ForegroundColor Green
        return
    }

    if (-not $TimestampInvalidationRegressionOnly.IsPresent) {
        Invoke-ConcurrentCatalogBuildRegression
    }
    if ($ConcurrentArtifactRegressionOnly.IsPresent) {
        Write-Host 'Concurrent distinct-artifact catalog build contract passed.' -ForegroundColor Green
        return
    }

    $buildProperties = @(
        "/p:ArtifactsPath=$outputRoot\artifacts\",
        '/p:UseArtifactsOutput=true',
        "/p:DataGenCatalogArtifactPath=$catalogArtifactPath"
    )

    Invoke-LoggedCommand -Name 'solution-build' -Command {
        dotnet build (Join-Path $snapshotRoot 'DataGen.slnx') -c Release -v minimal -m:8 @buildProperties
    }

    if (-not (Test-Path -LiteralPath $catalogArtifactPath -PathType Leaf)) {
        throw "The solution build did not generate '$catalogArtifactPath'."
    }

    $catalogBeforeNoOpBuild = Get-Item -LiteralPath $catalogArtifactPath
    $catalogHashBeforeNoOpBuild = (Get-FileHash -LiteralPath $catalogArtifactPath -Algorithm SHA256).Hash
    $catalogTimestampBeforeNoOpBuild = $catalogBeforeNoOpBuild.LastWriteTimeUtc

    Invoke-LoggedCommand -Name 'no-op-solution-build' -Command {
        dotnet build (Join-Path $snapshotRoot 'DataGen.slnx') -c Release -v minimal -m:8 @buildProperties
    }

    $catalogHashAfterNoOpBuild = (Get-FileHash -LiteralPath $catalogArtifactPath -Algorithm SHA256).Hash
    $catalogTimestampAfterNoOpBuild = (Get-Item -LiteralPath $catalogArtifactPath).LastWriteTimeUtc
    if ($catalogHashAfterNoOpBuild -ne $catalogHashBeforeNoOpBuild -or $catalogTimestampAfterNoOpBuild -ne $catalogTimestampBeforeNoOpBuild) {
        throw 'An unchanged catalog source set rewrote the catalog database instead of taking the fingerprint no-op path.'
    }

    $catalogSourceOriginalTimestamp = (Get-Item -LiteralPath $catalogSourceMutationPath).LastWriteTimeUtc
    [IO.File]::AppendAllText($catalogSourceMutationPath, "`r`nV094 Contract Fixture")
    [IO.File]::SetLastWriteTimeUtc($catalogSourceMutationPath, $catalogSourceOriginalTimestamp)
    if ((Get-Item -LiteralPath $catalogSourceMutationPath).LastWriteTimeUtc -ne $catalogSourceOriginalTimestamp) {
        throw 'The preserved-timestamp regression could not restore the catalog source timestamp.'
    }

    Invoke-LoggedCommand -Name 'source-change-rebuild' -Command {
        dotnet build (Join-Path $snapshotRoot 'DataGen.slnx') -c Release -v minimal -m:8 @buildProperties
    }

    $catalogAfterSourceChange = (Get-FileHash -LiteralPath $catalogArtifactPath -Algorithm SHA256).Hash
    if ($catalogAfterSourceChange -eq $catalogHashAfterNoOpBuild) {
        throw 'A preserved-timestamp catalog source change did not invalidate and regenerate the catalog database.'
    }

    if ($TimestampInvalidationRegressionOnly.IsPresent) {
        Write-Host 'Preserved-timestamp catalog invalidation contract passed.' -ForegroundColor Green
        return
    }

    Invoke-LoggedCommand -Name 'isolated-catalog-test' -Command {
        dotnet test (Join-Path $snapshotRoot 'DataGen.slnx') -c Release -v minimal -m:1 --no-restore --filter 'FullyQualifiedName~TestEnvironmentPathsTests.Catalogs_AreResolvedFromTheIsolatedTestOutput' @buildProperties
    }

    Invoke-LoggedCommand -Name 'package-module' -Command {
        & (Join-Path $snapshotRoot 'scripts\package-module.ps1') -Configuration Release -OutputRoot (Join-Path $outputRoot 'module')
    }

    $packagedCatalogPath = Join-Path $outputRoot 'module\publish\SyntheticEnterprise.PowerShell\catalogs\catalogs.sqlite'
    if (-not (Test-Path -LiteralPath $packagedCatalogPath -PathType Leaf)) {
        throw "The packaged module does not contain '$packagedCatalogPath'."
    }

    $versionedManifestPath = Join-Path $outputRoot 'module\SyntheticEnterprise.PowerShell\0.11.0\SyntheticEnterprise.PowerShell.psd1'
    if (-not (Test-Path -LiteralPath $versionedManifestPath -PathType Leaf)) {
        throw "The default package version did not produce '$versionedManifestPath'."
    }

    $cleanupSafetyRoot = Join-Path $outputRoot 'cleanup-safety'
    $sourceSentinelPath = Join-Path $snapshotRoot 'source-root-sentinel.txt'
    New-Item -ItemType Directory -Path $cleanupSafetyRoot -Force | Out-Null
    Set-Content -LiteralPath $sourceSentinelPath -Value 'source root must survive' -NoNewline
    Assert-UnsafeOutputRootIsRejected -Name 'source-root' -ValidationSourceRoot $snapshotRoot -UnsafeOutputRoot $snapshotRoot -SentinelPaths @($sourceSentinelPath)

    $ancestorSafetyRoot = Join-Path $cleanupSafetyRoot 'ancestor-root'
    $ancestorSourceRoot = Join-Path $ancestorSafetyRoot 'source'
    $ancestorSentinelPath = Join-Path $ancestorSafetyRoot 'ancestor-root-sentinel.txt'
    New-Item -ItemType Directory -Path $ancestorSourceRoot -Force | Out-Null
    Set-Content -LiteralPath $ancestorSentinelPath -Value 'ancestor must survive' -NoNewline
    Assert-UnsafeOutputRootIsRejected -Name 'source-ancestor' -ValidationSourceRoot $ancestorSourceRoot -UnsafeOutputRoot $ancestorSafetyRoot -SentinelPaths @($ancestorSentinelPath, $ancestorSourceRoot)

    $reparseSafetyRoot = Join-Path $cleanupSafetyRoot 'reparse-root'
    $reparseTargetRoot = Join-Path $reparseSafetyRoot 'target'
    $reparseOutputRoot = Join-Path $reparseSafetyRoot 'output-link'
    $reparseSentinelPath = Join-Path $reparseTargetRoot 'reparse-target-sentinel.txt'
    New-Item -ItemType Directory -Path $reparseTargetRoot -Force | Out-Null
    Set-Content -LiteralPath $reparseSentinelPath -Value 'reparse target must survive' -NoNewline
    New-Item -ItemType Junction -Path $reparseOutputRoot -Target $reparseTargetRoot | Out-Null
    Assert-UnsafeOutputRootIsRejected -Name 'reparse-point' -ValidationSourceRoot $snapshotRoot -UnsafeOutputRoot $reparseOutputRoot -SentinelPaths @($reparseOutputRoot, $reparseSentinelPath)
}
finally {
    [Environment]::SetEnvironmentVariable('DATAGEN_REPOSITORY_ROOT', $originalRepositoryRoot)
}

$global:LASTEXITCODE = 0
Write-Host "Clean-checkout catalog contract passed. Catalog: $catalogArtifactPath" -ForegroundColor Green
