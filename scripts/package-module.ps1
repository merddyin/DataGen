[CmdletBinding()]
param(
    [Parameter()]
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string]$Version = '0.11.0',

    [Parameter()]
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [Parameter()]
    [string]$ProjectPath = 'src/SyntheticEnterprise.PowerShell/SyntheticEnterprise.PowerShell.csproj',

    [Parameter()]
    [string]$OutputRoot = 'artifacts/module',

    [Parameter()]
    [string]$ModuleName = 'SyntheticEnterprise.PowerShell',

    [Parameter()]
    [string]$PowerShellVersion = '7.4'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$outputRootWasExplicitlySpecified = $PSBoundParameters.ContainsKey('OutputRoot')

function ConvertTo-NormalizedFullPath {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $fullPath = [IO.Path]::GetFullPath($Path)
    $pathRoot = [IO.Path]::GetPathRoot($fullPath)
    if ($fullPath.Length -gt $pathRoot.Length) {
        return $fullPath.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    }

    return $pathRoot
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

function Assert-PathHasNoReparsePoints {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$UnsafePathMessage
    )

    $currentPath = ConvertTo-NormalizedFullPath -Path $Path
    while ($true) {
        if (Test-Path -LiteralPath $currentPath) {
            $item = Get-Item -LiteralPath $currentPath -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "$UnsafePathMessage '$currentPath' is or is beneath a reparse point."
            }
        }

        $parentPath = [IO.Directory]::GetParent($currentPath)
        if ($null -eq $parentPath) {
            break
        }

        $currentPath = $parentPath.FullName
    }
}

function Assert-SafePackageOutputRoot {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot,

        [Parameter(Mandatory)]
        [string]$RequestedOutputRoot,

        [Parameter(Mandatory)]
        [string]$ApprovedRepositoryOutputRoot,

        [Parameter(Mandatory)]
        [bool]$AllowApprovedRepositoryOutputRoot
    )

    $repositoryRoot = ConvertTo-NormalizedFullPath -Path $RepositoryRoot
    $outputRootPath = ConvertTo-NormalizedFullPath -Path $RequestedOutputRoot
    $approvedOutputRoot = ConvertTo-NormalizedFullPath -Path $ApprovedRepositoryOutputRoot
    $filesystemRoot = [IO.Path]::GetPathRoot($outputRootPath)

    if ($outputRootPath.Equals($filesystemRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe package OutputRoot '$RequestedOutputRoot': filesystem roots cannot be used as package staging scopes."
    }

    $overlapsRepository = (Test-PathContains -ParentPath $repositoryRoot -ChildPath $outputRootPath) -or
        (Test-PathContains -ParentPath $outputRootPath -ChildPath $repositoryRoot)
    $isApprovedDefault = $AllowApprovedRepositoryOutputRoot -and $outputRootPath.Equals($approvedOutputRoot, [StringComparison]::OrdinalIgnoreCase)
    if ($overlapsRepository -and -not $isApprovedDefault) {
        throw "Unsafe package OutputRoot '$RequestedOutputRoot': it overlaps repository root '$repositoryRoot'."
    }

    Assert-PathHasNoReparsePoints -Path $outputRootPath -UnsafePathMessage "Unsafe package OutputRoot '$RequestedOutputRoot':"
    return $outputRootPath
}

function Assert-SafeScopedCleanupPath {
    param(
        [Parameter(Mandatory)]
        [string]$ScopeRoot,

        [Parameter(Mandatory)]
        [string]$CandidatePath
    )

    $scopeRoot = ConvertTo-NormalizedFullPath -Path $ScopeRoot
    $candidatePath = ConvertTo-NormalizedFullPath -Path $CandidatePath
    if ($candidatePath.Equals($scopeRoot, [StringComparison]::OrdinalIgnoreCase) -or -not (Test-PathContains -ParentPath $scopeRoot -ChildPath $candidatePath)) {
        throw "Unsafe package cleanup path '$CandidatePath': it is not a strict descendant of '$scopeRoot'."
    }

    Assert-PathHasNoReparsePoints -Path $candidatePath -UnsafePathMessage "Unsafe package cleanup path '$CandidatePath':"
    return $candidatePath
}

