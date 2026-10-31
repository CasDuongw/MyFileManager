using System.IO;
using System.Security.Cryptography;
using FM.Core.FileSystem;

namespace FM.Core.Operations.Transfer;

/// <summary>
/// Default implementation of <see cref="IStagingBackupStore"/> that isolates overwritten files
/// in per-operation directories within a designated staging root.
/// </summary>
public sealed class StagingBackupStore : IStagingBackupStore
{
    private readonly IFileSystem _fs;
    private readonly string _rootDirectory;
    private readonly TimeProvider _timeProvider;

    public StagingBackupStore(IFileSystem fs, string rootDirectory, TimeProvider? timeProvider = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _rootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
            ? throw new ArgumentException("Staging root directory cannot be empty.", nameof(rootDirectory))
            : rootDirectory;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string GetPlannedBackupPath(string sourcePath, Guid operationId, int itemIndex)
    {
        string opDir = Path.Combine(_rootDirectory, operationId.ToString("D"));
        string originalName = Path.GetFileName(sourcePath);
        string ext = Path.GetExtension(originalName);
        string nameWithoutExt = Path.GetFileNameWithoutExtension(originalName);
        if (nameWithoutExt.Length > 80)
        {
            nameWithoutExt = nameWithoutExt.Substring(0, 80);
        }
        string backupFileName = $"{itemIndex}_{nameWithoutExt}{ext}";
        return Path.Combine(opDir, backupFileName);
    }

    public async Task<StagedBackup> StageAsync(string sourcePath, Guid operationId, int itemIndex, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("Source path cannot be empty.", nameof(sourcePath));
        }

        if (_fs.DirectoryExists(sourcePath))
        {
            throw new InvalidOperationException($"Cannot stage a directory: '{sourcePath}'. Only files may be staged.");
        }

        var sourceInfo = _fs.GetInfo(sourcePath);
        if (sourceInfo == null || !sourceInfo.Exists)
        {
            throw new FileNotFoundException($"File to stage does not exist: '{sourcePath}'.", sourcePath);
        }

        string opDir = Path.Combine(_rootDirectory, operationId.ToString("D"));
        if (!_fs.DirectoryExists(opDir))
        {
            _fs.CreateDirectory(opDir);
        }

        string backupPath = GetPlannedBackupPath(sourcePath, operationId, itemIndex);

        bool sameVolume = string.Equals(
            _fs.GetVolumeId(sourcePath),
            _fs.GetVolumeId(opDir),
            StringComparison.OrdinalIgnoreCase);

        if (sameVolume)
        {
            _fs.Move(sourcePath, backupPath, overwrite: false);
        }
        else
        {
            // Cross-volume staging: verify free space
            long freeSpace = _fs.GetFreeSpace(_fs.GetVolumeId(opDir));
            long requiredSpace = sourceInfo.Size + (1024 * 1024); // 1 MiB safety buffer
            if (freeSpace < requiredSpace)
            {
                throw new IOException($"Insufficient disk space on staging drive ({freeSpace} bytes free, {requiredSpace} required).");
            }

            // Copy with hash verification, then delete source
            try
            {
                await CopyVerifiedAsync(sourcePath, backupPath, sourceInfo.Size, sourceInfo.Modified, ct).ConfigureAwait(false);
                _fs.Delete(sourcePath);
            }
            catch
            {
                try
                {
                    if (_fs.FileExists(backupPath))
                    {
                        _fs.Delete(backupPath);
                    }
                }
                catch
                {
                    // Best effort cleanup
                }
                throw;
            }
        }

        return new StagedBackup(backupPath, sourceInfo.Size, sourceInfo.Modified);
    }

    public async Task RestoreAsync(string backupPath, string targetPath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (!_fs.FileExists(backupPath))
        {
            throw new FileNotFoundException($"Staged backup file not found: '{backupPath}'.", backupPath);
        }

        string? targetDir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(targetDir) && !_fs.DirectoryExists(targetDir))
        {
            _fs.CreateDirectory(targetDir);
        }

        var backupInfo = _fs.GetInfo(backupPath);
        long size = backupInfo?.Size ?? 0;
        DateTime modified = backupInfo?.Modified ?? DateTime.UtcNow;

        bool sameVolume = string.Equals(
            _fs.GetVolumeId(backupPath),
            _fs.GetVolumeId(targetPath),
            StringComparison.OrdinalIgnoreCase);

