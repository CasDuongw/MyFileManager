namespace FM.Core.Models;

/// <summary>
/// Immutable representation of a file system entry.
/// </summary>
/// <param name="Name">The file or directory name including extension.</param>
/// <param name="FullPath">The absolute file system path.</param>
/// <param name="IsDirectory">True if this entry is a directory, false if a file.</param>
/// <param name="Size">Size in bytes (0 for directories).</param>
/// <param name="Modified">Last modified date and time.</param>
public record FileEntry(string Name, string FullPath, bool IsDirectory, long Size, DateTime Modified);
