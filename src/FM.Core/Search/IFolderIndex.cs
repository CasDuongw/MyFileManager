using System;
using System.Collections.Generic;

namespace FM.Core.Search;

public enum FolderIndexState
{
    Idle,
    Building,
    Ready
}

/// <summary>
/// Thread-safe in-memory index for fast folder lookups.
/// Supports atomic snapshots and concurrent reads during background crawl updates.
/// </summary>
public interface IFolderIndex
{
    int Count { get; }
    FolderIndexState State { get; set; }
    string StatusMessage { get; }
    event Action? StateChanged;

    void Add(string path);
    void BulkAdd(IEnumerable<string> paths);
    void Remove(string path);
    void Rename(string oldPath, string newPath);
    void Clear();
    void SetEntries(IEnumerable<FolderIndexEntry> entries);

    IReadOnlyList<FolderIndexEntry> Snapshot();
    List<ScoredResult<FolderIndexEntry>> Search(PreparedQuery query, int maxResults);
}