        if (sameVolume)
        {
            _fs.Move(backupPath, targetPath, overwrite: false);
        }
        else
        {
            await CopyVerifiedAsync(backupPath, targetPath, size, modified, ct).ConfigureAwait(false);
            _fs.Delete(backupPath);
        }
    }

    public Task DiscardAsync(Guid operationId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        string opDir = Path.Combine(_rootDirectory, operationId.ToString("D"));
        try
        {
            if (_fs.DirectoryExists(opDir))
            {
                _fs.DeleteDirectory(opDir, recursive: true);
            }
        }
        catch
        {
            // Best effort discard
        }

        return Task.CompletedTask;
    }

    public Task PurgeAsync(StagingPolicy policy, IReadOnlySet<Guid>? liveOperationIds = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (!_fs.DirectoryExists(_rootDirectory))
        {
            return Task.CompletedTask;
        }

        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
        var opDirs = new List<(string Path, Guid Id, DateTime Modified, long Size)>();

        foreach (var entry in _fs.EnumerateEntries(_rootDirectory))
        {
            ct.ThrowIfCancellationRequested();

            if (!_fs.DirectoryExists(entry)) continue;

            string dirName = Path.GetFileName(entry);
            if (!Guid.TryParse(dirName, out var opId)) continue;

            if (liveOperationIds != null && liveOperationIds.Contains(opId))
            {
                continue;
            }

            var dirInfo = _fs.GetInfo(entry);
            DateTime modified = dirInfo?.Modified ?? now;
            long dirSize = 0;

            try
            {
                foreach (var file in _fs.EnumerateEntries(entry))
                {
                    if (_fs.FileExists(file))
                    {
                        var fi = _fs.GetInfo(file);
                        if (fi != null)
                        {
                            dirSize += fi.Size;
                            if (fi.Modified > modified) modified = fi.Modified;
                        }
                    }
                }
            }
            catch
            {
                // Best effort size query
            }

            // 1. Purge by age
            if (now - modified > policy.MaxAge)
            {
                try
                {
                    _fs.DeleteDirectory(entry, recursive: true);
                }
                catch
                {
                    // Best effort
                }
                continue;
            }

            opDirs.Add((entry, opId, modified, dirSize));
        }

        // 2. Purge by quota (oldest first)
        long totalBytes = opDirs.Sum(d => d.Size);
        if (totalBytes > policy.MaxTotalBytes)
        {
            var sorted = opDirs.OrderBy(d => d.Modified).ToList();
            foreach (var op in sorted)
            {
                ct.ThrowIfCancellationRequested();
                if (totalBytes <= policy.MaxTotalBytes) break;

                try
                {
                    _fs.DeleteDirectory(op.Path, recursive: true);
                    totalBytes -= op.Size;
                }
                catch
                {
                    // Best effort
                }
            }
        }

        return Task.CompletedTask;
    }

    public bool BackupExists(string backupPath)
    {
        if (string.IsNullOrWhiteSpace(backupPath)) return false;
        return _fs.FileExists(backupPath);
    }

    private async Task CopyVerifiedAsync(string source, string destination, long expectedSize, DateTime modified, CancellationToken ct)
    {
        byte[] buffer = new byte[64 * 1024];
        byte[] sourceHash;

        using (var inStream = _fs.OpenRead(source))
        using (var outStream = _fs.CreateFileForWrite(destination))
        using (var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            int read;
            long totalRead = 0;
            while ((read = await inStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
            {
                await outStream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                hasher.AppendData(buffer, 0, read);
                totalRead += read;
            }
            sourceHash = hasher.GetHashAndReset();

            if (totalRead != expectedSize)
            {
                throw new IOException($"Staging copy size mismatch for '{source}': expected {expectedSize}, wrote {totalRead}.");
            }
        }

        // Verify destination hash
        byte[] destHash;
        using (var verifyStream = _fs.OpenRead(destination))
        using (var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            int read;
            while ((read = await verifyStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
            {
                hasher.AppendData(buffer, 0, read);
            }
            destHash = hasher.GetHashAndReset();
        }

        if (!sourceHash.AsSpan().SequenceEqual(destHash))
        {
            throw new IOException($"Staging verification failed: SHA-256 hash mismatch for '{source}'.");
        }

        _fs.SetModified(destination, modified);
    }
}
