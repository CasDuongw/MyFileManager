using System.IO;

namespace FM.Core.FileSystem;

public record FileItemInfo(
    bool Exists,
    bool IsDirectory,
    long Size,
    DateTime Modified,
    FileAttributes Attributes,
    bool IsReparsePoint);

/// <summary>
/// Abstraction for filesystem operations, enabling pure in-memory testing of file operations
/// without touching physical storage.
/// </summary>
public interface IFileSystem
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    bool Exists(string path);

    void CreateDirectory(string path);
    void DeleteDirectory(string path, bool recursive = false);
    void DeleteFile(string path);

    void MoveFile(string sourcePath, string destinationPath);
    void MoveDirectory(string sourcePath, string destinationPath);

    bool IsDirectoryEmpty(string path);
    IEnumerable<string> EnumerateEntries(string path);

    // Transfer Engine extensions
    FileItemInfo? GetInfo(string path);
    Stream OpenRead(string path);
    Stream CreateFileForWrite(string path);
    Stream OpenAppend(string path);
    void SetModified(string path, DateTime time);
    void Delete(string path);
    void Move(string sourcePath, string destinationPath, bool overwrite = false);
    string GetVolumeId(string path);
    string GetVolumeRoot(string path);
    long GetFreeSpace(string path);
    void SetHidden(string path);
}
