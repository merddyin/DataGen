Set-StrictMode -Version Latest

$script:GenerationProvenanceSidecarName = '.datagen-generation-provenance.json'
$script:SourceInclusionSet = 'git-ls-files-z-v2:tracked+untracked-nonignored;exclude-prefix-ordinal=.beads/;path=/;order=ordinal'
$script:InvocationContractVersion = 'datagen-generation-invocation-v1'
$script:ParentContractVersion = 'datagen-generation-parent-contract-v1'
$script:MaximumInventoryFileCount = 100000
$script:MaximumRelativePathLength = 1024
$script:MaximumInventoryTotalBytes = 4TB

function Assert-FullyQualifiedFilePath {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Label)

    if (-not [IO.Path]::IsPathFullyQualified($Path)) {
        throw "$Label must be a fully qualified path."
    }
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Label '$Path' does not exist."
    }
}

function Invoke-NativeCapture {
    param(
        [Parameter(Mandatory)][string]$ExecutablePath,
        [Parameter(Mandatory)][string[]]$ArgumentList,
        [switch]$BinaryOutput
    )

    Assert-FullyQualifiedFilePath -Path $ExecutablePath -Label 'Evidence executable'
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $ExecutablePath
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $ArgumentList) {
        $startInfo.ArgumentList.Add($argument)
    }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        throw "Unable to start evidence executable '$ExecutablePath'."
    }
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $memory = [IO.MemoryStream]::new()
    try {
        $process.StandardOutput.BaseStream.CopyTo($memory)
        $process.WaitForExit()
        $stderr = $stderrTask.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            throw "Evidence executable '$ExecutablePath' exited $($process.ExitCode): $stderr"
        }
        $bytes = $memory.ToArray()
    }
    finally {
        $memory.Dispose()
        $process.Dispose()
    }

    if ($BinaryOutput) {
        return ,$bytes
    }
    return [Text.UTF8Encoding]::new($false, $true).GetString($bytes).TrimEnd("`r", "`n")
}

function ConvertFrom-GitNullPathBytes {
    [CmdletBinding()]
    param([Parameter(Mandatory)][byte[]]$Bytes)

    $paths = [Collections.Generic.List[string]]::new()
    $start = 0
    for ($index = 0; $index -lt $Bytes.Length; $index++) {
        if ($Bytes[$index] -ne 0) {
            continue
        }
        if ($index -gt $start) {
            $paths.Add([Text.UTF8Encoding]::new($false, $true).GetString($Bytes, $start, $index - $start))
        }
        $start = $index + 1
    }
    if ($start -ne $Bytes.Length) {
        throw 'Git NUL-delimited output was not terminated by NUL.'
    }
    return ,@($paths)
}

function Select-DataGenSourcePath {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string[]]$Path)

    return @($Path | Where-Object { -not $_.StartsWith('.beads/', [StringComparison]::Ordinal) })
}

function Get-Sha256Hex {
    [CmdletBinding()]
    param([Parameter(Mandatory)][byte[]]$Bytes)

    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Bytes)).ToLowerInvariant()
}

