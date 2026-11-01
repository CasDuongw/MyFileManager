using System.IO;

namespace FM.Core.Search;

/// <summary>
/// Thread-safe, in-memory Most Recently Used (MRU) folder tracker.
/// Enforces path normalization, case-insensitive deduplication, and a capacity cap of 10.
/// Pure Core with zero filesystem I/O.
/// </summary>
public sealed class RecentFolderList
{
    public const int DefaultCapacity = 10;
    private readonly int _capacity;
    private readonly List<string> _items = new();
    private readonly object _lock = new();

    public RecentFolderList(int capacity = DefaultCapacity, IEnumerable<string>? initialItems = null)
    {
        _capacity = Math.Max(1, capacity);
        if (initialItems != null)
        {
            foreach (var item in initialItems)
            {
                AddInternal(item);
            }
        }
    }

    /// <summary>
    /// Returns an immutable snapshot of recent folder paths in MRU order (most recent first).
    /// </summary>
    public IReadOnlyList<string> Items
    {
        get
        {
            lock (_lock)
            {
                return _items.ToArray();
            }
        }
    }

    /// <summary>
    /// Adds or moves a folder path to the front of the MRU list.
    /// Normalizes path, strips trailing slashes (except drive roots), and caps list at 10 items.
    /// </summary>
    public void Add(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        lock (_lock)
        {
            AddInternal(path);
        }
    }

    /// <summary>
    /// Clears all entries from the MRU list.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _items.Clear();
        }
    }

    private void AddInternal(string path)
    {
        string normalized = NormalizeFolderPath(path);
        if (string.IsNullOrEmpty(normalized)) return;

        string finalPath = normalized;
        // Remove if existing (case-insensitive) to move to the front, preserving original casing
        for (int i = 0; i < _items.Count; i++)
        {
            if (string.Equals(_items[i], normalized, StringComparison.OrdinalIgnoreCase))
            {
                finalPath = _items[i];
                _items.RemoveAt(i);
                break;
            }
        }

        _items.Insert(0, finalPath);

        while (_items.Count > _capacity)
        {
            _items.RemoveAt(_items.Count - 1);
        }
    }

    public static string NormalizeFolderPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;

        try
        {
            string full = Path.GetFullPath(path.Trim());
            string root = Path.GetPathRoot(full) ?? string.Empty;

            // Retain trailing slash for drive roots (e.g. C:\)
            if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(full.TrimEnd('\\', '/'), root.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            {
                return root.EndsWith('\\') || root.EndsWith('/') ? root : root + "\\";
            }

            return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
