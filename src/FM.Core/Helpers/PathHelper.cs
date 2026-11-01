using System.IO;

namespace FM.Core.Helpers;

/// <summary>
/// Cross-platform and Win32 path normalization utilities, specifically handling
/// long paths (>= 240 characters) via \\?\ and \\?\UNC\ prefixes.
/// </summary>
public static class PathHelper
{
    /// <summary>
    /// Returns the full normalized path, prefixing with \\?\ on Windows if the path length
    /// is 240 characters or greater to bypass MAX_PATH restrictions.
    /// </summary>
    public static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        string fullPath = Path.GetFullPath(path);

        if (OperatingSystem.IsWindows() && fullPath.Length >= 240 && !fullPath.StartsWith(@"\\?\") && !fullPath.StartsWith(@"\\.\"))
        {
            if (fullPath.StartsWith(@"\\"))
            {
                return @"\\?\UNC\" + fullPath.Substring(2);
            }
            return @"\\?\" + fullPath;
        }

        return fullPath;
    }
}

