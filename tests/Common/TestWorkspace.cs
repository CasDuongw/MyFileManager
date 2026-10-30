using System.IO;

namespace FM.Tests.Common;

/// <summary>
/// Manages a temporary on-disk workspace for tests under %TEMP%\FM_{purpose}_{guid}.
/// Strictly enforces a hard budget of max 200 files and 1 MiB total size.
/// Automatically cleans up on Dispose and sweeps stale FM_* folders older than 1 hour on startup.
/// </summary>
public sealed class TestWorkspace : IDisposable
{
    public const int MaxFiles = 200;
    public const long MaxBytes = 1024 * 1024; // 1 MiB

    private static int _sweptOnce;

    public string RootPath { get; }
    private int _fileCount;
    private long _totalBytes;
    private bool _disposed;

    public int FileCount => _fileCount;
    public long TotalBytes => _totalBytes;

    public TestWorkspace(string purpose = "workspace")
    {
        EnsureStaleWorkspacesSwept();

        RootPath = Path.Combine(Path.GetTempPath(), $"FM_{purpose}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(RootPath);
    }

    public string CreateDirectory(string relativePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        string fullPath = Path.Combine(RootPath, relativePath);
        Directory.CreateDirectory(fullPath);
        return fullPath;
    }

    public string CreateFile(string relativePath, byte[]? content = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        byte[] bytes = content ?? Array.Empty<byte>();

        if (_fileCount + 1 > MaxFiles)
        {
            throw new InvalidOperationException(
                $"TestWorkspace budget exceeded! Maximum allowed files: {MaxFiles}. Attempted to create file #{_fileCount + 1}.");
        }

        if (_totalBytes + bytes.Length > MaxBytes)
        {
            throw new InvalidOperationException(
                $"TestWorkspace budget exceeded! Maximum allowed size: {MaxBytes} bytes (1 MiB). Attempted total: {_totalBytes + bytes.Length} bytes.");
        }

        string fullPath = Path.Combine(RootPath, relativePath);
        string? parentDir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
        {
            Directory.CreateDirectory(parentDir);
        }

        File.WriteAllBytes(fullPath, bytes);
        _fileCount++;
        _totalBytes += bytes.Length;

        return fullPath;
    }

    public string CreateTextFile(string relativePath, string text = "")
    {
        return CreateFile(relativePath, System.Text.Encoding.UTF8.GetBytes(text));
    }

    private static void EnsureStaleWorkspacesSwept()
    {
        if (Interlocked.Exchange(ref _sweptOnce, 1) == 0)
        {
            try
            {
                string tempDir = Path.GetTempPath();
                var staleDirs = Directory.GetDirectories(tempDir, "FM_*");
                var now = DateTime.UtcNow;

                foreach (var dir in staleDirs)
                {
                    try
                    {
                        var info = new DirectoryInfo(dir);
                        if (now - info.CreationTimeUtc > TimeSpan.FromHours(1))
                        {
                            Directory.Delete(dir, recursive: true);
                        }
                    }
                    catch
                    {
                        // Best effort deletion of stale folders
                    }
                }
            }
            catch
            {
                // Best effort sweep
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }
}