function Test-ReparsePoint {
    param([Parameter(Mandatory)][IO.FileSystemInfo]$Item)

    return ($Item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        $Item.LinkType -in @('SymbolicLink', 'Junction')
}

function Assert-NoReparsePointTree {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RootPath)

    $resolvedRoot = [IO.Path]::GetFullPath($RootPath)
    $rootItem = Get-Item -LiteralPath $resolvedRoot -Force
    if (Test-ReparsePoint -Item $rootItem) {
        throw "Inventory root '$resolvedRoot' is a filesystem reparse point."
    }

    foreach ($item in Get-ChildItem -LiteralPath $resolvedRoot -Force -Recurse) {
        if (Test-ReparsePoint -Item $item) {
            $relativePath = [IO.Path]::GetRelativePath($resolvedRoot, $item.FullName).Replace('\', '/')
            throw "Inventory descendant '$relativePath' is a filesystem reparse point."
        }
    }
}

function Assert-NoReparsePointSourcePath {
    param(
        [Parameter(Mandatory)][string]$RootPath,
        [Parameter(Mandatory)][string]$RelativePath
    )

    $resolvedRoot = [IO.Path]::GetFullPath($RootPath)
    $rootItem = Get-Item -LiteralPath $resolvedRoot -Force
    if (Test-ReparsePoint -Item $rootItem) {
        throw "Source root '$resolvedRoot' is a filesystem reparse point."
    }
    $current = $resolvedRoot
    foreach ($segment in $RelativePath.Replace('\', '/').Split('/')) {
        $current = Join-Path $current $segment
        $item = Get-Item -LiteralPath $current -Force
        if (Test-ReparsePoint -Item $item) {
            throw "Included source path '$RelativePath' traverses filesystem reparse point '$segment'."
        }
    }
}

function Get-StableFileIdentity {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)

    $resolvedPath = [IO.Path]::GetFullPath($Path)
    $before = Get-Item -LiteralPath $resolvedPath -Force
    if (Test-ReparsePoint -Item $before) {
        throw "Stable read rejected filesystem reparse point '$resolvedPath'."
    }

    $beforeLength = [long]$before.Length
    $beforeWriteTicks = $before.LastWriteTimeUtc.Ticks
    $stream = $null
    try {
        try {
            $stream = [IO.FileStream]::new(
                $resolvedPath,
                [IO.FileMode]::Open,
                [IO.FileAccess]::Read,
                [IO.FileShare]::Read,
                1MB,
                [IO.FileOptions]::SequentialScan)
        }
        catch {
            throw "Unable to acquire stable read access for '$resolvedPath': $($_.Exception.Message)"
        }

        $streamLengthBefore = $stream.Length
        $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant()
        $streamLengthAfter = $stream.Length
        $during = Get-Item -LiteralPath $resolvedPath -Force
        if ($streamLengthBefore -ne $streamLengthAfter -or
            $beforeLength -ne $streamLengthBefore -or
            [long]$during.Length -ne $streamLengthAfter -or
            $beforeWriteTicks -ne $during.LastWriteTimeUtc.Ticks) {
            throw "Stable read detected concurrent metadata mutation for '$resolvedPath'."
        }
    }
    finally {
        if ($null -ne $stream) {
            $stream.Dispose()
        }
    }

    $after = Get-Item -LiteralPath $resolvedPath -Force
    if ((Test-ReparsePoint -Item $after) -or
        [long]$after.Length -ne $beforeLength -or
        $after.LastWriteTimeUtc.Ticks -ne $beforeWriteTicks) {
        throw "Stable read detected post-read metadata mutation for '$resolvedPath'."
    }

    return [pscustomobject][ordered]@{
        sizeBytes = $beforeLength
        sha256 = $hash
    }
}

function Get-FileSha256Hex {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)

    return (Get-StableFileIdentity -Path $Path).sha256
}

function Read-StableJsonEvidenceFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [int]$MaximumBytes = 4MB
    )

    $resolvedPath = [IO.Path]::GetFullPath($Path)
    $before = Get-Item -LiteralPath $resolvedPath -Force
    if (Test-ReparsePoint -Item $before) {
        throw "Stable JSON read rejected filesystem reparse point '$resolvedPath'."
    }
    if ($before.Length -gt $MaximumBytes) {
        throw "Stable JSON read rejected '$resolvedPath' because it exceeds $MaximumBytes bytes."
    }

    $beforeLength = [long]$before.Length
    $beforeWriteTicks = $before.LastWriteTimeUtc.Ticks
    $stream = $null
    try {
        try {
            $stream = [IO.FileStream]::new(
                $resolvedPath,
                [IO.FileMode]::Open,
                [IO.FileAccess]::Read,
                [IO.FileShare]::Read,
                64KB,
                [IO.FileOptions]::SequentialScan)
        }
        catch {
            throw "Unable to acquire stable JSON read access for '$resolvedPath': $($_.Exception.Message)"
        }

        if ($stream.Length -ne $beforeLength) {
            throw "Stable JSON read detected pre-read metadata mutation for '$resolvedPath'."
        }
        $bytes = [byte[]]::new([int]$stream.Length)
        $offset = 0
        while ($offset -lt $bytes.Length) {
            $read = $stream.Read($bytes, $offset, $bytes.Length - $offset)
            if ($read -eq 0) {
                throw "Stable JSON read ended early for '$resolvedPath'."
            }
            $offset += $read
        }
        if ($stream.Length -ne $beforeLength) {
            throw "Stable JSON read detected concurrent length mutation for '$resolvedPath'."
        }
        $during = Get-Item -LiteralPath $resolvedPath -Force
        if ([long]$during.Length -ne $beforeLength -or $during.LastWriteTimeUtc.Ticks -ne $beforeWriteTicks) {
            throw "Stable JSON read detected concurrent metadata mutation for '$resolvedPath'."
        }

        $sha256 = Get-Sha256Hex -Bytes $bytes
        $text = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
        $value = $text | ConvertFrom-Json
    }
    finally {
        if ($null -ne $stream) {
            $stream.Dispose()
        }
    }

    $after = Get-Item -LiteralPath $resolvedPath -Force
    if ((Test-ReparsePoint -Item $after) -or
        [long]$after.Length -ne $beforeLength -or
        $after.LastWriteTimeUtc.Ticks -ne $beforeWriteTicks) {
        throw "Stable JSON read detected post-read metadata mutation for '$resolvedPath'."
    }

    return [pscustomobject][ordered]@{
        value = $value
        bytes = $bytes
        sizeBytes = $beforeLength
        sha256 = $sha256
    }
}

