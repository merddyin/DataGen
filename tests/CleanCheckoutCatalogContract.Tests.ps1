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
    [switch]$ReleaseVersionContractOnly,

    [Parameter()]
    [switch]$ReleaseArtifactContractOnly,

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
    if ($ReleaseVersionContractOnly.IsPresent) {
        $childArguments += '-ReleaseVersionContractOnly'
    }
    if ($ReleaseArtifactContractOnly.IsPresent) {
        $childArguments += '-ReleaseArtifactContractOnly'
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
        'RELEASE_REF_NAME: ${{ github.ref_name }}'
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
        '$env:RELEASE_REF_NAME'
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

    $probeRoot = Join-Path $ContractOutputRoot 'release-workflow-version-probes'
    New-Item -ItemType Directory -Path $probeRoot -Force | Out-Null
    $probeScriptPath = Join-Path $probeRoot 'resolve-release-version.ps1'
    Set-Content -LiteralPath $probeScriptPath -Value $runSource -Encoding utf8 -NoNewline

    $cases = @(
        [pscustomobject]@{
            Name = 'workflow-dispatch-quoted-semicolon-comment'
            EventName = 'workflow_dispatch'
            InputVersion = "0.10.0';`$version='0.10.0';#"
            RefName = ''
        },
        [pscustomobject]@{
            Name = 'pushed-tag-quoted-semicolon-comment'
            EventName = 'push'
            InputVersion = ''
            RefName = "v0.10.0';`$version='0.10.0';#"
        }
    )

    foreach ($case in $cases) {
        $caseRoot = Join-Path $probeRoot $case.Name
        New-Item -ItemType Directory -Path $caseRoot -Force | Out-Null
        $githubOutputPath = Join-Path $caseRoot 'github-output.txt'
        $previousEventName = [Environment]::GetEnvironmentVariable('RELEASE_EVENT_NAME', 'Process')
        $previousInputVersion = [Environment]::GetEnvironmentVariable('RELEASE_INPUT_VERSION', 'Process')
        $previousRefName = [Environment]::GetEnvironmentVariable('RELEASE_REF_NAME', 'Process')
        $previousGithubOutput = [Environment]::GetEnvironmentVariable('GITHUB_OUTPUT', 'Process')

        try {
            $env:RELEASE_EVENT_NAME = $case.EventName
            $env:RELEASE_INPUT_VERSION = $case.InputVersion
            $env:RELEASE_REF_NAME = $case.RefName
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
            $env:RELEASE_REF_NAME = $previousRefName
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

function Assert-ReleaseVersionContract {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot
    )

    $expectedVersion = '0.10.0'
    $expectedAssemblyVersion = '0.10.0.0'
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

    $versionedManifestPath = Join-Path $outputRoot 'module\SyntheticEnterprise.PowerShell\0.10.0\SyntheticEnterprise.PowerShell.psd1'
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
