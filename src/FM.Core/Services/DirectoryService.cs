using System.Runtime.CompilerServices;
using FM.Core.Models;

namespace FM.Core.Services;

/// <summary>
/// Default implementation of <see cref="IDirectoryService"/> using .NET standard file system APIs.
/// Honors cancellation, ignores inaccessible entries, and yields execution periodically to prevent UI freezes.
/// </summary>
public class DirectoryService : IDirectoryService
{
    public async IAsyncEnumerable<FileEntry> EnumerateAsync(
        string path,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path cannot be null or empty.", nameof(path));
        }

        // Shift execution to the thread pool immediately to never block the caller (UI thread)
        await Task.Yield();
        ct.ThrowIfCancellationRequested();

        string normalizedPath = NormalizePath(path);

        if (!Directory.Exists(normalizedPath))
        {
            throw new DirectoryNotFoundException($"Directory not found: '{path}'");
        }

        var dirInfo = new DirectoryInfo(normalizedPath);

        // EnumerationOptions with IgnoreInaccessible ensures Windows API level access errors are ignored
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false,
            AttributesToSkip = 0 // Include hidden and system files for full file manager visibility
        };

        IEnumerator<FileSystemInfo> enumerator;
        try
        {
            enumerator = dirInfo.EnumerateFileSystemInfos("*", options).GetEnumerator();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // Re-throw if the root directory itself cannot be opened, allowing the caller to show "Access Denied"
            throw;
        }

        using (enumerator)
        {
            int counter = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                FileSystemInfo current;
                try
                {
                    if (!enumerator.MoveNext())
                    {
                        break;
                    }
                    current = enumerator.Current;
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    // Skip entries that fail during MoveNext (e.g. locked files or inaccessible junction targets)
                    continue;
                }

                FileEntry? entry = null;
                try
                {
                    bool isDirectory = (current.Attributes & FileAttributes.Directory) == FileAttributes.Directory;
                    long size = 0;

                    if (!isDirectory && current is FileInfo fileInfo)
                    {
                        try
                        {
                            size = fileInfo.Length;
                        }
                        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                        {
                            size = 0;
                        }
                    }

                    DateTime modified = DateTime.MinValue;
                    try
                    {
                        modified = current.LastWriteTime;
                    }
                    catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                    {
                        modified = DateTime.MinValue;
                    }

                    entry = new FileEntry(current.Name, current.FullName, isDirectory, size, modified);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    // Skip individual entry if attribute resolution fails
                    continue;
                }

                if (entry != null)
                {
                    yield return entry;
                }

                // Periodically yield thread to keep thread pool balanced and responsive when reading 50,000+ files
                if (++counter % 256 == 0)
                {
                    await Task.Yield();
                }
            }
        }
    }

    /// <summary>
    /// Normalizes path and prepends the Win32 extended path prefix (\\?\) for paths >= 240 chars.
    /// </summary>
    private static string NormalizePath(string path)
    {
        string fullPath = Path.GetFullPath(path);

        // Prepend \\?\ if path is long and rooted on Windows to bypass standard MAX_PATH (260 char) limitation
        if (OperatingSystem.IsWindows() && fullPath.Length >= 240 && !fullPath.StartsWith(@"\\?\") && !fullPath.StartsWith(@"\\.\"))
        {
            if (fullPath.StartsWith(@"\\"))
            {
                // UNC path: \\server\share -> \\?\UNC\server\share
                return @"\\?\UNC\" + fullPath.Substring(2);
            }
            return @"\\?\" + fullPath;
        }

        return fullPath;
    }
}
