namespace SyntheticEnterprise.Core.Plugins;

using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using SyntheticEnterprise.Contracts.Plugins;

internal static class ExternalPluginPathSecurity
{
    internal const int MaximumEntryPointBytes = 4 * 1024 * 1024;

    public static bool TryValidateManifestPaths(GenerationPluginManifest manifest, out string? warning)
    {
        foreach (var (label, path) in EnumerateManifestPaths(manifest))
        {
            if (TryValidateNoReparsePoints(path, out var pathWarning))
            {
                continue;
            }

            warning = $"{label} '{path}' failed plugin path security validation: {pathWarning}";
            return false;
        }

        warning = null;
        return true;
    }

    public static bool TryValidateNoReparsePoints(string path, out string? warning)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrWhiteSpace(root))
            {
                warning = "the path has no filesystem root.";
                return false;
            }

            var current = root;
            var relativePath = Path.GetRelativePath(root, fullPath);
            foreach (var component in relativePath.Split(
                         new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, component);
                var attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    warning = $"path component '{current}' is a symbolic link or reparse point.";
                    return false;
                }
            }

            warning = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            warning = ex.Message;
            return false;
        }
    }

    public static FileStream? OpenVerifiedEntryPoint(
        GenerationPluginManifest manifest,
        out string? warning)
    {
        if (string.IsNullOrWhiteSpace(manifest.EntryPoint) || !File.Exists(manifest.EntryPoint))
        {
            warning = "Plugin entry point is unavailable at execution time.";
            return null;
        }

        if (!TryValidateManifestPaths(manifest, out warning))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(manifest.Provenance.EntryPointHash))
        {
            warning = "Plugin provenance is incomplete because the discovered entry point hash is missing.";
            return null;
        }

        var stream = OpenVerifiedPackageFile(manifest, manifest.EntryPoint, out warning);
        if (stream is null)
        {
            return null;
        }

        try
        {
            var currentHash = ComputeBoundedHash(stream, MaximumEntryPointBytes);
            if (string.Equals(currentHash, manifest.Provenance.EntryPointHash, StringComparison.OrdinalIgnoreCase))
            {
                warning = null;
                return stream;
            }

            stream.Dispose();
            warning = "Plugin entry point hash no longer matches discovered provenance.";
            return null;
        }
        catch (PluginInputPayloadLimitExceededException)
        {
            stream.Dispose();
            warning = $"Plugin entry point exceeded the approved package-file limit of {MaximumEntryPointBytes} bytes.";
            return null;
        }
    }

    public static string ReadVerifiedText(FileStream verifiedStream)
    {
        var budget = new PluginInputByteBudget(MaximumEntryPointBytes);
        using var boundedStream = new BoundedPluginCatalogReadStream(
            verifiedStream,
            budget,
            MaximumEntryPointBytes,
            leaveOpen: true);
        using var reader = new StreamReader(
            boundedStream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096,
            leaveOpen: true);
        return reader.ReadToEnd();
    }

    public static FileStream? OpenVerifiedPackageFile(
        GenerationPluginManifest manifest,
        string path,
        out string? warning)
    {
        if (string.IsNullOrWhiteSpace(manifest.SourcePath))
        {
            warning = "Plugin package root is unavailable for handle-based path validation.";
            return null;
        }

        return OpenVerifiedPackageFile(manifest.SourcePath, path, out warning);
    }

    public static FileStream? OpenVerifiedPackageFile(
        string manifestPath,
        string path,
        out string? warning)
    {
        var packageRoot = Path.GetDirectoryName(Path.GetFullPath(manifestPath));
        if (string.IsNullOrWhiteSpace(packageRoot))
        {
            warning = "Plugin package root is unavailable for handle-based path validation.";
            return null;
        }

        if (!TryValidateNoReparsePoints(packageRoot, out warning)
            || !TryValidateNoReparsePoints(path, out warning))
        {
            return null;
        }

        FileStream stream;
        try
        {
            stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.SequentialScan);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warning = $"Plugin package file could not be opened securely: {ex.Message}";
            return null;
        }

        if (TryValidateOpenedPackageFile(stream, packageRoot, path, out warning)
            && TryValidateNoReparsePoints(packageRoot, out warning)
            && TryValidateNoReparsePoints(path, out warning))
        {
            return stream;
        }

        stream.Dispose();
        return null;
    }

    public static string? ComputeVerifiedPackageFileHash(
        string manifestPath,
        string path,
        long maxBytes,
        out string? warning)
    {
        using var stream = OpenVerifiedPackageFile(manifestPath, path, out warning);
        if (stream is null)
        {
            return null;
        }

        try
        {
            var hash = ComputeBoundedHash(stream, maxBytes);
            warning = null;
            return hash;
        }
        catch (PluginInputPayloadLimitExceededException)
        {
            warning = $"Plugin package file '{path}' exceeded the approved limit of {maxBytes} bytes.";
            return null;
        }
    }

    internal static bool TryValidateOpenedPackageFile(
        FileStream stream,
        string packageRoot,
        string expectedPath,
        out string? warning)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!TryGetWindowsFinalPath(stream.SafeFileHandle, out var finalPath)
                || !TryGetWindowsFinalDirectoryPath(packageRoot, out var finalRoot))
            {
                warning = "The final target of the opened package file could not be resolved from its handle.";
                return false;
            }

            return ValidateFinalTarget(finalPath, finalRoot, expectedPath, out warning);
        }

        if (OperatingSystem.IsLinux())
        {
            var descriptorPath = $"/proc/self/fd/{stream.SafeFileHandle.DangerousGetHandle().ToInt64()}";
            try
            {
                var target = File.ResolveLinkTarget(descriptorPath, returnFinalTarget: true);
                if (target is null)
                {
                    warning = "The final target of the opened package file could not be resolved from /proc/self/fd.";
                    return false;
                }

                return ValidateFinalTarget(target.FullName, Path.GetFullPath(packageRoot), expectedPath, out warning);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                warning = $"The final target of the opened package file could not be resolved: {ex.Message}";
                return false;
            }
        }

        // On platforms without a handle-to-path API, reject any visible link indicator before and
        // after opening. Strong final-target proof is currently available on Windows and Linux.
        return TryValidateNoReparsePoints(packageRoot, out warning)
               && TryValidateNoReparsePoints(expectedPath, out warning);
    }

    private static bool ValidateFinalTarget(
        string finalPath,
        string finalRoot,
        string expectedPath,
        out string? warning)
    {
        var normalizedFinalPath = Path.GetFullPath(finalPath);
        var normalizedFinalRoot = Path.GetFullPath(finalRoot);
        var normalizedExpectedPath = Path.GetFullPath(expectedPath);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!string.Equals(normalizedFinalPath, normalizedExpectedPath, comparison)
            || !IsWithinRoot(normalizedFinalRoot, normalizedFinalPath, comparison))
        {
            warning = $"Opened package file resolved to '{normalizedFinalPath}' instead of the approved package path '{normalizedExpectedPath}'.";
            return false;
        }

        warning = null;
        return true;
    }

    private static bool IsWithinRoot(string root, string candidate, StringComparison comparison)
    {
        var normalizedRoot = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(normalizedRoot, comparison);
    }

    private static string ComputeBoundedHash(FileStream stream, long maxBytes)
    {
        stream.Position = 0;
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        long totalBytes = 0;
        while (true)
        {
            var bytesRead = stream.Read(buffer, 0, buffer.Length);
            if (bytesRead == 0)
            {
                stream.Position = 0;
                return Convert.ToHexString(sha.GetHashAndReset());
            }

            totalBytes += bytesRead;
            if (totalBytes > maxBytes)
            {
                throw new PluginInputPayloadLimitExceededException();
            }

            sha.AppendData(buffer, 0, bytesRead);
        }
    }

    private static bool TryGetWindowsFinalDirectoryPath(string path, out string finalPath)
    {
        finalPath = string.Empty;
        using var handle = CreateFile(
            path,
            0,
            FileShare.Read | FileShare.Write | FileShare.Delete,
            IntPtr.Zero,
            FileMode.Open,
            FileFlagsAndAttributes.BackupSemantics,
            IntPtr.Zero);
        return !handle.IsInvalid && TryGetWindowsFinalPath(handle, out finalPath);
    }

    private static bool TryGetWindowsFinalPath(SafeFileHandle handle, out string finalPath)
    {
        var buffer = new StringBuilder(4096);
        var length = GetFinalPathNameByHandle(handle, buffer, buffer.Capacity, 0);
        if (length == 0 || length >= buffer.Capacity)
        {
            finalPath = string.Empty;
            return false;
        }

        finalPath = NormalizeWindowsDevicePath(buffer.ToString());
        return true;
    }

    private static string NormalizeWindowsDevicePath(string path)
    {
        const string uncPrefix = @"\\?\UNC\";
        const string devicePrefix = @"\\?\";
        if (path.StartsWith(uncPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return @"\\" + path[uncPrefix.Length..];
        }

        return path.StartsWith(devicePrefix, StringComparison.OrdinalIgnoreCase)
            ? path[devicePrefix.Length..]
            : path;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        FileShare shareMode,
        IntPtr securityAttributes,
        FileMode creationDisposition,
        FileFlagsAndAttributes flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(
        SafeFileHandle file,
        StringBuilder filePath,
        int filePathSize,
        uint flags);

    [Flags]
    private enum FileFlagsAndAttributes : uint
    {
        BackupSemantics = 0x02000000
    }

    private static IEnumerable<(string Label, string Path)> EnumerateManifestPaths(GenerationPluginManifest manifest)
    {
        if (!string.IsNullOrWhiteSpace(manifest.SourcePath))
        {
            var packageRoot = Path.GetDirectoryName(Path.GetFullPath(manifest.SourcePath));
            if (!string.IsNullOrWhiteSpace(packageRoot))
            {
                yield return ("Plugin package root", packageRoot);
            }

            yield return ("Plugin manifest", manifest.SourcePath);
        }

        if (!string.IsNullOrWhiteSpace(manifest.EntryPoint))
        {
            yield return ("Plugin entry point", manifest.EntryPoint);
        }

        foreach (var path in manifest.LocalDataPaths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            yield return ("Plugin catalog", path);
        }
    }
}

internal sealed class PluginInputByteBudget
{
    private readonly long _maxBytes;
    private long _consumedBytes;

    public PluginInputByteBudget(long maxBytes)
    {
        _maxBytes = Math.Max(0, maxBytes);
    }

    public long RemainingBytes
    {
        get
        {
            lock (this)
            {
                return _maxBytes - _consumedBytes;
            }
        }
    }

    public void Consume(long bytes)
    {
        lock (this)
        {
            if (bytes < 0 || bytes > _maxBytes - _consumedBytes)
            {
                throw new PluginInputPayloadLimitExceededException();
            }

            _consumedBytes += bytes;
        }
    }
}

internal sealed class BoundedPluginCatalogReadStream : Stream
{
    private readonly Stream _inner;
    private readonly PluginInputByteBudget _cumulativeBudget;
    private readonly long _maxFileBytes;
    private readonly bool _leaveOpen;
    private long _fileBytesRead;

    public BoundedPluginCatalogReadStream(
        Stream inner,
        PluginInputByteBudget cumulativeBudget,
        long maxFileBytes,
        bool leaveOpen = false)
    {
        _inner = inner;
        _cumulativeBudget = cumulativeBudget;
        _maxFileBytes = Math.Max(0, maxFileBytes);
        _leaveOpen = leaveOpen;
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
        => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (buffer.IsEmpty)
        {
            return 0;
        }

        var allowed = Math.Min(_maxFileBytes - _fileBytesRead, _cumulativeBudget.RemainingBytes);
        var readLength = (int)Math.Min(buffer.Length, Math.Max(1, allowed + 1));
        var bytesRead = _inner.Read(buffer[..readLength]);
        Account(bytesRead, allowed);
        return bytesRead;
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        if (buffer.IsEmpty)
        {
            return 0;
        }

        var allowed = Math.Min(_maxFileBytes - _fileBytesRead, _cumulativeBudget.RemainingBytes);
        var readLength = (int)Math.Min(buffer.Length, Math.Max(1, allowed + 1));
        var bytesRead = await _inner.ReadAsync(buffer[..readLength], cancellationToken).ConfigureAwait(false);
        Account(bytesRead, allowed);
        return bytesRead;
    }

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    private void Account(int bytesRead, long allowed)
    {
        if (bytesRead > allowed)
        {
            throw new PluginInputPayloadLimitExceededException();
        }

        _cumulativeBudget.Consume(bytesRead);
        _fileBytesRead += bytesRead;
    }

    public override void Flush()
        => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin)
        => throw new NotSupportedException();

    public override void SetLength(long value)
        => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
        => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_leaveOpen)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}

