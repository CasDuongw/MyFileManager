using FM.Core.Models;

namespace FM.Core.Services;

/// <summary>
/// Provides asynchronous, non-blocking file system enumeration.
/// </summary>
public interface IDirectoryService
{
    /// <summary>
    /// Enumerates all accessible file entries in the specified directory path.
    /// Inaccessible entries are skipped without throwing exceptions.
    /// </summary>
    /// <param name="path">Absolute directory path to enumerate.</param>
    /// <param name="ct">Cancellation token to cancel ongoing enumeration.</param>
    /// <returns>An async stream of <see cref="FileEntry"/>.</returns>
    IAsyncEnumerable<FileEntry> EnumerateAsync(string path, CancellationToken ct = default);
}
