using FM.Core.Models;

namespace FM.Core.Services;

/// <summary>
/// Provides asynchronous, non-blocking file system enumeration.
/// </summary>
public interface IDirectoryService
{
    /// <summary>
    /// Enumerates accessible file entries in batches from a dedicated background producer.
    /// Inaccessible entries are skipped without throwing exceptions.
    /// </summary>
    /// <param name="path">Absolute directory path to enumerate.</param>
    /// <param name="batchSize">Maximum number of entries per batch.</param>
    /// <param name="ct">Cancellation token to cancel ongoing enumeration.</param>
    /// <returns>An async stream of <see cref="IReadOnlyList{FileEntry}"/> batches.</returns>
    IAsyncEnumerable<IReadOnlyList<FileEntry>> EnumerateBatchesAsync(string path, int batchSize = 200, CancellationToken ct = default);
}