function Get-GenerationProvenanceSidecarName {
    [CmdletBinding()]
    param()

    return $script:GenerationProvenanceSidecarName
}

function Get-CanonicalFileInventory {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RootPath,
        [string[]]$RelativePaths,
        [string[]]$ExcludeRelativePath = @(),
        [switch]$RejectReparsePoints,
        [int]$MaximumFileCount = $script:MaximumInventoryFileCount,
        [int]$MaximumPathLength = $script:MaximumRelativePathLength,
        [long]$MaximumTotalBytes = $script:MaximumInventoryTotalBytes
    )

    $resolvedRoot = [IO.Path]::GetFullPath($RootPath)
    if (-not (Test-Path -LiteralPath $resolvedRoot -PathType Container)) {
        throw "Inventory root '$resolvedRoot' does not exist."
    }
    if ($RejectReparsePoints) {
        Assert-NoReparsePointTree -RootPath $resolvedRoot
    }

    if (-not $PSBoundParameters.ContainsKey('RelativePaths')) {
        $RelativePaths = @(
            Get-ChildItem -LiteralPath $resolvedRoot -File -Force -Recurse |
                ForEach-Object { [IO.Path]::GetRelativePath($resolvedRoot, $_.FullName) }
        )
    }

    $excluded = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($path in $ExcludeRelativePath) {
        [void]$excluded.Add($path.Replace('\', '/'))
    }

    [string[]]$normalizedPaths = @(
        foreach ($path in $RelativePaths) {
            if ([string]::IsNullOrWhiteSpace($path)) {
                continue
            }
            $normalized = $path.Replace('\', '/')
            if (-not $excluded.Contains($normalized)) {
                $normalized
            }
        }
    )
    [Array]::Sort($normalizedPaths, [StringComparer]::Ordinal)
    if ($normalizedPaths.Count -gt $MaximumFileCount) {
        throw "Inventory file count $($normalizedPaths.Count) exceeds bound $MaximumFileCount."
    }

    $comparison = if ([OperatingSystem]::IsWindows()) {
        [StringComparison]::OrdinalIgnoreCase
    }
    else {
        [StringComparison]::Ordinal
    }
    $rootPrefix = [IO.Path]::TrimEndingDirectorySeparator($resolvedRoot) + [IO.Path]::DirectorySeparatorChar
    $entries = [Collections.Generic.List[object]]::new()
    $previousPath = $null
    $runningTotalBytes = 0L
    foreach ($relativePath in $normalizedPaths) {
        if ($relativePath -ceq $previousPath) {
            continue
        }
        $previousPath = $relativePath
        if ($relativePath.Length -gt $MaximumPathLength) {
            throw "Inventory relative path length $($relativePath.Length) exceeds bound $MaximumPathLength."
        }

        $platformPath = $relativePath.Replace('/', [IO.Path]::DirectorySeparatorChar)
        $fullPath = [IO.Path]::GetFullPath((Join-Path $resolvedRoot $platformPath))
        if (-not $fullPath.StartsWith($rootPrefix, $comparison)) {
            throw "Inventory path '$relativePath' escapes root '$resolvedRoot'."
        }
        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
            throw "Inventory file '$relativePath' does not exist under '$resolvedRoot'."
        }

        $identity = Get-StableFileIdentity -Path $fullPath
        $entries.Add([pscustomobject][ordered]@{
            relativePath = $relativePath
            sizeBytes = $identity.sizeBytes
            sha256 = $identity.sha256
        })
        $runningTotalBytes += $identity.sizeBytes
        if ($runningTotalBytes -gt $MaximumTotalBytes) {
            throw "Inventory total bytes exceed bound $MaximumTotalBytes."
        }
    }

    $entryArray = @($entries)
    $canonicalJson = ConvertTo-Json -InputObject $entryArray -Depth 4 -Compress
    $totalBytes = if ($entryArray.Count -eq 0) {
        0L
    }
    else {
        $runningTotalBytes
    }

    return [pscustomobject][ordered]@{
        fileCount = $entryArray.Count
        totalBytes = $totalBytes
        aggregateSha256 = Get-Sha256Hex -Bytes ([Text.Encoding]::UTF8.GetBytes($canonicalJson))
        files = $entryArray
    }
}

function Get-DataGenSourceTreeIdentity {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$GitPath
    )

    $resolvedRepoRoot = [IO.Path]::GetFullPath($RepoRoot)
    if (-not (Test-Path -LiteralPath (Join-Path $resolvedRepoRoot '.git'))) {
        throw "Repo root '$resolvedRepoRoot' does not contain a .git directory."
    }
    Assert-FullyQualifiedFilePath -Path $GitPath -Label 'GitPath'
    $rootItem = Get-Item -LiteralPath $resolvedRepoRoot -Force
    if (Test-ReparsePoint -Item $rootItem) {
        throw "Source root '$resolvedRepoRoot' is a filesystem reparse point."
    }

    [byte[]]$sourceBytes = Invoke-NativeCapture `
        -ExecutablePath $GitPath `
        -ArgumentList @('-C', $resolvedRepoRoot, 'ls-files', '-z', '--cached', '--others', '--exclude-standard') `
        -BinaryOutput
    $sourcePaths = [Collections.Generic.List[string]]::new()
    foreach ($path in Select-DataGenSourcePath -Path (ConvertFrom-GitNullPathBytes -Bytes $sourceBytes)) {
        $sourcePaths.Add($path)
    }
    foreach ($sourcePath in $sourcePaths) {
        Assert-NoReparsePointSourcePath -RootPath $resolvedRepoRoot -RelativePath $sourcePath
    }
    $inventory = Get-CanonicalFileInventory -RootPath $resolvedRepoRoot -RelativePaths @($sourcePaths)

    return [pscustomobject][ordered]@{
        inclusionSet = $script:SourceInclusionSet
        fileCount = $inventory.fileCount
        totalBytes = $inventory.totalBytes
        aggregateSha256 = $inventory.aggregateSha256
        files = $inventory.files
    }
}

function Get-DataGenBuildIdentity {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$GitPath
    )

    $resolvedRepoRoot = [IO.Path]::GetFullPath($RepoRoot)
    $source = Get-DataGenSourceTreeIdentity -RepoRoot $resolvedRepoRoot -GitPath $GitPath
    [xml]$buildProperties = Get-Content -LiteralPath (Join-Path $resolvedRepoRoot 'Directory.Build.props') -Raw
    $version = [string]($buildProperties.Project.PropertyGroup | Where-Object { $null -ne $_.Version } | Select-Object -First 1).Version
    if ([string]::IsNullOrWhiteSpace($version)) {
        throw 'Directory.Build.props does not define Version.'
    }

    $commit = Invoke-NativeCapture -ExecutablePath $GitPath -ArgumentList @('-C', $resolvedRepoRoot, 'rev-parse', 'HEAD')
    $branch = Invoke-NativeCapture -ExecutablePath $GitPath -ArgumentList @('-C', $resolvedRepoRoot, 'branch', '--show-current')
    [byte[]]$statusBytes = Invoke-NativeCapture `
        -ExecutablePath $GitPath `
        -ArgumentList @('-C', $resolvedRepoRoot, 'status', '--porcelain=v1', '-z', '--untracked-files=all', '--', '.', ':(exclude).beads/**') `
        -BinaryOutput

    return [pscustomobject][ordered]@{
        version = $version
        branch = $branch
        commit = $commit
        dirty = $statusBytes.Length -gt 0
        treeSha256 = $source.aggregateSha256
        treeFileCount = $source.fileCount
        treeTotalBytes = $source.totalBytes
        inclusionSet = $source.inclusionSet
    }
}