internal sealed class PluginPathSecurityException : Exception
{
    public PluginPathSecurityException(string message)
        : base(message)
    {
    }
}

internal interface IExternalPluginAssemblyStager
{
    StagedExternalPluginAssembly Stage(GenerationPluginManifest manifest, string temporaryRoot);
}

internal sealed record StagedExternalPluginAssembly(string EntryPoint);

internal sealed class FileSystemExternalPluginAssemblyStager : IExternalPluginAssemblyStager
{
    internal const int MaximumStagedFileCount = 1024;
    internal const long MaximumStagedFileBytes = 64L * 1024 * 1024;
    internal const long MaximumStagedPackageBytes = 256L * 1024 * 1024;

    public StagedExternalPluginAssembly Stage(GenerationPluginManifest manifest, string temporaryRoot)
    {
        if (string.IsNullOrWhiteSpace(manifest.EntryPoint))
        {
            throw new PluginPathSecurityException("Assembly plugin entry point is unavailable for verified staging.");
        }

        var sourceEntryPoint = Path.GetFullPath(manifest.EntryPoint);
        var sourceDirectory = Path.GetDirectoryName(sourceEntryPoint)
            ?? throw new PluginPathSecurityException("Assembly plugin entry-point directory is unavailable for verified staging.");
        if (!ExternalPluginPathSecurity.TryValidateNoReparsePoints(sourceDirectory, out var pathWarning))
        {
            throw new PluginPathSecurityException($"Assembly plugin staging source failed path security validation: {pathWarning}");
        }

        var stagingRoot = Path.Combine(temporaryRoot, "plugin-package");
        Directory.CreateDirectory(stagingRoot);
        var packageBudget = new PluginInputByteBudget(MaximumStagedPackageBytes);
        var files = EnumeratePackageFiles(sourceDirectory).ToList();
        var stagedFileHashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        string? stagedEntryPoint = null;
        foreach (var sourcePath in files)
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, sourcePath);
            if (relativePath == ".."
                || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                throw new PluginPathSecurityException("Assembly plugin staging source escaped the approved entry-point directory.");
            }

