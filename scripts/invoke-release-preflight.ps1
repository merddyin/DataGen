[CmdletBinding()]
param(
    [Parameter()]
    [string]$RepositoryRoot = (Join-Path $PSScriptRoot '..'),

    [Parameter()]
    [string]$OutputRoot = (Join-Path ([IO.Path]::GetTempPath()) "DataGenReleasePreflight-$([Guid]::NewGuid().ToString('N'))"),

    [Parameter()]
    [switch]$RequireWindowsPublisherMetadataCrossFilesystemEvidence,

    [Parameter()]
    [switch]$CreateReleaseAttestation,

    [Parameter()]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string]$SigningCertificateThumbprint,

    [Parameter()]
    [string]$PublicCertificatePath = (Join-Path $PSScriptRoot '..\release-trust\datagen-release-preflight-attestation.cer'),

    [Parameter()]
    [string]$GitPath = 'C:\Program Files\Git\cmd\git.exe',

    [Parameter()]
    [string]$TarPath = 'C:\Windows\System32\tar.exe',

    [Parameter()]
    [string]$DotNetPath = 'C:\Program Files\dotnet\dotnet.exe'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Test-PathContains {
    param(
        [Parameter(Mandatory)][string]$ParentPath,
        [Parameter(Mandatory)][string]$ChildPath
    )

    $relativePath = [IO.Path]::GetRelativePath($ParentPath, $ChildPath)
    return $relativePath -eq '.' -or
        (-not [IO.Path]::IsPathRooted($relativePath) -and
            -not $relativePath.StartsWith("..$([IO.Path]::DirectorySeparatorChar)") -and
            $relativePath -ne '..')
}