function Get-DataGenRuntimeIdentity {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$DotNetPath)

    Assert-FullyQualifiedFilePath -Path $DotNetPath -Label 'DotNetPath'
    $dotnetVersion = Invoke-NativeCapture -ExecutablePath $DotNetPath -ArgumentList @('--version')

    return [pscustomobject][ordered]@{
        framework = [Runtime.InteropServices.RuntimeInformation]::FrameworkDescription
        os = [Runtime.InteropServices.RuntimeInformation]::OSDescription
        architecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
        powershell = $PSVersionTable.PSVersion.ToString()
        dotnetSdk = $dotnetVersion
    }
}

function Get-GenerationScriptIdentity {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$RepoRoot
    )

    $resolvedPath = [IO.Path]::GetFullPath($Path)
    $resolvedRepoRoot = [IO.Path]::GetFullPath($RepoRoot)
    if (-not (Test-Path -LiteralPath $resolvedPath -PathType Leaf)) {
        throw "Generation script '$resolvedPath' does not exist."
    }

    $comparison = if ([OperatingSystem]::IsWindows()) {
        [StringComparison]::OrdinalIgnoreCase
    }
    else {
        [StringComparison]::Ordinal
    }
    $repoPrefix = [IO.Path]::TrimEndingDirectorySeparator($resolvedRepoRoot) + [IO.Path]::DirectorySeparatorChar
    $logicalIdentity = if ($resolvedPath.StartsWith($repoPrefix, $comparison)) {
        'repo:' + [IO.Path]::GetRelativePath($resolvedRepoRoot, $resolvedPath).Replace('\', '/')
    }
    else {
        'external:' + [IO.Path]::GetFileName($resolvedPath)
    }
    $identity = Get-StableFileIdentity -Path $resolvedPath

    return [pscustomobject][ordered]@{
        logicalIdentity = $logicalIdentity
        sha256 = $identity.sha256
        sizeBytes = $identity.sizeBytes
    }
}

