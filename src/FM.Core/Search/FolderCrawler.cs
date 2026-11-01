using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FM.Core.FileSystem;

namespace FM.Core.Search;

/// <summary>
/// Low-priority iterative background directory crawler.
/// Ingests directories without deep call-stack recursion, respects exclusion lists and reparse-point skipping,
/// and streams discovered directories into IFolderIndex in progressive batches.
/// </summary>
public sealed class FolderCrawler
{
    private readonly IFileSystem _fileSystem;
    private readonly IFolderIndex _index;

    public static readonly string[] DefaultExcludes =
    [
        "$Recycle.Bin",
        "System Volume Information",
        "Windows",
        "Windows.old",
        "Program Files",
        "Program Files (x86)",
        "ProgramData",
        "AppData",
        "node_modules",
        ".git",
        ".vs",
        "bin",
        "obj",
        ".gradle",
        "__pycache__"
    ];

    public FolderCrawler(IFileSystem fileSystem, IFolderIndex index)
    {
        _fileSystem = fileSystem;
        _index = index;
    }

    public async Task CrawlAsync(
        IEnumerable<string> roots,
        IEnumerable<string>? excludes = null,
        int batchSize = 500,
        CancellationToken cancellationToken = default)
    {
        var excludeSet = new HashSet<string>(excludes ?? DefaultExcludes, StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();

        foreach (var r in roots)
        {
            if (string.IsNullOrWhiteSpace(r)) continue;
            string norm = r.TrimEnd('\\', '/');
            string rootName = Path.GetFileName(norm);
            if (!string.IsNullOrEmpty(rootName) && excludeSet.Contains(rootName)) continue;
            if (visited.Add(norm))
            {
                queue.Enqueue(norm);
            }
        }

        if (queue.Count == 0)
        {
            _index.State = FolderIndexState.Ready;
            return;
        }

        _index.State = FolderIndexState.Building;

        await Task.Run(() =>
        {
            var batch = new List<string>(batchSize);

            while (queue.Count > 0)
            {
                if (cancellationToken.IsCancellationRequested) break;

                string currentDir = queue.Dequeue();

                // Add current folder to batch (except drive roots if already present)
                batch.Add(currentDir);
                if (batch.Count >= batchSize)
                {
                    _index.BulkAdd(batch);
                    batch.Clear();
                    // Yield briefly to avoid hogging CPU
                    Thread.Yield();
                }

                // Enumerate child directories
                IEnumerable<string> children;
                try
                {
                    children = _fileSystem.EnumerateDirectories(currentDir);
                }
                catch
                {
                    continue;
                }

                foreach (var child in children)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    if (string.IsNullOrWhiteSpace(child)) continue;

                    string childName = Path.GetFileName(child.TrimEnd('\\', '/'));
                    if (excludeSet.Contains(childName)) continue;

                    if (visited.Add(child))
                    {
                        queue.Enqueue(child);
                    }
                }
            }

            if (batch.Count > 0)
            {
                _index.BulkAdd(batch);
                batch.Clear();
            }

            _index.State = FolderIndexState.Ready;
        }, cancellationToken);
    }
}
