using System.IO;

namespace FM.Core.FileSystem;

/// <summary>
/// Real physical filesystem implementation for runtime execution on Windows.
/// </summary>
public sealed class PhysicalFileSystem : IFileSystem
{
    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void DeleteDirectory(string path, bool recursive = false) => Directory.Delete(path, recursive);

    public void DeleteFile(string path) => File.Delete(path);

    public void MoveFile(string sourcePath, string destinationPath) => File.Move(sourcePath, destinationPath);

    public void MoveDirectory(string sourcePath, string destinationPath) => Directory.Move(sourcePath, destinationPath);

    public bool IsDirectoryEmpty(string path)
    {
        if (!Directory.Exists(path)) return true;
        return !Directory.EnumerateFileSystemEntries(path).Any();
    }

    public IEnumerable<string> EnumerateEntries(string path)
    {
        if (!Directory.Exists(path)) return Enumerable.Empty<string>();
        return Directory.EnumerateFileSystemEntries(path);
    }
}
