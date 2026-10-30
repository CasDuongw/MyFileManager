using System.IO;
using FM.Core.FileSystem;

namespace FM.Core.Operations.Transfer;

public record PlannedItem(
    string SourcePath,
    string RelativeDestinationPath,
    bool IsDirectory,
    long Size,
    DateTime Modified,
    bool IsReparsePoint);

public record TransferPlan(
    TransferRequest Request,
    IReadOnlyList<PlannedItem> Items,
    long OverallTotalBytes,
    int FilesTotal,
    bool IsCrossVolume,
    string SourceVolumeId,
    string DestinationVolumeId);

/// <summary>
/// Validates transfer requests, scans source hierarchies, detects reparse loops,
/// calculates totals, and checks destination volume free space prior to byte movement.
/// </summary>
public static class TransferPlanner
{
    public static TransferPlan CreatePlan(
        IFileSystem fs,
        TransferRequest request,
        IProgress<TransferProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentNullException.ThrowIfNull(request);

        if (request.Sources == null || request.Sources.Count == 0)
        {
            throw new ArgumentException("Transfer request must contain at least one source.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.DestinationDirectory))
        {
            throw new ArgumentException("Destination directory cannot be empty.", nameof(request));
        }

        // 1. Validating Phase
        progress?.Report(new TransferProgress(TransferPhase.Validating, null, 0, 0, 0, null, 0, null, 0, null));

        string normDest = Normalize(request.DestinationDirectory);

        // Check each top-level source
        foreach (var source in request.Sources)
        {
            ct.ThrowIfCancellationRequested();
            string normSource = Normalize(source);

            var info = fs.GetInfo(normSource);
            if (info == null || !info.Exists)
            {
                throw new FileNotFoundException($"Source item not found: '{source}'", source);
            }

            if (info.IsDirectory)
            {
                // Rule: Reject destination equal to or inside source directory
                if (normDest.Equals(normSource, StringComparison.OrdinalIgnoreCase))
                {
                    if (request.Kind == TransferKind.Move)
                    {
                        throw new InvalidOperationException($"Cannot move directory '{source}' into itself.");
                    }
                }
                else if (normDest.StartsWith(normSource.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Cannot {request.Kind.ToString().ToLowerInvariant()} directory '{source}' into a subdirectory of itself ('{request.DestinationDirectory}').");
                }
            }
        }

        // 2. Scanning Phase (Totals are null while scanning)
        progress?.Report(new TransferProgress(TransferPhase.Scanning, null, 0, 0, 0, null, 0, null, 0, null));

        var plannedItems = new List<PlannedItem>();
        var visitedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in request.Sources)
        {
            ct.ThrowIfCancellationRequested();
            string normSource = Normalize(source);
            var info = fs.GetInfo(normSource)!;

            string baseName = Path.GetFileName(normSource);
            if (string.IsNullOrEmpty(baseName))
            {
                baseName = "Root";
            }

            // Same-directory copy resolves to KeepBoth-style name
            bool isSameDirectory = string.Equals(Path.GetDirectoryName(normSource), normDest, StringComparison.OrdinalIgnoreCase);
            if (request.Kind == TransferKind.Copy && isSameDirectory)
            {
                baseName = GetKeepBothName(fs, normDest, baseName, info.IsDirectory);
            }

            if (!info.IsDirectory)
            {
                plannedItems.Add(new PlannedItem(normSource, baseName, false, info.Size, info.Modified, info.IsReparsePoint));
            }
            else
            {
                plannedItems.Add(new PlannedItem(normSource, baseName, true, 0, info.Modified, info.IsReparsePoint));

                if (info.IsReparsePoint)
                {
                    // Do not follow reparse points by default
                    continue;
                }

                visitedDirs.Add(normSource);
                ScanDirectory(fs, normSource, baseName, plannedItems, visitedDirs, progress, ct);
            }
        }

        // 3. Totals Calculation
        long overallTotalBytes = plannedItems.Where(i => !i.IsDirectory).Sum(i => i.Size);
        int filesTotal = plannedItems.Count(i => !i.IsDirectory);

        // 4. Volume & Free Space Verification
        string destVolumeId = fs.GetVolumeId(normDest);
        string sourceVolumeId = fs.GetVolumeId(request.Sources[0]);
        bool isCrossVolume = !string.Equals(sourceVolumeId, destVolumeId, StringComparison.OrdinalIgnoreCase);

        bool consumesSpace = request.Kind == TransferKind.Copy || (request.Kind == TransferKind.Move && isCrossVolume);
        if (consumesSpace)
        {
            long availableFreeSpace = fs.GetFreeSpace(normDest);
            if (availableFreeSpace < overallTotalBytes)
            {
                throw new IOException(
                    $"Insufficient disk space on destination volume '{destVolumeId}'. Required: {overallTotalBytes} bytes, Available: {availableFreeSpace} bytes.");
            }
        }

        return new TransferPlan(
            request,
            plannedItems,
            overallTotalBytes,
            filesTotal,
            isCrossVolume,
            sourceVolumeId,
            destVolumeId);
    }

    private static void ScanDirectory(
        IFileSystem fs,
        string dirPath,
        string relativePrefix,
        List<PlannedItem> plannedItems,
        HashSet<string> visitedDirs,
        IProgress<TransferProgress>? progress,
        CancellationToken ct)
    {
        progress?.Report(new TransferProgress(TransferPhase.Scanning, dirPath, 0, 0, 0, null, 0, null, 0, null));

        foreach (var entry in fs.EnumerateEntries(dirPath))
        {
            ct.ThrowIfCancellationRequested();
            string normEntry = Normalize(entry);
            var info = fs.GetInfo(normEntry);
            if (info == null || !info.Exists) continue;

            string name = Path.GetFileName(normEntry);
            string childRel = Path.Combine(relativePrefix, name);

            if (info.IsDirectory)
            {
                if (info.IsReparsePoint || visitedDirs.Contains(normEntry))
                {
                    // Reparse point or symlink cycle detected: skip expansion
                    continue;
                }

                visitedDirs.Add(normEntry);
                plannedItems.Add(new PlannedItem(normEntry, childRel, true, 0, info.Modified, false));
                ScanDirectory(fs, normEntry, childRel, plannedItems, visitedDirs, progress, ct);
            }
            else
            {
                plannedItems.Add(new PlannedItem(normEntry, childRel, false, info.Size, info.Modified, info.IsReparsePoint));
            }
        }
    }

    public static string GetKeepBothName(IFileSystem fs, string directory, string originalName, bool isDirectory)
    {
        string targetPath = Path.Combine(directory, originalName);
        if (!fs.Exists(targetPath)) return originalName;

        string nameWithoutExt = isDirectory ? originalName : Path.GetFileNameWithoutExtension(originalName);
        string ext = isDirectory ? "" : Path.GetExtension(originalName);

        for (int i = 2; ; i++)
        {
            string candidateName = $"{nameWithoutExt} ({i}){ext}";
            string candidatePath = Path.Combine(directory, candidateName);
            if (!fs.Exists(candidatePath))
            {
                return candidateName;
            }
        }
    }

    private static string Normalize(string path)
    {
        return InMemoryFileSystem.Normalize(path);
    }
}