            var destinationPath = Path.Combine(stagingRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            using var source = string.Equals(sourcePath, sourceEntryPoint, PathComparison)
                ? ExternalPluginPathSecurity.OpenVerifiedEntryPoint(manifest, out pathWarning)
                : ExternalPluginPathSecurity.OpenVerifiedPackageFile(manifest, sourcePath, out pathWarning);
            if (source is null)
            {
                throw new PluginPathSecurityException(pathWarning!);
            }

            try
            {
                stagedFileHashes[NormalizeRelativePath(relativePath)] = CopyVerifiedFile(
                    source,
                    destinationPath,
                    packageBudget);
            }
            catch (PluginInputPayloadLimitExceededException)
            {
                throw new PluginPathSecurityException(
                    $"Assembly plugin package exceeded the approved staging limits of {MaximumStagedFileBytes} bytes per file and {MaximumStagedPackageBytes} bytes cumulatively.");
            }

            if (string.Equals(sourcePath, sourceEntryPoint, PathComparison))
            {
                stagedEntryPoint = destinationPath;
            }
        }

        if (stagedEntryPoint is null)
        {
            throw new PluginPathSecurityException("Assembly plugin entry point was not present in the verified staged package.");
        }

        var manifestHash = ExternalPluginPathSecurity.ComputeVerifiedPackageFileHash(
                               manifest.SourcePath!,
                               manifest.SourcePath!,
                               ExternalPluginPathSecurity.MaximumEntryPointBytes,
                               out var manifestWarning)
                           ?? throw new PluginPathSecurityException(manifestWarning!);
        var packageHash = ComputePackageHash(stagedFileHashes);
        var currentContentHash = ComputeApprovedContentHash(
            manifestHash,
            manifest.Provenance.EntryPointHash!,
            manifest.Provenance.LocalDataHashes,
            packageHash);
        if (!string.Equals(currentContentHash, manifest.Provenance.ContentHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new PluginPathSecurityException(
                "Assembly plugin staged package hash no longer matches discovered provenance.");
        }

        return new StagedExternalPluginAssembly(stagedEntryPoint);
    }

