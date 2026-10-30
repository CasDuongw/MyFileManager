using System.IO;
using System.Windows;
using FM.Core.FileSystem;
using FM.Core.Operations.Transfer;

namespace FM.App.Helpers;

public record DropResolution(
    bool IsValid,
    DragDropEffects Effect,
    TransferKind? Kind,
    string? Reason = null);

public static class DropEffectResolver
{
    public static DropResolution Resolve(
        IFileSystem fs,
        IReadOnlyList<string>? sourcePaths,
        string destinationDirectory,
        DragDropKeyStates keyStates)
    {
        ArgumentNullException.ThrowIfNull(fs);

        if (sourcePaths == null || sourcePaths.Count == 0)
        {
            return new DropResolution(false, DragDropEffects.None, null, "No sources specified.");
        }

        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            return new DropResolution(false, DragDropEffects.None, null, "Destination directory cannot be empty.");
        }

        string normDest = NormalizePath(destinationDirectory);

        // Destination must exist and be a directory
        var destInfo = fs.GetInfo(normDest);
        if (destInfo == null || !destInfo.Exists || !destInfo.IsDirectory)
        {
            return new DropResolution(false, DragDropEffects.None, null, "Destination directory does not exist.");
        }

        // Sources must exist
        foreach (var src in sourcePaths)
        {
            if (string.IsNullOrWhiteSpace(src))
            {
                return new DropResolution(false, DragDropEffects.None, null, "Invalid source path.");
            }

            var srcInfo = fs.GetInfo(src);
            if (srcInfo == null || !srcInfo.Exists)
            {
                return new DropResolution(false, DragDropEffects.None, null, $"Source path '{src}' does not exist.");
            }

            string normSrc = NormalizePath(src);

            // Cannot drop a directory into itself
            if (string.Equals(normDest, normSrc, StringComparison.OrdinalIgnoreCase))
            {
                return new DropResolution(false, DragDropEffects.None, null, "Cannot drop a directory into itself.");
            }

            // Cannot drop a directory into its own descendant
            if (srcInfo.IsDirectory)
            {
                string srcPrefix = normSrc + Path.DirectorySeparatorChar;
                if (normDest.StartsWith(srcPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return new DropResolution(false, DragDropEffects.None, null, "Cannot drop a directory into its own descendant.");
                }
            }
        }

        // Rule: destination is parent of all sources (cannot drop into same containing directory)
        bool allSameParent = sourcePaths.All(src =>
        {
            string? parent = Path.GetDirectoryName(NormalizePath(src));
            return parent != null && string.Equals(NormalizePath(parent), normDest, StringComparison.OrdinalIgnoreCase);
        });

        if (allSameParent)
        {
            return new DropResolution(false, DragDropEffects.None, null, "Dropping into the current containing folder is not allowed.");
        }

        // Resolve Effect and Kind based on modifier keys & volume
        bool ctrl = (keyStates & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey;
        bool shift = (keyStates & DragDropKeyStates.ShiftKey) == DragDropKeyStates.ShiftKey;
        bool alt = (keyStates & DragDropKeyStates.AltKey) == DragDropKeyStates.AltKey;

        // Alt or Ctrl+Shift -> link (unsupported in v0.4)
        if (alt || (ctrl && shift))
        {
            return new DropResolution(false, DragDropEffects.None, null, "Linking is not supported.");
        }

        if (ctrl)
        {
            return new DropResolution(true, DragDropEffects.Copy, TransferKind.Copy);
        }

        if (shift)
        {
            return new DropResolution(true, DragDropEffects.Move, TransferKind.Move);
        }

        // Default: same volume -> Move; cross volume -> Copy
        string destVolume = fs.GetVolumeId(normDest);
        bool allSameVolume = sourcePaths.All(src =>
            string.Equals(fs.GetVolumeId(src), destVolume, StringComparison.OrdinalIgnoreCase));

        if (allSameVolume)
        {
            return new DropResolution(true, DragDropEffects.Move, TransferKind.Move);
        }

        return new DropResolution(true, DragDropEffects.Copy, TransferKind.Copy);
    }

    public static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        string trimmed = path.Trim();
        // Keep root slash for paths like C:\ or \\server\share
        if (trimmed.EndsWith(":") || trimmed.EndsWith(@":\"))
            return trimmed;

        return trimmed.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
