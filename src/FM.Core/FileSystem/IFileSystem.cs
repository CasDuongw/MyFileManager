namespace FM.Core.FileSystem;

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
}