    internal static string ComputeDiscoveredPackageHash(string manifestPath, string entryPointPath)
    {
        var sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(entryPointPath))
            ?? throw new PluginPathSecurityException("Assembly plugin entry-point directory is unavailable for provenance hashing.");
        var packageBudget = new PluginInputByteBudget(MaximumStagedPackageBytes);
        var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in EnumeratePackageFiles(sourceDirectory))
        {
            using var stream = ExternalPluginPathSecurity.OpenVerifiedPackageFile(manifestPath, path, out var warning)
                ?? throw new PluginPathSecurityException(warning!);
            try
            {
                hashes[NormalizeRelativePath(Path.GetRelativePath(sourceDirectory, path))] =
                    ComputeBoundedFileHash(stream, packageBudget);
            }
            catch (PluginInputPayloadLimitExceededException)
            {
                throw new PluginPathSecurityException(
                    $"Assembly plugin package exceeded the approved provenance limits of {MaximumStagedFileBytes} bytes per file and {MaximumStagedPackageBytes} bytes cumulatively.");
            }
        }

        return ComputePackageHash(hashes);
    }

    internal static string ComputeApprovedContentHash(
        string manifestHash,
        string entryPointHash,
        IReadOnlyDictionary<string, string> localDataHashes,
        string? packageHash)
    {
        var parts = new List<string>
        {
            manifestHash,
            entryPointHash,
            string.Join("|", localDataHashes
                .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .Select(entry => $"{entry.Key}:{entry.Value}"))
        };
        if (packageHash is not null)
        {
            parts.Add(packageHash);
        }

        var combinedMaterial = string.Join("|", parts);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(combinedMaterial)));
    }

    private static StringComparison PathComparison
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static IEnumerable<string> EnumeratePackageFiles(string sourceDirectory)
    {
        var pending = new Stack<string>();
        pending.Push(sourceDirectory);
        var fileCount = 0;
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!ExternalPluginPathSecurity.TryValidateNoReparsePoints(current, out var warning))
            {
                throw new PluginPathSecurityException($"Assembly plugin staging source failed path security validation: {warning}");
            }

            foreach (var entry in Directory.EnumerateFileSystemEntries(current).OrderBy(path => path, StringComparer.Ordinal))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new PluginPathSecurityException(
                        $"Assembly plugin staging source '{entry}' is a symbolic link or reparse point.");
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                    continue;
                }

                fileCount++;
                if (fileCount > MaximumStagedFileCount)
                {
                    throw new PluginPathSecurityException(
                        $"Assembly plugin package exceeded the approved staging limit of {MaximumStagedFileCount} files.");
                }

                yield return entry;
            }
        }
    }

    private static string CopyVerifiedFile(
        FileStream source,
        string destinationPath,
        PluginInputByteBudget packageBudget)
    {
        var partialPath = destinationPath + $".{Guid.NewGuid():N}.partial";
        try
        {
            source.Position = 0;
            string hash;
            using (var boundedSource = new BoundedPluginCatalogReadStream(
                       source,
                       packageBudget,
                       MaximumStagedFileBytes,
                       leaveOpen: true))
            using (var destination = new FileStream(
                       partialPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 64 * 1024,
                       FileOptions.SequentialScan))
            {
                hash = CopyAndHash(boundedSource, destination);
                destination.Flush(flushToDisk: true);
            }

            File.Move(partialPath, destinationPath);
            return hash;
        }
        finally
        {
            if (File.Exists(partialPath))
            {
                File.Delete(partialPath);
            }
        }
    }

    private static string ComputeBoundedFileHash(FileStream source, PluginInputByteBudget packageBudget)
    {
        source.Position = 0;
        using var boundedSource = new BoundedPluginCatalogReadStream(
            source,
            packageBudget,
            MaximumStagedFileBytes,
            leaveOpen: true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        while (boundedSource.Read(buffer, 0, buffer.Length) is var bytesRead && bytesRead > 0)
        {
            hash.AppendData(buffer, 0, bytesRead);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static string CopyAndHash(Stream source, Stream destination)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        while (source.Read(buffer, 0, buffer.Length) is var bytesRead && bytesRead > 0)
        {
            hash.AppendData(buffer, 0, bytesRead);
            destination.Write(buffer, 0, bytesRead);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static string ComputePackageHash(IReadOnlyDictionary<string, string> hashes)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join("|", hashes.Select(entry => $"{entry.Key}:{entry.Value}")))));

    private static string NormalizeRelativePath(string relativePath)
        => relativePath.Replace(Path.DirectorySeparatorChar, '/');
}