function Get-EvidenceExecutableIdentity {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$LogicalLabel,
        [Parameter(Mandatory)][string[]]$VersionArgumentList
    )

    Assert-FullyQualifiedFilePath -Path $Path -Label $LogicalLabel
    $identity = Get-StableFileIdentity -Path $Path
    return [pscustomobject][ordered]@{
        logicalLabel = $LogicalLabel
        sha256 = $identity.sha256
        sizeBytes = $identity.sizeBytes
        version = Invoke-NativeCapture -ExecutablePath $Path -ArgumentList $VersionArgumentList
    }
}

function Get-GenerationInvocationIdentity {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][int]$Seed,
        [Parameter(Mandatory)][DateTimeOffset]$GeneratedAt,
        [string[]]$GenerationArgumentList = @(),
        [string[]]$SensitiveGenerationArgumentName = @(),
        [string[]]$SensitiveGenerationArgumentPattern = @(),
        [int[]]$SensitiveGenerationArgumentIndex = @()
    )

    $sensitiveNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($name in $SensitiveGenerationArgumentName) {
        $normalizedName = $name.Trim().TrimStart('-')
        if ([string]::IsNullOrWhiteSpace($normalizedName)) {
            throw 'Sensitive generation argument names cannot be empty.'
        }
        [void]$sensitiveNames.Add($normalizedName)
    }
    $sensitivePatterns = @(
        foreach ($pattern in $SensitiveGenerationArgumentPattern) {
            [Text.RegularExpressions.Regex]::new($pattern, [Text.RegularExpressions.RegexOptions]::CultureInvariant)
        }
    )
    $explicitSensitiveIndexes = [Collections.Generic.HashSet[int]]::new()
    foreach ($index in $SensitiveGenerationArgumentIndex) {
        if ($index -lt 0 -or $index -ge $GenerationArgumentList.Count) {
            throw "Sensitive generation argument index $index is outside the forwarded argument vector."
        }
        [void]$explicitSensitiveIndexes.Add($index)
    }

    [string[]]$canonicalVector = @(
        '-OutputPath', '<candidate-root>',
        '-ScenarioPath', '<scenario-path>',
        '-Seed', $Seed.ToString([Globalization.CultureInfo]::InvariantCulture),
        '-GeneratedAt', $GeneratedAt.ToUniversalTime().ToString('O', [Globalization.CultureInfo]::InvariantCulture)
    ) + @($GenerationArgumentList | ForEach-Object { [string]$_ })
    [string[]]$digestVector = @($canonicalVector)
    [string[]]$structuredVector = @($canonicalVector)
    $structuredVector[7] = 'utc:' + $structuredVector[7]

    $safeArguments = [Collections.Generic.List[object]]::new()
    $redactNextFor = $null
    $sensitiveInputCount = 0
    for ($index = 0; $index -lt $structuredVector.Count; $index++) {
        $value = $structuredVector[$index]
        $extraIndex = $index - 8
        if ($extraIndex -ge 0 -and $explicitSensitiveIndexes.Contains($extraIndex)) {
            $digestVector[$index] = '<redacted-sensitive-input>'
            $safeArguments.Add([pscustomobject][ordered]@{
                index = $index
                value = '<redacted-sensitive-input>'
                redacted = $true
                parameter = "index:$extraIndex"
            })
            $sensitiveInputCount++
            $redactNextFor = $null
            continue
        }
        if ($null -ne $redactNextFor) {
            $digestVector[$index] = '<redacted-sensitive-input>'
            $safeArguments.Add([pscustomobject][ordered]@{
                index = $index
                value = '<redacted-sensitive-input>'
                redacted = $true
                parameter = $redactNextFor
            })
            $sensitiveInputCount++
            $redactNextFor = $null
            continue
        }

        $equalsMatch = [Text.RegularExpressions.Regex]::Match($value, '^--?([^=]+)=(.*)$')
        $equalsName = if ($equalsMatch.Success) { $equalsMatch.Groups[1].Value } else { $null }
        $equalsSensitive = $equalsMatch.Success -and (
            $sensitiveNames.Contains($equalsName) -or
            @($sensitivePatterns | Where-Object { $_.IsMatch($equalsName) }).Count -gt 0)
        if ($equalsSensitive) {
            $digestVector[$index] = "-$equalsName=<redacted-sensitive-input>"
            $safeArguments.Add([pscustomobject][ordered]@{
                index = $index
                value = "-$equalsName=<redacted-sensitive-input>"
                redacted = $true
                parameter = $equalsName
            })
            $sensitiveInputCount++
            continue
        }

        $flagMatch = [Text.RegularExpressions.Regex]::Match($value, '^--?(.+)$')
        $safeArguments.Add([pscustomobject][ordered]@{
            index = $index
            value = $value
            redacted = $false
        })
        if ($flagMatch.Success) {
            $flagName = $flagMatch.Groups[1].Value
            if ($sensitiveNames.Contains($flagName) -or
                @($sensitivePatterns | Where-Object { $_.IsMatch($flagName) }).Count -gt 0) {
                $redactNextFor = $flagName
            }
        }
    }

    if ($null -ne $redactNextFor) {
        throw "Sensitive generation argument '-$redactNextFor' has no value to redact."
    }

    [string[]]$orderedSensitiveNames = @($sensitiveNames)
    [Array]::Sort($orderedSensitiveNames, [StringComparer]::OrdinalIgnoreCase)
    [string[]]$orderedSensitivePatterns = @($SensitiveGenerationArgumentPattern)
    [Array]::Sort($orderedSensitivePatterns, [StringComparer]::Ordinal)
    [int[]]$orderedSensitiveIndexes = @($explicitSensitiveIndexes)
    [Array]::Sort($orderedSensitiveIndexes)
    $canonicalJson = ConvertTo-Json -InputObject $digestVector -Compress

    return [pscustomobject][ordered]@{
        contractVersion = $script:InvocationContractVersion
        argumentDigestSha256 = Get-Sha256Hex -Bytes ([Text.Encoding]::UTF8.GetBytes($canonicalJson))
        safeArguments = @($safeArguments)
        sensitiveArgumentNames = $orderedSensitiveNames
        sensitiveArgumentPatterns = $orderedSensitivePatterns
        sensitiveArgumentIndexes = $orderedSensitiveIndexes
        sensitiveInputsExcludedFromReproducibility = $sensitiveInputCount -gt 0
        sensitiveInputCount = $sensitiveInputCount
    }
}

