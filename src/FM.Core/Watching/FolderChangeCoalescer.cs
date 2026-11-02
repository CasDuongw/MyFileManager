namespace FM.Core.Watching;

/// <summary>
/// Coalesces bursts of raw file watcher events into a deduplicated, normalized set of net operations.
/// Prevents UI refresh storms and redundant operations when multiple events arrive for the same path.
/// </summary>
public static class FolderChangeCoalescer
{
    public class CoalescedResult
    {
        public List<string> Added { get; } = new();
        public List<string> Removed { get; } = new();
        public List<(string OldPath, string NewPath)> Renamed { get; } = new();
        public List<string> Modified { get; } = new();
        public bool RequiresFullRefresh { get; set; }
    }

    public static CoalescedResult Coalesce(IEnumerable<FolderChangeEventArgs> events, int maxBatchSize = 5000)
    {
        var result = new CoalescedResult();
        if (events == null) return result;

        var eventList = events as IList<FolderChangeEventArgs> ?? events.ToList();
        if (eventList.Count == 0) return result;

        if (eventList.Count > maxBatchSize)
        {
            result.RequiresFullRefresh = true;
            return result;
        }

        // Track lifecycle per path: Added, Removed, Modified, or Renamed
        // Path -> current state
        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var modified = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // newPath -> oldPath
        var renames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var ev in eventList)
        {
            switch (ev.ChangeType)
            {
                case FolderChangeType.Created:
                    if (removed.Contains(ev.FullPath))
                    {
                        // Was removed earlier in batch, now created again -> treated as Modified
                        removed.Remove(ev.FullPath);
                        modified.Add(ev.FullPath);
                    }
                    else
                    {
                        added.Add(ev.FullPath);
                    }
                    break;

                case FolderChangeType.Deleted:
                    if (added.Contains(ev.FullPath))
                    {
                        // Created then deleted within the same burst -> cancels out
                        added.Remove(ev.FullPath);
                        modified.Remove(ev.FullPath);
                    }
                    else if (renames.TryGetValue(ev.FullPath, out var origOldPath))
                    {
                        // Renamed from A to B, then B deleted -> A deleted
                        renames.Remove(ev.FullPath);
                        removed.Add(origOldPath);
                    }
                    else
                    {
                        removed.Add(ev.FullPath);
                        modified.Remove(ev.FullPath);
                    }
                    break;

                case FolderChangeType.Renamed:
                    string oldPath = ev.OldFullPath ?? string.Empty;
                    string newPath = ev.FullPath;

                    if (string.IsNullOrEmpty(oldPath))
                    {
                        // Missing old path, treat as creation
                        added.Add(newPath);
                        break;
                    }

                    if (added.Contains(oldPath))
                    {
                        // Created as A, then renamed to B -> Created as B
                        added.Remove(oldPath);
                        added.Add(newPath);
                    }
                    else if (renames.TryGetValue(oldPath, out var rootOldPath))
                    {
                        // Renamed A -> B, then B -> C => A -> C
                        renames.Remove(oldPath);
                        renames[newPath] = rootOldPath;
                    }
                    else
                    {
                        renames[newPath] = oldPath;
                    }
                    break;

                case FolderChangeType.Changed:
                    // If already added, the addition will read latest info anyway
                    if (!added.Contains(ev.FullPath) && !removed.Contains(ev.FullPath))
                    {
                        modified.Add(ev.FullPath);
                    }
                    break;
            }
        }

        result.Added.AddRange(added);
        result.Removed.AddRange(removed);
        foreach (var kvp in renames)
        {
            result.Renamed.Add((kvp.Value, kvp.Key));
        }
        result.Modified.AddRange(modified);

        return result;
    }
}