internal sealed class PluginOutputByteBudget
{
    private readonly long _maxBytes;
    private readonly TaskCompletionSource _limitExceeded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _consumedBytes;

    public PluginOutputByteBudget(long maxBytes)
    {
        _maxBytes = Math.Max(0, maxBytes);
    }

    public Task LimitExceededTask => _limitExceeded.Task;
    public bool LimitExceeded => _limitExceeded.Task.IsCompleted;

    public long RemainingBytes
    {
        get
        {
            lock (this)
            {
                return _maxBytes - _consumedBytes;
            }
        }
    }

    public bool TryConsume(int bytes)
    {
        lock (this)
        {
            if (bytes < 0 || bytes > _maxBytes - _consumedBytes)
            {
                _limitExceeded.TrySetResult();
                return false;
            }

            _consumedBytes += bytes;
            return true;
        }
    }
}

internal sealed class BoundedProcessStreamCapture
{
    private readonly Stream _stream;
    private readonly PluginOutputByteBudget _budget;
    private readonly int _maxRetainedBytes;
    private readonly MemoryStream _retained = new();

    public BoundedProcessStreamCapture(
        Stream stream,
        PluginOutputByteBudget budget,
        int maxRetainedBytes)
    {
        _stream = stream;
        _budget = budget;
        _maxRetainedBytes = Math.Max(0, maxRetainedBytes);
    }

