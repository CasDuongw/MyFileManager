using System.IO;
using System.Security;

namespace FM.Core.Operations.Zip;

/// <summary>
/// Sanitizes archive entry names and paths to defend against Zip-Slip vulnerabilities,
/// directory traversal, Windows reserved device names, and invalid filename characters.
/// </summary>
public static class ZipPathSanitizer
{
    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    private static readonly HashSet<char> InvalidChars = new(
        Path.GetInvalidFileNameChars()
            .Concat(new[] { '*', '?', ':', '"', '<', '>', '|', '/', '\\' }));

    /// <summary>
    /// Sanitizes an individual file or directory name segment (not a full path).
    /// Strips invalid Win32 chars, trims trailing dots and spaces, and prefixes
    /// reserved Win32 device names with an underscore.
    /// </summary>
    public static string SanitizeSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment)) return "_";

        var sb = new System.Text.StringBuilder(segment.Length);
        foreach (char c in segment)
        {
            if (InvalidChars.Contains(c))
            {
                sb.Append('_');
            }
            else
            {
                sb.Append(c);
            }
        }

        string clean = sb.ToString().Trim(' ', '.');
        if (string.IsNullOrWhiteSpace(clean))
        {
            clean = "_";
        }

        // Check reserved device names (e.g., CON, AUX.txt, com1.tar.gz)
        string baseName = Path.GetFileNameWithoutExtension(clean);
        if (ReservedDeviceNames.Contains(baseName))
        {
            clean = "_" + clean;
        }

        return clean;
    }

    /// <summary>
    /// Sanitizes an archive entry's relative path, normalizing separators,
    /// skipping traversal tokens ('.', '..'), and sanitizing each segment.
    /// </summary>
    public static string SanitizeRelativePath(string entryFullName)
    {
        if (string.IsNullOrWhiteSpace(entryFullName)) return string.Empty;

        // Split by '/' and '\'
        var segments = entryFullName.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        var cleanSegments = new List<string>(segments.Length);

        foreach (var seg in segments)
        {
            if (seg == "." || seg == "..")
            {
                // Discard traversal tokens
                continue;
            }

            cleanSegments.Add(SanitizeSegment(seg));
        }

        return cleanSegments.Count > 0
            ? string.Join(Path.DirectorySeparatorChar.ToString(), cleanSegments)
            : string.Empty;
    }

    /// <summary>
    /// Resolves and validates the destination path for an entry.
    /// Ensures that the resulting path is strictly contained within the canonical destination directory.
    /// Throws <see cref="SecurityException"/> if Zip-Slip traversal is detected.
    /// </summary>
    public static string GetSafeDestinationPath(string destinationDirectory, string entryFullName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);

        string canonicalDest = Path.GetFullPath(destinationDirectory);
        if (!canonicalDest.EndsWith(Path.DirectorySeparatorChar))
        {
            canonicalDest += Path.DirectorySeparatorChar;
        }

        // 1. Raw traversal check (Zip-Slip defense)
        string normalizedEntry = entryFullName.Replace('/', Path.DirectorySeparatorChar);
        string rawCandidate = Path.GetFullPath(Path.Combine(canonicalDest, normalizedEntry));
        if (!rawCandidate.StartsWith(canonicalDest, StringComparison.OrdinalIgnoreCase))
        {
            throw new SecurityException(
                $"Zip entry '{entryFullName}' attempts directory traversal outside destination '{destinationDirectory}'.");
        }

        // 2. Segment sanitization (Windows reserved names, invalid characters, trailing dots/spaces)
        string sanitizedRel = SanitizeRelativePath(entryFullName);
        string candidate = Path.GetFullPath(Path.Combine(canonicalDest, sanitizedRel));

        if (!candidate.StartsWith(canonicalDest, StringComparison.OrdinalIgnoreCase))
        {
            throw new SecurityException(
                $"Zip entry '{entryFullName}' attempts directory traversal outside destination '{destinationDirectory}'.");
        }

        return candidate;
    }
}
