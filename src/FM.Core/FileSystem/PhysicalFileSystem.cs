using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace FM.Core.FileSystem;

/// <summary>
/// Real physical filesystem implementation for runtime execution on Windows.
/// Uses asynchronous sequential FileStreams, Win32 volume identification, and long path normalization.
/// </summary>
public sealed class PhysicalFileSystem : IFileSystem
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumePathNameW(
        string lpszFileName,
        [Out] StringBuilder lpszVolumePathName,
        uint cchBufferLength);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformationW(
        string lpRootPathName,
        [Out] StringBuilder? lpVolumeNameBuffer,
        uint nVolumeNameSize,
        out uint lpVolumeSerialNumber,
        out uint lpMaximumComponentLength,
        out uint lpFileSystemFlags,
        [Out] StringBuilder? lpFileSystemNameBuffer,
        uint nFileSystemNameSize);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceExW(
        string lpDirectoryName,
        out ulong lpFreeBytesAvailableToCaller,
        out ulong lpTotalNumberOfBytes,
        out ulong lpTotalNumberOfFreeBytes);

    public static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        string fullPath = Path.GetFullPath(path);

        if (OperatingSystem.IsWindows() && fullPath.Length >= 240 && !fullPath.StartsWith(@"\\?\") && !fullPath.StartsWith(@"\\.\"))
        {
            if (fullPath.StartsWith(@"\\"))
            {
                return @"\\?\UNC\" + fullPath.Substring(2);
            }
            return @"\\?\" + fullPath;
        }

        return fullPath;
    }

    public bool FileExists(string path) => File.Exists(NormalizePath(path));

    public bool DirectoryExists(string path) => Directory.Exists(NormalizePath(path));

    public bool Exists(string path)
    {
        string norm = NormalizePath(path);
        return File.Exists(norm) || Directory.Exists(norm);
    }

    public void CreateDirectory(string path) => Directory.CreateDirectory(NormalizePath(path));

    public void DeleteDirectory(string path, bool recursive = false) => Directory.Delete(NormalizePath(path), recursive);

    public void DeleteFile(string path) => File.Delete(NormalizePath(path));

    public void MoveFile(string sourcePath, string destinationPath) =>
        File.Move(NormalizePath(sourcePath), NormalizePath(destinationPath));

    public void MoveDirectory(string sourcePath, string destinationPath) =>
        Directory.Move(NormalizePath(sourcePath), NormalizePath(destinationPath));

    public bool IsDirectoryEmpty(string path)
    {
        string norm = NormalizePath(path);
        if (!Directory.Exists(norm)) return true;
        return !Directory.EnumerateFileSystemEntries(norm).Any();
    }

    public IEnumerable<string> EnumerateEntries(string path)
    {
        string norm = NormalizePath(path);
        if (!Directory.Exists(norm)) return Enumerable.Empty<string>();
        return Directory.EnumerateFileSystemEntries(norm);
    }

    public FileItemInfo? GetInfo(string path)
    {
        string norm = NormalizePath(path);
        try
        {
            var attr = File.GetAttributes(norm);
            bool isDir = (attr & FileAttributes.Directory) == FileAttributes.Directory;
            bool isReparse = (attr & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;

            if (isDir)
            {
                var di = new DirectoryInfo(norm);
                return new FileItemInfo(true, true, 0, di.LastWriteTimeUtc, attr, isReparse);
            }
            else
            {
                var fi = new FileInfo(norm);
                return new FileItemInfo(true, false, fi.Length, fi.LastWriteTimeUtc, attr, isReparse);
            }
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public Stream OpenRead(string path)
    {
        string norm = NormalizePath(path);
        return new FileStream(
            norm,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    public Stream CreateFileForWrite(string path)
    {
        string norm = NormalizePath(path);
        string? parent = Path.GetDirectoryName(norm);
        if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
        {
            Directory.CreateDirectory(parent);
        }

        return new FileStream(
            norm,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    public Stream OpenAppend(string path)
    {
        string norm = NormalizePath(path);
        string? parent = Path.GetDirectoryName(norm);
        if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
        {
            Directory.CreateDirectory(parent);
        }

        return new FileStream(
            norm,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    public void SetModified(string path, DateTime time)
    {
        string norm = NormalizePath(path);
        DateTime utc = time.Kind == DateTimeKind.Utc ? time : time.ToUniversalTime();
        if (Directory.Exists(norm))
        {
            Directory.SetLastWriteTimeUtc(norm, utc);
        }
        else if (File.Exists(norm))
        {
            File.SetLastWriteTimeUtc(norm, utc);
        }
    }

    public void Delete(string path)
    {
        string norm = NormalizePath(path);
        if (Directory.Exists(norm))
        {
            Directory.Delete(norm, recursive: false);
        }
        else if (File.Exists(norm))
        {
            File.Delete(norm);
        }
    }

    public void Move(string sourcePath, string destinationPath, bool overwrite = false)
    {
        string src = NormalizePath(sourcePath);
        string dst = NormalizePath(destinationPath);

        if (Directory.Exists(src))
        {
            if (overwrite && Directory.Exists(dst))
            {
                Directory.Delete(dst, true);
            }
            Directory.Move(src, dst);
        }
        else
        {
            File.Move(src, dst, overwrite);
        }
    }

    public string GetVolumeId(string path)
    {
        string norm = NormalizePath(path);
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var sb = new StringBuilder(260);
                if (GetVolumePathNameW(norm, sb, (uint)sb.Capacity))
                {
                    string volumePath = sb.ToString();
                    if (GetVolumeInformationW(volumePath, null, 0, out uint serial, out _, out _, null, 0))
                    {
                        return $"{volumePath.TrimEnd('\\').ToUpperInvariant()}_{serial:X8}";
                    }
                    return volumePath.TrimEnd('\\').ToUpperInvariant();
                }
            }
            catch
            {
                // Fall back to drive root if P/Invoke fails
            }
        }

        string? root = Path.GetPathRoot(norm);
        return !string.IsNullOrEmpty(root) ? root.TrimEnd('\\').ToUpperInvariant() : "VOL_DEFAULT";
    }

    public long GetFreeSpace(string path)
    {
        string norm = NormalizePath(path);
        if (OperatingSystem.IsWindows())
        {
            try
            {
                if (GetDiskFreeSpaceExW(norm, out ulong freeBytes, out _, out _))
                {
                    return (long)freeBytes;
                }
            }
            catch
            {
                // Fall back to DriveInfo
            }
        }

        try
        {
            string? root = Path.GetPathRoot(norm);
            if (!string.IsNullOrEmpty(root))
            {
                return new DriveInfo(root).AvailableFreeSpace;
            }
        }
        catch
        {
            // If drive cannot be queried, assume abundant space
        }

        return long.MaxValue;
    }
}