function Get-GenerationEvidenceEnvironment {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$WrapperPath,
        [Parameter(Mandatory)][string]$GenerationScriptPath,
        [Parameter(Mandatory)][string]$ScenarioPath,
        [Parameter(Mandatory)][string]$GitPath,
        [Parameter(Mandatory)][string]$DotNetPath,
        [Parameter(Mandatory)][int]$Seed,
        [Parameter(Mandatory)][DateTimeOffset]$GeneratedAt,
        [string[]]$GenerationArgumentList = @(),
        [string[]]$SensitiveGenerationArgumentName = @(),
        [string[]]$SensitiveGenerationArgumentPattern = @(),
        [int[]]$SensitiveGenerationArgumentIndex = @()
    )

    $wrapper = Get-StableFileIdentity -Path $WrapperPath
    $generator = Get-GenerationScriptIdentity -Path $GenerationScriptPath -RepoRoot $RepoRoot
    $scenario = Get-StableFileIdentity -Path $ScenarioPath
    return [pscustomobject][ordered]@{
        wrapper = [ordered]@{
            logicalLabel = [IO.Path]::GetFileName($WrapperPath)
            sha256 = $wrapper.sha256
            sizeBytes = $wrapper.sizeBytes
        }
        generator = $generator
        scenario = [ordered]@{
            logicalLabel = [IO.Path]::GetFileName($ScenarioPath)
            sha256 = $scenario.sha256
            sizeBytes = $scenario.sizeBytes
        }
        executables = [ordered]@{
            git = Get-EvidenceExecutableIdentity -Path $GitPath -LogicalLabel 'git' -VersionArgumentList @('--version')
            dotnet = Get-EvidenceExecutableIdentity -Path $DotNetPath -LogicalLabel 'dotnet' -VersionArgumentList @('--version')
        }
        source = Get-DataGenBuildIdentity -RepoRoot $RepoRoot -GitPath $GitPath
        runtime = Get-DataGenRuntimeIdentity -DotNetPath $DotNetPath
        invocation = Get-GenerationInvocationIdentity `
            -Seed $Seed `
            -GeneratedAt $GeneratedAt `
            -GenerationArgumentList $GenerationArgumentList `
            -SensitiveGenerationArgumentName $SensitiveGenerationArgumentName `
            -SensitiveGenerationArgumentPattern $SensitiveGenerationArgumentPattern `
            -SensitiveGenerationArgumentIndex $SensitiveGenerationArgumentIndex
    }
}