    public async Task CaptureAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        while (true)
        {
            var bytesRead = await _stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                return;
            }

            if (!_budget.TryConsume(bytesRead))
            {
                return;
            }

            var retainedBytes = Math.Min(bytesRead, _maxRetainedBytes - (int)_retained.Length);
            if (retainedBytes > 0)
            {
                _retained.Write(buffer, 0, retainedBytes);
            }
        }
    }

    public string GetText()
        => Encoding.UTF8.GetString(_retained.GetBuffer(), 0, (int)_retained.Length);
}

internal sealed class BoundedPluginOutputReadStream : Stream
{
    private readonly Stream _inner;
    private readonly PluginOutputByteBudget _budget;

    public BoundedPluginOutputReadStream(Stream inner, PluginOutputByteBudget budget)
    {
        _inner = inner;
        _budget = budget;
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
        => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (buffer.IsEmpty)
        {
            return 0;
        }

        var readLength = (int)Math.Min(buffer.Length, Math.Max(1, _budget.RemainingBytes + 1));
        var bytesRead = _inner.Read(buffer[..readLength]);
        if (!_budget.TryConsume(bytesRead))
        {
            throw new PluginOutputPayloadLimitExceededException();
        }

        return bytesRead;
    }

    public override void Flush()
        => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin)
        => throw new NotSupportedException();

    public override void SetLength(long value)
        => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
        => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}