function Assert-SafePackageStagingPath {
    param(
        [Parameter(Mandatory)]
        [string]$ScopeRoot,

        [Parameter(Mandatory)]
        [string]$CandidatePath,

        [Parameter(Mandatory)]
        [string]$Operation
    )

    $scopeRoot = ConvertTo-NormalizedFullPath -Path $ScopeRoot
    $candidatePath = ConvertTo-NormalizedFullPath -Path $CandidatePath
    if ($candidatePath.Equals($scopeRoot, [StringComparison]::OrdinalIgnoreCase) -or -not (Test-PathContains -ParentPath $scopeRoot -ChildPath $candidatePath)) {
        throw "Unsafe package $Operation path '$CandidatePath': it is not a strict descendant of '$scopeRoot'."
    }

    Assert-PathHasNoReparsePoints -Path $candidatePath -UnsafePathMessage "Unsafe package $Operation path '$CandidatePath':"
    return $candidatePath
}

function Assert-SafePackageStagingTree {
    param(
        [Parameter(Mandatory)]
        [string]$ScopeRoot,

        [Parameter(Mandatory)]
        [string]$Operation
    )

    $scopeRoot = ConvertTo-NormalizedFullPath -Path $ScopeRoot
    Assert-PathHasNoReparsePoints -Path $scopeRoot -UnsafePathMessage "Unsafe package $Operation path '$scopeRoot':"
    if (-not (Test-Path -LiteralPath $scopeRoot)) {
        return $scopeRoot
    }

    foreach ($item in Get-ChildItem -LiteralPath $scopeRoot -Force -Recurse) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Unsafe package $Operation path '$($item.FullName)': a descendant reparse point is not allowed in the staging scope."
        }
    }

    return $scopeRoot
}

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$releaseVersionAssertionPath = Join-Path $PSScriptRoot 'assert-release-version.ps1'
& $releaseVersionAssertionPath -Version $Version -RepositoryRoot $repoRoot
$projectFullPath = [IO.Path]::GetFullPath((Join-Path $repoRoot $ProjectPath))
$requestedModuleStageRoot = if ([System.IO.Path]::IsPathRooted($OutputRoot)) {
    $OutputRoot
}
else {
    Join-Path $repoRoot $OutputRoot
}
$approvedRepositoryOutputRoot = Join-Path $repoRoot 'artifacts\module'
$moduleStageRoot = Assert-SafePackageOutputRoot `
    -RepositoryRoot $repoRoot `
    -RequestedOutputRoot $requestedModuleStageRoot `
    -ApprovedRepositoryOutputRoot $approvedRepositoryOutputRoot `
    -AllowApprovedRepositoryOutputRoot (-not $outputRootWasExplicitlySpecified)
$moduleBuildArtifactsRoot = Join-Path $moduleStageRoot 'build'
$catalogArtifactPath = Join-Path $moduleStageRoot 'catalog\catalogs.sqlite'
$catalogFingerprintPath = "$catalogArtifactPath.inputs.sha256"
$moduleStagePath = Join-Path $moduleStageRoot $ModuleName
$versionedStagePath = Join-Path $moduleStagePath $Version
$publishStagePath = Join-Path (Join-Path $moduleStageRoot 'publish') $ModuleName
$zipPath = Join-Path $moduleStageRoot "$ModuleName-$Version.zip"
$buildOutput = Join-Path (Join-Path (Join-Path $moduleBuildArtifactsRoot 'bin') $ModuleName) $Configuration.ToLowerInvariant()
$moduleDll = Join-Path $versionedStagePath "$ModuleName.dll"
$manifestPath = Join-Path $versionedStagePath "$ModuleName.psd1"
$transientCleanupPaths = @(
    (Join-Path $versionedStagePath 'ref'),
    (Join-Path $versionedStagePath 'refint')
)

