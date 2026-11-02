using FM.Core.Models;

namespace FM.Core.Helpers;

/// <summary>
/// Sorting utilities for FileEntry collections.
/// Guarantees that directories always appear before regular files regardless of sort direction.
/// </summary>
public static class FileEntrySorter
{
    public static IEnumerable<FileEntry> OrderEntries(
        this IEnumerable<FileEntry> entries,
        FileSortColumn column,
        bool ascending = true)
    {
        if (entries == null) throw new ArgumentNullException(nameof(entries));

        // Folders ALWAYS appear above files (IsDirectory: true before false)
        var ordered = entries.OrderByDescending(e => e.IsDirectory);

        return (column, ascending) switch
        {
            (FileSortColumn.Name, true) => ordered.ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase),
            (FileSortColumn.Name, false) => ordered.ThenByDescending(e => e.Name, StringComparer.OrdinalIgnoreCase),

            (FileSortColumn.Size, true) => ordered.ThenBy(e => e.Size).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase),
            (FileSortColumn.Size, false) => ordered.ThenByDescending(e => e.Size).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase),

            (FileSortColumn.Modified, true) => ordered.ThenBy(e => e.Modified).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase),
            (FileSortColumn.Modified, false) => ordered.ThenByDescending(e => e.Modified).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase),

            (FileSortColumn.Type, true) => ordered.ThenBy(GetTypeKey, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase),
            (FileSortColumn.Type, false) => ordered.ThenByDescending(GetTypeKey, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase),

            _ => ordered.ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
        };
    }

    public static int CompareEntries(FileEntry a, FileEntry b, FileSortColumn column, bool ascending)
    {
        if (ReferenceEquals(a, b)) return 0;
        if (a is null) return -1;
        if (b is null) return 1;

        // Folders ALWAYS appear above files
        if (a.IsDirectory != b.IsDirectory)
        {
            return a.IsDirectory ? -1 : 1;
        }

        if (column == FileSortColumn.Name)
        {
            int nameCmp = StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
            return ascending ? nameCmp : -nameCmp;
        }

        int cmp = column switch
        {
            FileSortColumn.Size => a.Size.CompareTo(b.Size),
            FileSortColumn.Modified => a.Modified.CompareTo(b.Modified),
            FileSortColumn.Type => StringComparer.OrdinalIgnoreCase.Compare(GetTypeKey(a), GetTypeKey(b)),
            _ => 0
        };

        if (cmp != 0)
        {
            return ascending ? cmp : -cmp;
        }

        // Secondary tie-breaker: Name ascending (matches OrderEntries)
        return StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
    }

    public static int FindSortedIndex<T>(IReadOnlyList<T> items, FileEntry entry, Func<T, FileEntry> entrySelector, FileSortColumn column, bool ascending)
    {
        if (items == null || items.Count == 0) return 0;

        int low = 0;
        int high = items.Count - 1;

        while (low <= high)
        {
            int mid = low + ((high - low) >> 1);
            int cmp = CompareEntries(entry, entrySelector(items[mid]), column, ascending);

            if (cmp <= 0)
            {
                high = mid - 1;
            }
            else
            {
                low = mid + 1;
            }
        }

        return low;
    }

    private static string GetTypeKey(FileEntry entry)
    {
        if (entry.IsDirectory) return string.Empty;
        return Path.GetExtension(entry.Name) ?? string.Empty;
    }
}
