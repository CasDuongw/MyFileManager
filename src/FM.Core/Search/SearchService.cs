using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using FM.Core.FileSystem;
using FM.Core.Helpers;
using FM.Core.Models;

namespace FM.Core.Search;

/// <summary>
/// High-performance recursive search engine.
/// Traverses directories asynchronously, skips reparse points, captures inaccessible folders,
/// streams results progressively via bounded Channel, and respects maximum result limits.
/// Pure Core with zero WPF/UI dependencies.
/// </summary>
public sealed class SearchService : ISearchService
{
    private readonly IFileSystem _fileSystem;

    public int SkippedFolderCount { get; private set; }
    public bool IsResultsLimited { get; private set; }

    public SearchService(IFileSystem? fileSystem = null)
    {
        _fileSystem = fileSystem ?? new PhysicalFileSystem();
    }

    public async IAsyncEnumerable<IReadOnlyList<SearchResult>> SearchAsync(
        SearchQuery query,
        [EnumeratorCancellation] CancellationToken ct)
    {
        SkippedFolderCount = 0;
        IsResultsLimited = false;

        if (query.RootPaths == null || query.RootPaths.Count == 0)
        {
            yield break;
        }

        var canonicalRoots = DeduplicateRoots(query.RootPaths);
        if (canonicalRoots.Count == 0)
        {
            yield break;
        }

        var channel = Channel.CreateBounded<IReadOnlyList<SearchResult>>(new BoundedChannelOptions(10)
        {
            SingleWriter = true,
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait
        });

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var producerCt = linkedCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                var buffer = new List<SearchResult>();
                int currentBatchTarget = 25;
                int maxBatchSize = 250;
                int totalCount = 0;

                var dirQueue = new Queue<string>(canonicalRoots);

                while (dirQueue.Count > 0)
                {
                    producerCt.ThrowIfCancellationRequested();

                    if (totalCount >= query.MaxResults)
                    {
                        IsResultsLimited = true;
                        break;
                    }

                    string currentDir = dirQueue.Dequeue();

                    IEnumerable<string> entries;
                    try
                    {
                        entries = _fileSystem.EnumerateEntries(currentDir);
                    }
                    catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
                    {
                        SkippedFolderCount++;
                        continue;
                    }

                    foreach (var entryPath in entries)
                    {
                        producerCt.ThrowIfCancellationRequested();

                        if (totalCount >= query.MaxResults)
                        {
                            IsResultsLimited = true;
                            break;
                        }

                        FileItemInfo? info;
                        try
                        {
                            info = _fileSystem.GetInfo(entryPath);
                        }
                        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
                        {
                            continue;
                        }

                        if (info == null || !info.Exists) continue;

                        string name = Path.GetFileName(entryPath);
                        if (string.IsNullOrEmpty(name)) name = entryPath;

                        double score = string.IsNullOrWhiteSpace(query.QueryText)
                            ? 100.0
                            : NameMatcher.Score(name, query.QueryText);

                        bool isHidden = (info.Attributes & FileAttributes.Hidden) != 0;
                        bool isSystem = (info.Attributes & FileAttributes.System) != 0;
                        var fileEntry = new FileEntry(name, entryPath, info.IsDirectory, info.Size, info.Modified, isHidden, isSystem);

                        if (score > 0)
                        {
                            if (query.FilterPredicate == null || query.FilterPredicate(fileEntry))
                            {
                                buffer.Add(new SearchResult(fileEntry, currentDir, score));
                                totalCount++;

                                if (buffer.Count >= currentBatchTarget)
                                {
                                    await channel.Writer.WriteAsync(buffer.ToArray(), producerCt).ConfigureAwait(false);
                                    buffer.Clear();
                                    currentBatchTarget = Math.Min(currentBatchTarget * 2, maxBatchSize);
                                }
                            }
                        }

                        if (info.IsDirectory)
                        {
                            if (info.IsReparsePoint)
                            {
                                // Skip reparse points and symlinks to avoid circular traversal loops
                                continue;
                            }

                            dirQueue.Enqueue(entryPath);
                        }
                    }
                }

                if (buffer.Count > 0)
                {
                    await channel.Writer.WriteAsync(buffer.ToArray(), producerCt).ConfigureAwait(false);
                }

                channel.Writer.Complete();
            }
            catch (Exception ex)
            {
                channel.Writer.Complete(ex);
            }
        }, CancellationToken.None);

        try
        {
            while (await channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (channel.Reader.TryRead(out var batch))
                {
                    ct.ThrowIfCancellationRequested();
                    yield return batch;
                }
            }

            await channel.Reader.Completion.ConfigureAwait(false);
        }
        finally
        {
            linkedCts.Cancel();
        }
    }

    /// <summary>
    /// Deduplicates root paths so nested subdirectories are not redundantly traversed.
    /// </summary>
    public static List<string> DeduplicateRoots(IEnumerable<string> paths)
    {
        var normalized = paths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p.Length)
            .ToList();

        var result = new List<string>();
        foreach (var p in normalized)
        {
            bool isNested = false;
            foreach (var existing in result)
            {
                if (p.StartsWith(existing + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    p.StartsWith(existing + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    isNested = true;
                    break;
                }
            }
            if (!isNested)
            {
                result.Add(p);
            }
        }
        return result;
    }
}