# Validate every existing derived path before project or build work can write through it.
$moduleBuildArtifactsRoot = Assert-SafePackageStagingPath -ScopeRoot $moduleStageRoot -CandidatePath $moduleBuildArtifactsRoot -Operation 'build'
$buildOutput = Assert-SafePackageStagingPath -ScopeRoot $moduleStageRoot -CandidatePath $buildOutput -Operation 'build output'
$catalogArtifactPath = Assert-SafePackageStagingPath -ScopeRoot $moduleStageRoot -CandidatePath $catalogArtifactPath -Operation 'catalog'
$catalogFingerprintPath = Assert-SafePackageStagingPath -ScopeRoot $moduleStageRoot -CandidatePath $catalogFingerprintPath -Operation 'catalog receipt'
$moduleStagePath = Assert-SafePackageStagingPath -ScopeRoot $moduleStageRoot -CandidatePath $moduleStagePath -Operation 'module'
$versionedStagePath = Assert-SafePackageStagingPath -ScopeRoot $moduleStageRoot -CandidatePath $versionedStagePath -Operation 'module version'
$publishStagePath = Assert-SafePackageStagingPath -ScopeRoot $moduleStageRoot -CandidatePath $publishStagePath -Operation 'publish'
$zipPath = Assert-SafePackageStagingPath -ScopeRoot $moduleStageRoot -CandidatePath $zipPath -Operation 'zip'
$moduleDll = Assert-SafePackageStagingPath -ScopeRoot $moduleStageRoot -CandidatePath $moduleDll -Operation 'module binary'
$manifestPath = Assert-SafePackageStagingPath -ScopeRoot $moduleStageRoot -CandidatePath $manifestPath -Operation 'manifest'
$transientCleanupPaths = @($transientCleanupPaths | ForEach-Object {
    Assert-SafePackageStagingPath -ScopeRoot $moduleStageRoot -CandidatePath $_ -Operation 'transient cleanup'
})
Assert-SafePackageStagingTree -ScopeRoot $moduleStageRoot -Operation 'staging'

if (-not (Test-Path $projectFullPath)) {
    throw "Module project not found at '$projectFullPath'."
}

