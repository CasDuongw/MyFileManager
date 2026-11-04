using System.IO;

namespace FM.Core.Elevation;

public static class ElevatedPathValidator
{
    private static readonly HashSet<string> SensitiveRootDirectories = new(StringComparer.OrdinalIgnoreCase);

    static ElevatedPathValidator()
    {
        string? windir = Environment.GetEnvironmentVariable("WINDIR") ?? Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(windir))
        {
            SensitiveRootDirectories.Add(Path.GetFullPath(windir).TrimEnd('\\'));
        }

        string? sysRoot = Environment.GetEnvironmentVariable("SystemRoot");
        if (!string.IsNullOrWhiteSpace(sysRoot))
        {
            SensitiveRootDirectories.Add(Path.GetFullPath(sysRoot).TrimEnd('\\'));
        }

        string? progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(progFiles))
        {
            SensitiveRootDirectories.Add(Path.GetFullPath(progFiles).TrimEnd('\\'));
        }

        string? progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(progFilesX86))
        {
            SensitiveRootDirectories.Add(Path.GetFullPath(progFilesX86).TrimEnd('\\'));
        }

        string? userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            string? usersDir = Path.GetDirectoryName(userProfile);
            if (!string.IsNullOrWhiteSpace(usersDir))
            {
                SensitiveRootDirectories.Add(Path.GetFullPath(usersDir).TrimEnd('\\'));
            }
        }
    }

    public static bool IsPathSafeForElevation(string path, out string? reason)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            reason = "Đường dẫn không được để trống.";
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex)
        {
            reason = $"Đường dẫn không hợp lệ: {ex.Message}";
            return false;
        }

        string? root = Path.GetPathRoot(fullPath);
        if (string.Equals(fullPath.TrimEnd('\\'), root?.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            reason = "Thao tác trên thư mục gốc của ổ đĩa bị nghiêm cấm vì lý do an toàn.";
            return false;
        }

        string normalized = fullPath.TrimEnd('\\');

        // Check if path is directly one of the sensitive system directories
        foreach (var sensitive in SensitiveRootDirectories)
        {
            if (string.Equals(normalized, sensitive, StringComparison.OrdinalIgnoreCase))
            {
                reason = $"Thao tác trực tiếp trên thư mục hệ thống '{sensitive}' bị nghiêm cấm.";
                return false;
            }
        }

        // Specifically block any operation inside Windows directory
        string? windir = Environment.GetEnvironmentVariable("WINDIR") ?? Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(windir))
        {
            string winNormalized = Path.GetFullPath(windir).TrimEnd('\\');
            if (normalized.StartsWith(winNormalized + "\\", StringComparison.OrdinalIgnoreCase))
            {
                reason = $"Thao tác bên trong thư mục Windows '{winNormalized}' bị nghiêm cấm đối với worker đặc quyền.";
                return false;
            }
        }

        reason = null;
        return true;
    }

    public static bool IsReparsePoint(string path)
    {
        try
        {
            if (File.Exists(path) || Directory.Exists(path))
            {
                var attr = File.GetAttributes(path);
                return (attr & FileAttributes.ReparsePoint) != 0;
            }
        }
        catch { }
        return false;
    }
}
