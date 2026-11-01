using System;
using System.Collections.Generic;
using System.Linq;

namespace FM.Core.Search;

/// <summary>
/// Thread-safe in-memory index holding pre-indexed folders with lock-free atomic snapshots for queries.
/// Capable of scaling to 200,000+ folders with instant sub-millisecond to low-millisecond queries.
/// </summary>
public sealed class FolderIndex : IFolderIndex
{
    private readonly Dictionary<string, FolderIndexEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private FolderIndexEntry[] _snapshot = Array.Empty<FolderIndexEntry>();
    private readonly object _lock = new();

    private FolderIndexState _state = FolderIndexState.Idle;
    public FolderIndexState State
    {
        get => _state;
        set
        {
            if (_state != value)
            {
                _state = value;
                StateChanged?.Invoke();
            }
        }
    }

    public int Count => _snapshot.Length;

    public string StatusMessage => State switch
    {
        FolderIndexState.Building => $"Indexing... {Count:N0} folders",
        FolderIndexState.Ready => $"Indexed {Count:N0} folders",
        _ => Count > 0 ? $"Indexed {Count:N0} folders" : "Index idle"
    };

    public event Action? StateChanged;

    public void Add(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        lock (_lock)
        {
            if (!_entries.ContainsKey(path))
            {
                _entries[path] = FolderIndexEntry.Create(path);
                _snapshot = _entries.Values.ToArray();
            }
        }
        StateChanged?.Invoke();
    }

    public void BulkAdd(IEnumerable<string> paths)
    {
        bool changed = false;
        lock (_lock)
        {
            foreach (var p in paths)
            {
                if (!string.IsNullOrWhiteSpace(p) && !_entries.ContainsKey(p))
                {
                    _entries[p] = FolderIndexEntry.Create(p);
                    changed = true;
                }
            }

            if (changed)
            {
                _snapshot = _entries.Values.ToArray();
            }
        }

        if (changed)
        {
            StateChanged?.Invoke();
        }
    }

    public void Remove(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        bool changed = false;
        lock (_lock)
        {
            if (_entries.Remove(path))
            {
                changed = true;
                _snapshot = _entries.Values.ToArray();
            }
        }
        if (changed)
        {
            StateChanged?.Invoke();
        }
    }

    public void Rename(string oldPath, string newPath)
    {
        if (string.IsNullOrWhiteSpace(oldPath) || string.IsNullOrWhiteSpace(newPath)) return;
        bool changed = false;
        lock (_lock)
        {
            if (_entries.Remove(oldPath))
            {
                _entries[newPath] = FolderIndexEntry.Create(newPath);
                changed = true;
                _snapshot = _entries.Values.ToArray();
            }
        }
        if (changed)
        {
            StateChanged?.Invoke();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
            _snapshot = Array.Empty<FolderIndexEntry>();
        }
        StateChanged?.Invoke();
    }

    public void SetEntries(IEnumerable<FolderIndexEntry> entries)
    {
        lock (_lock)
        {
            _entries.Clear();
            foreach (var e in entries)
            {
                _entries[e.FullPath] = e;
            }
            _snapshot = _entries.Values.ToArray();
        }
        StateChanged?.Invoke();
    }

    public IReadOnlyList<FolderIndexEntry> Snapshot() => _snapshot;

    public List<ScoredResult<FolderIndexEntry>> Search(PreparedQuery query, int maxResults)
    {
        var currentSnapshot = _snapshot;
        if (currentSnapshot.Length == 0 || query.IsEmpty)
        {
            return new List<ScoredResult<FolderIndexEntry>>();
        }

        return PreparedMatcher.SelectTopK(
            currentSnapshot,
            query,
            maxResults,
            e => (e.Name, e.NormName, e.Bitmask, e.RawParent, e.NormParent),
            e => Math.Max(-40.0, -e.Depth * 2.0)); // Slight shallower-is-better boost
    }
}