internal sealed class PluginOutputPayloadLimitExceededException : Exception
{
}

internal interface IExternalPluginTemporaryDirectoryManager
{
    string CreateDirectory();
    ExternalPluginCleanupResult Cleanup(string rootPath, string requestPath, string responsePath);
}

internal sealed record ExternalPluginCleanupResult(bool Succeeded, string? Error);

internal class FileSystemExternalPluginTemporaryDirectoryManager : IExternalPluginTemporaryDirectoryManager
{
    public virtual string CreateDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"datagen-assembly-host-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    public ExternalPluginCleanupResult Cleanup(string rootPath, string requestPath, string responsePath)
    {
        var errors = new List<string>();
        DeleteFile(requestPath, "request", errors);
        DeleteFile(responsePath, "response", errors);

        if (Directory.Exists(rootPath))
        {
            DeleteDirectoryContents(
                rootPath,
                new HashSet<string>(new[] { requestPath, responsePath }, StringComparer.OrdinalIgnoreCase),
                errors);
            TryCleanupAction(
                () => DeleteDirectory(rootPath),
                $"temporary directory '{rootPath}'",
                errors);
        }

        return errors.Count == 0
            ? new ExternalPluginCleanupResult(true, null)
            : new ExternalPluginCleanupResult(false, string.Join("; ", errors));
    }

    protected virtual void DeleteFileCore(string path)
        => File.Delete(path);

    protected virtual void DeleteDirectory(string path)
        => Directory.Delete(path, recursive: false);

    private void DeleteFile(string path, string description, List<string> errors)
    {
        if (!File.Exists(path))
        {
            return;
        }

        TryCleanupAction(() => DeleteFileCore(path), $"{description} file '{path}'", errors);
    }

    private void DeleteDirectoryContents(
        string path,
        IReadOnlySet<string> excludedFiles,
        List<string> errors)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly))
            {
                if (!excludedFiles.Contains(file))
                {
                    DeleteFile(file, "temporary", errors);
                }
            }

            foreach (var directory in Directory.EnumerateDirectories(path, "*", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var attributes = File.GetAttributes(directory);
                    if ((attributes & FileAttributes.ReparsePoint) == 0)
                    {
                        DeleteDirectoryContents(directory, excludedFiles, errors);
                    }

                    TryCleanupAction(
                        () => DeleteDirectory(directory),
                        $"temporary directory '{directory}'",
                        errors);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    errors.Add($"could not inspect temporary directory '{directory}': {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors.Add($"could not enumerate temporary directory '{path}': {ex.Message}");
        }
    }

    private static void TryCleanupAction(Action action, string description, List<string> errors)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors.Add($"could not delete {description}: {ex.Message}");
        }
    }
}
