using System.IO;

namespace FM.App.Helpers;

/// <summary>
/// Helper methods for formatting file sizes and timestamps cleanly for the UI.
/// </summary>
public static class FormatHelper
{
    private static readonly string[] SizeSuffixes = { "B", "KB", "MB", "GB", "TB" };

    public static string FormatFileSize(long bytes, bool isDirectory)
    {
        // Directory sizes are not displayed in the column, matching Windows Explorer
        if (isDirectory) return string.Empty;
        if (bytes <= 0) return "0 B";

        int order = 0;
        double len = bytes;
        while (len >= 1024 && order < SizeSuffixes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return order == 0 ? $"{len:0} {SizeSuffixes[order]}" : $"{len:0.#} {SizeSuffixes[order]}";
    }

    public static string FormatFileType(string name, bool isDirectory)
    {
        if (isDirectory) return "File folder";
        string ext = Path.GetExtension(name);
        if (string.IsNullOrEmpty(ext)) return "File";
        return ext.TrimStart('.').ToUpperInvariant() + " File";
    }
}
