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

    private static string GetTypeKey(FileEntry entry)
    {
        if (entry.IsDirectory) return string.Empty;
        return Path.GetExtension(entry.Name) ?? string.Empty;
    }
}