function Assert-SafeOutputRoot {
    param(
        [Parameter(Mandatory)][string]$SourceRootPath,
        [Parameter(Mandatory)][string]$OutputRootPath
    )

    $source = [IO.Path]::GetFullPath($SourceRootPath).TrimEnd('\', '/')
    $output = [IO.Path]::GetFullPath($OutputRootPath).TrimEnd('\', '/')
    $filesystemRoot = [IO.Path]::GetPathRoot($output).TrimEnd('\', '/')
    if ([string]::IsNullOrWhiteSpace($output) -or $output -eq $filesystemRoot) {
        throw "Unsafe preflight OutputRoot '$OutputRootPath': filesystem roots are not allowed."
    }
    if ((Test-PathContains -ParentPath $source -ChildPath $output) -or
        (Test-PathContains -ParentPath $output -ChildPath $source)) {
        throw "Unsafe preflight OutputRoot '$OutputRootPath': it overlaps RepositoryRoot '$SourceRootPath'."
    }

    $currentPath = $output
    while ($true) {
        if (Test-Path -LiteralPath $currentPath) {
            $item = Get-Item -LiteralPath $currentPath -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Unsafe preflight OutputRoot '$OutputRootPath': '$currentPath' is or is beneath a reparse point."
            }
        }
        $parent = [IO.Directory]::GetParent($currentPath)
        if ($null -eq $parent) {
            break
        }
        $currentPath = $parent.FullName
    }
}

function Invoke-GitQuery {
    param([Parameter(Mandatory)][string[]]$Arguments)

    $output = & $GitPath -C $repositoryRoot @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed: $($output | Out-String)"
    }
    return ($output | Out-String).Trim()
}

function Get-ReleaseVersionFromBuildProperties {
    param([Parameter(Mandatory)][string]$Content)

    [xml]$buildProperties = $Content
    $versionNodes = @($buildProperties.SelectNodes('/Project/PropertyGroup/Version'))
    $versions = @($versionNodes |
        ForEach-Object { $_.InnerText.Trim() } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -Unique)
    if ($versions.Count -eq 0) {
        throw 'Directory.Build.props does not declare a release version.'
    }
    if ($versions.Count -ne 1) {
        throw "Directory.Build.props declares conflicting release versions: $($versions -join ', ')."
    }
    if ($versions[0] -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') {
        throw "Directory.Build.props Version '$($versions[0])' is not a valid release version."
    }
    return $versions[0]
}

function Get-ReleaseSourceState {
    param([Parameter()][switch]$RequireCleanMain)

    $branch = Invoke-GitQuery -Arguments @('branch', '--show-current')
    if ($RequireCleanMain.IsPresent -and $branch -cne 'main') {
        throw "Release attestation must be prepared from the main branch; current branch is '$branch'."
    }

    $status = Invoke-GitQuery -Arguments @('status', '--porcelain=v1', '--untracked-files=all')
    if ($RequireCleanMain.IsPresent -and -not [string]::IsNullOrWhiteSpace($status)) {
        throw 'Release attestation requires a clean working tree so the evidence is bound to one committed source state.'
    }

    $sourceCommit = Invoke-GitQuery -Arguments @('rev-parse', '--verify', 'HEAD^{commit}')
    $sourceTreeId = Invoke-GitQuery -Arguments @('rev-parse', "$sourceCommit`^{tree}")
    if ($sourceCommit -notmatch '^[0-9a-fA-F]{40}$' -or $sourceTreeId -notmatch '^[0-9a-fA-F]{40}$') {
        throw 'Could not resolve a full committed source SHA and tree id.'
    }

    $buildPropertiesContent = Invoke-GitQuery -Arguments @('show', "${sourceCommit}:Directory.Build.props")
    return [pscustomobject]@{
        Branch = $branch
        SourceCommit = $sourceCommit.ToLowerInvariant()
        SourceTreeId = $sourceTreeId.ToLowerInvariant()
        Version = Get-ReleaseVersionFromBuildProperties -Content $buildPropertiesContent
        Status = $status
    }
}

function New-SourceManifest {
    param(
        [Parameter(Mandatory)][string]$SnapshotRoot,
        [Parameter(Mandatory)][string]$DestinationPath,
        [Parameter(Mandatory)]$SourceState
    )

    $reparsePoints = @(Get-ChildItem -LiteralPath $SnapshotRoot -Recurse -Force |
        Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 })
    if ($reparsePoints.Count -gt 0) {
        throw "Committed source snapshot contains a reparse point at '$($reparsePoints[0].FullName)'."
    }

    $relativePaths = @(Get-ChildItem -LiteralPath $SnapshotRoot -Recurse -File -Force |
        ForEach-Object { [IO.Path]::GetRelativePath($SnapshotRoot, $_.FullName).Replace('\', '/') })
    [Array]::Sort($relativePaths, [StringComparer]::Ordinal)
    $caseCollisions = @($relativePaths | Group-Object { $_.ToLowerInvariant() } | Where-Object Count -gt 1)
    if ($caseCollisions.Count -gt 0) {
        throw "Committed source snapshot contains case-colliding paths: $($caseCollisions[0].Name)."
    }

    $entries = foreach ($relativePath in $relativePaths) {
        $filePath = Join-Path $SnapshotRoot $relativePath
        $file = Get-Item -LiteralPath $filePath -Force
        [ordered]@{
            Path = $relativePath
            Length = $file.Length
            Sha256 = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    $manifest = [ordered]@{
        Schema = 'datagen-release-source-manifest-v1'
        SourceCommit = $SourceState.SourceCommit
        SourceTreeId = $SourceState.SourceTreeId
        Files = @($entries)
    } | ConvertTo-Json -Depth 5
    [IO.File]::WriteAllText($DestinationPath, "$manifest`n", [Text.UTF8Encoding]::new($false))
    return (Get-FileHash -LiteralPath $DestinationPath -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Assert-SourceArtifactsUnchanged {
    param(
        [Parameter(Mandatory)][string]$ArchivePath,
        [Parameter(Mandatory)][string]$ExpectedArchiveHash,
        [Parameter(Mandatory)][string]$SnapshotRoot,
        [Parameter(Mandatory)][string]$VerificationManifestPath,
        [Parameter(Mandatory)][string]$ExpectedManifestHash,
        [Parameter(Mandatory)]$SourceState
    )

    $archiveHash = (Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($archiveHash -cne $ExpectedArchiveHash) {
        throw 'Committed source archive changed while release preflight executed.'
    }
    $manifestHash = New-SourceManifest -SnapshotRoot $SnapshotRoot -DestinationPath $VerificationManifestPath -SourceState $SourceState
    if ($manifestHash -cne $ExpectedManifestHash) {
        throw 'Committed source snapshot changed while release preflight executed.'
    }
}

function Assert-LiveSourceStateUnchanged {
    param(
        [Parameter(Mandatory)]$ExpectedState,
        [Parameter(Mandatory)][switch]$RequireCleanMain
    )

    $actualState = Get-ReleaseSourceState -RequireCleanMain:$RequireCleanMain
    if ($actualState.Branch -cne $ExpectedState.Branch -or
        $actualState.SourceCommit -cne $ExpectedState.SourceCommit -or
        $actualState.SourceTreeId -cne $ExpectedState.SourceTreeId -or
        $actualState.Version -cne $ExpectedState.Version) {
        throw 'The live repository branch, source SHA, tree id, or version changed while preflight was running.'
    }
}

$repositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$outputRoot = [IO.Path]::GetFullPath($OutputRoot)
Assert-SafeOutputRoot -SourceRootPath $repositoryRoot -OutputRootPath $outputRoot
if (-not (Test-Path -LiteralPath $GitPath -PathType Leaf)) {
    throw "Git executable was not found at '$GitPath'."
}
if (-not (Test-Path -LiteralPath $TarPath -PathType Leaf)) {
    throw "Tar executable was not found at '$TarPath'."
}
if (Test-Path -LiteralPath $outputRoot) {
    $existingItems = @(Get-ChildItem -LiteralPath $outputRoot -Force)
    if ($existingItems.Count -gt 0) {
        throw "Release preflight OutputRoot '$outputRoot' must be empty."
    }
}
else {
    New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
}

$expectedPublicCertificatePath = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'release-trust\datagen-release-preflight-attestation.cer'))
$providedPublicCertificatePath = [IO.Path]::GetFullPath($PublicCertificatePath)
if ($providedPublicCertificatePath -ine $expectedPublicCertificatePath) {
    throw "Release preflight must use the tracked public certificate at '$expectedPublicCertificatePath'."
}

$sourceState = Get-ReleaseSourceState -RequireCleanMain:$CreateReleaseAttestation
$archivePath = Join-Path $outputRoot 'source-archive.tar'
$snapshotRoot = Join-Path $outputRoot 'source-snapshot'
$manifestPath = Join-Path $outputRoot 'source-manifest.json'
$postContractManifestPath = Join-Path $outputRoot 'source-manifest-post-contract.json'
$finalManifestPath = Join-Path $outputRoot 'source-manifest-final.json'
New-Item -ItemType Directory -Path $snapshotRoot -Force | Out-Null

& $GitPath -C $repositoryRoot archive '--format=tar' "--output=$archivePath" $sourceState.SourceCommit
if ($LASTEXITCODE -ne 0) {
    throw 'Could not create the committed source archive for release preflight.'
}
$archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()

& $TarPath -xf $archivePath -C $snapshotRoot
if ($LASTEXITCODE -ne 0) {
    throw 'Could not extract the committed source archive into the isolated preflight snapshot.'
}
$manifestHash = New-SourceManifest -SnapshotRoot $snapshotRoot -DestinationPath $manifestPath -SourceState $sourceState
$contractScript = Join-Path $snapshotRoot 'tests\CleanCheckoutCatalogContract.Tests.ps1'
if (-not (Test-Path -LiteralPath $contractScript -PathType Leaf)) {
    throw "Release preflight contract script was not found in committed snapshot '$contractScript'."
}

function Invoke-ReleasePreflightContract {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$SwitchName
    )

    $contractOutputRoot = Join-Path $outputRoot $Name
    $contractParameters = @{$SwitchName = $true}
    & $contractScript -SourceRoot $snapshotRoot -OutputRoot $contractOutputRoot -DotNetPath $DotNetPath @contractParameters
    if ($LASTEXITCODE -ne 0) {
        throw "Release preflight '$Name' failed with exit code $LASTEXITCODE."
    }
}

Invoke-ReleasePreflightContract -Name 'release-version' -SwitchName 'ReleaseVersionContractOnly'
Invoke-ReleasePreflightContract -Name 'windows-publisher-metadata-portable' -SwitchName 'WindowsPublisherMetadataPortableOnly'

if ($RequireWindowsPublisherMetadataCrossFilesystemEvidence.IsPresent -or $CreateReleaseAttestation.IsPresent) {
    Invoke-ReleasePreflightContract -Name 'windows-publisher-metadata-cross-filesystem' -SwitchName 'WindowsPublisherMetadataRegressionOnly'
    $crossFilesystemStatus = 'passed'
}
else {
    $crossFilesystemStatus = 'not-run (requires the prepared NTFS D: and ReFS G: workstation volumes)'
}

Assert-SourceArtifactsUnchanged `
    -ArchivePath $archivePath `
    -ExpectedArchiveHash $archiveHash `
    -SnapshotRoot $snapshotRoot `
    -VerificationManifestPath $postContractManifestPath `
    -ExpectedManifestHash $manifestHash `
    -SourceState $sourceState
Assert-LiveSourceStateUnchanged -ExpectedState $sourceState -RequireCleanMain:$CreateReleaseAttestation

$summaryPath = Join-Path $outputRoot 'release-preflight-summary.txt'
@(
    'DataGen release preflight completed.',
    "Source commit: $($sourceState.SourceCommit)",
    "Source tree: $($sourceState.SourceTreeId)",
    "Source archive SHA-256: $archiveHash",
    "Source manifest SHA-256: $manifestHash",
    'Release version contract: passed',
    'Windows publisher metadata portable contract: passed',
    "Windows publisher metadata cross-filesystem evidence: $crossFilesystemStatus"
) | Set-Content -LiteralPath $summaryPath

if ($CreateReleaseAttestation.IsPresent) {
    $completedAtUtc = [DateTimeOffset]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
    $evidencePath = Join-Path $outputRoot 'release-preflight-evidence.json'
    $attestationPath = Join-Path $outputRoot 'release-preflight-attestation.txt'
    $volumeEvidence = [ordered]@{}
    foreach ($driveLetter in @('D', 'G')) {
        $volume = Get-Volume -DriveLetter $driveLetter -ErrorAction Stop
        $volumeEvidence[$driveLetter] = [ordered]@{
            FileSystem = [string]$volume.FileSystem
            DriveType = [string]$volume.DriveType
        }
    }

    [ordered]@{
        Schema = 'datagen-release-preflight-evidence-v2'
        Version = $sourceState.Version
        SourceCommit = $sourceState.SourceCommit
        SourceTreeId = $sourceState.SourceTreeId
        SourceArchiveSha256 = $archiveHash
        SourceManifestSha256 = $manifestHash
        Branch = $sourceState.Branch
        CompletedAtUtc = $completedAtUtc
        Workstation = $env:COMPUTERNAME
        Volumes = $volumeEvidence
        Contracts = [ordered]@{
            ReleaseVersion = 'passed'
            WindowsPublisherMetadataPortable = 'passed'
            WindowsPublisherMetadataCrossFilesystem = 'passed'
        }
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $evidencePath

    $attestationWriter = Join-Path $snapshotRoot 'scripts\new-release-preflight-attestation.ps1'
    $snapshotPublicCertificatePath = Join-Path $snapshotRoot 'release-trust\datagen-release-preflight-attestation.cer'
    $attestation = & $attestationWriter `
        -EvidencePath $evidencePath `
        -Version $sourceState.Version `
        -SourceCommit $sourceState.SourceCommit `
        -CompletedAtUtc $completedAtUtc `
        -SigningCertificateThumbprint $SigningCertificateThumbprint `
        -PublicCertificatePath $snapshotPublicCertificatePath

    Assert-SourceArtifactsUnchanged `
        -ArchivePath $archivePath `
        -ExpectedArchiveHash $archiveHash `
        -SnapshotRoot $snapshotRoot `
        -VerificationManifestPath $finalManifestPath `
        -ExpectedManifestHash $manifestHash `
        -SourceState $sourceState
    Assert-LiveSourceStateUnchanged -ExpectedState $sourceState -RequireCleanMain
    Set-Content -LiteralPath $attestationPath -Value $attestation

    Add-Content -LiteralPath $summaryPath -Value @(
        "Release version: $($sourceState.Version)",
        "Evidence: $evidencePath",
        "Workflow attestation: $attestationPath"
    )
}

Get-Content -LiteralPath $summaryPath
