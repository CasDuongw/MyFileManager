using System.IO;
using FM.Core.FileSystem;

namespace FM.Core.Operations.MultiRename;

/// <summary>
/// Previews and validates batch renames, detecting invalid file names, batch collisions,
/// and disk collisions off the UI thread.
/// </summary>
public static class MultiRenamePlanner
{
    public static MultiRenamePlan Plan(
        IReadOnlyList<MultiRenameItemInput> items,
        MultiRenameOptions options,
        IFileSystem fs)
    {
        if (items == null || items.Count == 0)
        {
            return new MultiRenamePlan(Array.Empty<MultiRenamePlanItem>());
        }

        var planItems = new List<MultiRenamePlanItem>(items.Count);
        var sourcePaths = new HashSet<string>(items.Select(i => i.FullPath), StringComparer.OrdinalIgnoreCase);

        // 1. Initial evaluation and basic validation
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            string newName = MultiRenameTokenEvaluator.Evaluate(item, i, options);
            string? dir = Path.GetDirectoryName(item.FullPath);
            string newPath = !string.IsNullOrEmpty(dir) ? Path.Combine(dir, newName) : newName;

            MultiRenameStatus status;
            string? errorMessage = null;

            if (!FileNameValidator.TryValidate(newName, out string? valError))
            {
                status = MultiRenameStatus.InvalidName;
                errorMessage = valError;
            }
            else if (string.Equals(item.Name, newName, StringComparison.Ordinal))
            {
                status = MultiRenameStatus.Unchanged;
            }
            else
            {
                status = MultiRenameStatus.Valid;
            }

            planItems.Add(new MultiRenamePlanItem(
                OriginalPath: item.FullPath,
                OriginalName: item.Name,
                NewName: newName,
                NewPath: newPath,
                Status: status,
                ErrorMessage: errorMessage,
                IsDirectory: item.IsDirectory));
        }

        // 2. Batch collision check (multiple items in the same directory producing identical new names)
        var nameGroups = planItems
            .GroupBy(p => (!string.IsNullOrEmpty(Path.GetDirectoryName(p.OriginalPath)) ? Path.GetDirectoryName(p.OriginalPath)! : "",
                           p.NewName),
                     new TupleComparer())
            .Where(g => g.Count() > 1);

        foreach (var group in nameGroups)
        {
            // If items in this group would end up with the same name and at least one is changing
            bool hasChange = group.Any(x => !string.Equals(x.OriginalName, x.NewName, StringComparison.Ordinal));
            if (hasChange)
            {
                for (int i = 0; i < planItems.Count; i++)
                {
                    var item = planItems[i];
                    string itemDir = Path.GetDirectoryName(item.OriginalPath) ?? "";
                    if (string.Equals(itemDir, group.Key.Item1, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(item.NewName, group.Key.Item2, StringComparison.OrdinalIgnoreCase))
                    {
                        planItems[i] = item with
                        {
                            Status = MultiRenameStatus.BatchCollision,
                            ErrorMessage = $"Conflict: Multiple items in this folder will be named '{item.NewName}'."
                        };
                    }
                }
            }
        }

        // 3. Disk collision check (new path exists on disk and is NOT one of the items being renamed in this batch)
        for (int i = 0; i < planItems.Count; i++)
        {
            var item = planItems[i];
            if (item.Status == MultiRenameStatus.Valid && item.IsChanged)
            {
                if (fs.Exists(item.NewPath))
                {
                    // If target path exists on disk, check if it's part of the items being renamed
                    if (!sourcePaths.Contains(item.NewPath))
                    {
                        planItems[i] = item with
                        {
                            Status = MultiRenameStatus.DiskCollision,
                            ErrorMessage = $"Conflict: An existing item on disk is already named '{item.NewName}'."
                        };
                    }
                }
            }
        }

        return new MultiRenamePlan(planItems);
    }

    private sealed class TupleComparer : IEqualityComparer<(string, string)>
    {
        public bool Equals((string, string) x, (string, string) y)
        {
            return string.Equals(x.Item1, y.Item1, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(x.Item2, y.Item2, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode((string, string) obj)
        {
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item1),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item2));
        }
    }
}