Write-Host "Building $ModuleName ($Configuration)..." -ForegroundColor Cyan
Assert-SafePackageStagingTree -ScopeRoot $moduleStageRoot -Operation 'build'
dotnet build $projectFullPath -c $Configuration -v minimal `
    "/p:ArtifactsPath=$moduleBuildArtifactsRoot" `
    '/p:UseArtifactsOutput=true' `
    "/p:DataGenCatalogArtifactPath=$catalogArtifactPath"
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE."
}

Assert-SafePackageStagingTree -ScopeRoot $moduleStageRoot -Operation 'post-build staging'
$buildOutput = Assert-SafePackageStagingPath -ScopeRoot $moduleStageRoot -CandidatePath $buildOutput -Operation 'build output'

if (-not (Test-Path $buildOutput)) {
    throw "Expected build output was not found at '$buildOutput'."
}

$versionedStagePath = Assert-SafeScopedCleanupPath -ScopeRoot $moduleStageRoot -CandidatePath $versionedStagePath
$publishStagePath = Assert-SafeScopedCleanupPath -ScopeRoot $moduleStageRoot -CandidatePath $publishStagePath
$zipPath = Assert-SafeScopedCleanupPath -ScopeRoot $moduleStageRoot -CandidatePath $zipPath

Assert-SafePackageStagingTree -ScopeRoot $moduleStageRoot -Operation 'cleanup'
if (Test-Path $versionedStagePath) {
    Remove-Item -LiteralPath $versionedStagePath -Recurse -Force
}

if (Test-Path $publishStagePath) {
    Remove-Item -LiteralPath $publishStagePath -Recurse -Force
}

New-Item -ItemType Directory -Path $versionedStagePath -Force | Out-Null

Write-Host "Staging module files..." -ForegroundColor Cyan
Assert-SafePackageStagingTree -ScopeRoot $moduleStageRoot -Operation 'module staging'
$buildOutput = Assert-SafePackageStagingPath -ScopeRoot $moduleStageRoot -CandidatePath $buildOutput -Operation 'build output'
$versionedStagePath = Assert-SafePackageStagingPath -ScopeRoot $moduleStageRoot -CandidatePath $versionedStagePath -Operation 'module version'
Copy-Item (Join-Path $buildOutput '*') $versionedStagePath -Recurse -Force

foreach ($fullPath in $transientCleanupPaths) {
    $fullPath = Assert-SafeScopedCleanupPath -ScopeRoot $moduleStageRoot -CandidatePath $fullPath
    if (Test-Path $fullPath) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
}

$debugSymbolArtifacts = Get-ChildItem -LiteralPath $versionedStagePath -File -Recurse |
    Where-Object { $_.Extension -in @('.pdb', '.mdb', '.dbg') }
foreach ($debugSymbolArtifact in $debugSymbolArtifacts) {
    Remove-Item -LiteralPath $debugSymbolArtifact.FullName -Force
}

$moduleDll = Assert-SafePackageStagingPath -ScopeRoot $moduleStageRoot -CandidatePath $moduleDll -Operation 'module binary'
if (-not (Test-Path $moduleDll)) {
    throw "Expected module binary was not found at '$moduleDll'."
}

Write-Host "Discovering exported cmdlets..." -ForegroundColor Cyan
$importedModule = Import-Module $moduleDll -Force -PassThru
$cmdletsToExport = Get-Command -Module $importedModule.Name |
    Where-Object CommandType -eq 'Cmdlet' |
    Select-Object -ExpandProperty Name |
    Sort-Object -Unique

if (-not $cmdletsToExport) {
    throw "No cmdlets were discovered for module '$($importedModule.Name)'."
}

$manifestPath = Assert-SafePackageStagingPath -ScopeRoot $moduleStageRoot -CandidatePath $manifestPath -Operation 'manifest'
$moduleGuid = '9c0e5d72-daa5-4f5c-9ce7-5d3d5072669f'

Write-Host "Creating module manifest..." -ForegroundColor Cyan
New-ModuleManifest `
    -Path $manifestPath `
    -RootModule "$ModuleName.dll" `
    -ModuleVersion $Version `
    -Guid $moduleGuid `
    -Author 'OpenAI / DataGen contributors' `
    -CompanyName 'DataGen' `
    -Copyright '(c) DataGen contributors' `
    -Description 'Synthetic enterprise data generation platform for labs, demos, exports, and downstream validation.' `
    -CompatiblePSEditions @('Core') `
    -PowerShellVersion $PowerShellVersion `
    -CmdletsToExport $cmdletsToExport `
    -FunctionsToExport @() `
    -AliasesToExport @() `
    -VariablesToExport @() `
    -Tags @('DataGen', 'SyntheticData', 'PowerShell', 'Enterprise') `
    -ProjectUri 'https://github.com/merddyin/DataGen' | Out-Null

$publishStagePath = Assert-SafePackageStagingPath -ScopeRoot $moduleStageRoot -CandidatePath $publishStagePath -Operation 'publish'
Assert-SafePackageStagingTree -ScopeRoot $moduleStageRoot -Operation 'publish staging'
New-Item -ItemType Directory -Path $publishStagePath -Force | Out-Null
foreach ($item in Get-ChildItem -Path $versionedStagePath -Force) {
    $destination = Join-Path $publishStagePath $item.Name
    $destination = Assert-SafePackageStagingPath -ScopeRoot $moduleStageRoot -CandidatePath $destination -Operation 'publish'
    if ($item.PSIsContainer) {
        Copy-Item $item.FullName $destination -Recurse -Force
    }
    else {
        Copy-Item $item.FullName $destination -Force
    }
}

$zipPath = Assert-SafePackageStagingPath -ScopeRoot $moduleStageRoot -CandidatePath $zipPath -Operation 'zip'
if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Write-Host "Compressing module package..." -ForegroundColor Cyan
Assert-SafePackageStagingTree -ScopeRoot $moduleStageRoot -Operation 'archive staging'
$moduleStagePath = Assert-SafePackageStagingPath -ScopeRoot $moduleStageRoot -CandidatePath $moduleStagePath -Operation 'module'
$zipPath = Assert-SafePackageStagingPath -ScopeRoot $moduleStageRoot -CandidatePath $zipPath -Operation 'zip'
Compress-Archive -Path (Join-Path $moduleStagePath '*') -DestinationPath $zipPath -Force

Write-Host ''
Write-Host "Module package created:" -ForegroundColor Green
Write-Host "  Folder: $versionedStagePath"
Write-Host "  Gallery: $publishStagePath"
Write-Host "  Zip:    $zipPath"
