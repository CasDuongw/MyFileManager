using System.IO;

namespace FM.Core.Operations;

/// <summary>
/// Validates Windows file and folder names according to Win32 file naming specifications.
/// </summary>
public static class FileNameValidator
{
    private static readonly char[] InvalidChars = { '\\', '/', ':', '*', '?', '"', '<', '>', '|' };

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static bool TryValidate(string? name, out string? errorMessage)
    {
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(name))
        {
            errorMessage = "Name cannot be empty or whitespace.";
            return false;
        }

        if (name.Length > 255)
        {
            errorMessage = "Name cannot exceed 255 characters.";
            return false;
        }

        if (name.EndsWith('.') || name.EndsWith(' '))
        {
            errorMessage = "Name cannot end with a period or space.";
            return false;
        }

        if (name.IndexOfAny(InvalidChars) >= 0)
        {
            errorMessage = "Name cannot contain any of the following characters: \\ / : * ? \" < > |";
            return false;
        }

        // Check reserved device names with or without extension (e.g., "CON", "aux.txt", "nul.tar.gz")
        string baseName = name;
        int firstDotIndex = name.IndexOf('.');
        if (firstDotIndex > 0)
        {
            baseName = name.Substring(0, firstDotIndex);
        }

        if (ReservedNames.Contains(baseName))
        {
            errorMessage = $"'{name}' is a reserved Windows device name.";
            return false;
        }

        return true;
    }
}