function Write-CanonicalJsonFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Value,
        [Parameter(Mandatory)][string]$Path,
        [int]$Depth = 16,
        [int]$MaximumBytes = 16MB
    )

    $resolvedPath = [IO.Path]::GetFullPath($Path)
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($resolvedPath)) | Out-Null
    $json = (ConvertTo-Json -InputObject $Value -Depth $Depth).ReplaceLineEndings("`n") + "`n"
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($json)
    if ($bytes.Length -gt $MaximumBytes) {
        throw "Canonical JSON size $($bytes.Length) exceeds bound $MaximumBytes."
    }
    $directory = [IO.Path]::GetDirectoryName($resolvedPath)
    $temporaryPath = Join-Path $directory ('.' + [IO.Path]::GetFileName($resolvedPath) + '.' + [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        $stream = [IO.FileStream]::new($temporaryPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try {
            $stream.Write($bytes, 0, $bytes.Length)
            $stream.Flush($true)
        }
        finally {
            $stream.Dispose()
        }

        if ([IO.File]::Exists($resolvedPath)) {
            [IO.File]::Move($temporaryPath, $resolvedPath, $true)
        }
        else {
            [IO.File]::Move($temporaryPath, $resolvedPath)
        }
    }
    finally {
        if ([IO.File]::Exists($temporaryPath)) {
            [IO.File]::Delete($temporaryPath)
        }
    }
}

Export-ModuleMember -Function @(
    'Assert-NoReparsePointTree',
    'ConvertFrom-GitNullPathBytes',
    'Select-DataGenSourcePath',
    'Get-CanonicalFileInventory',
    'Get-DataGenBuildIdentity',
    'Get-DataGenRuntimeIdentity',
    'Get-DataGenSourceTreeIdentity',
    'Get-FileSha256Hex',
    'Get-GenerationInvocationIdentity',
    'Get-GenerationEvidenceEnvironment',
    'Get-GenerationProvenanceSidecarName',
    'Get-GenerationScriptIdentity',
    'Get-EvidenceExecutableIdentity',
    'Get-Sha256Hex',
    'Get-StableFileIdentity',
    'Read-StableJsonEvidenceFile',
    'Write-CanonicalJsonFile'
)
