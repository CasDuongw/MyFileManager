using System.IO;

namespace FM.Core.PathResolution;

/// <summary>
/// Production path resolver handling environment variables, tilde expansion, shell: aliases,
/// UNC and WSL paths, long paths, and special virtual views ("This PC", "Recycle Bin").
/// </summary>
public sealed class PathResolver : IPathResolver
{
    public static readonly PathResolver Instance = new();

    private static readonly Dictionary<string, Func<string>> KnownShellFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Desktop"] = () => Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        ["Downloads"] = GetDownloadsPath,
        ["Documents"] = () => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        ["Personal"] = () => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        ["My Documents"] = () => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        ["Pictures"] = () => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        ["My Pictures"] = () => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        ["Music"] = () => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        ["My Music"] = () => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        ["Videos"] = () => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        ["My Video"] = () => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        ["AppData"] = () => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        ["Roaming AppData"] = () => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        ["Local AppData"] = () => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ["Startup"] = () => Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        ["SendTo"] = () => Environment.GetFolderPath(Environment.SpecialFolder.SendTo),
        ["Recent"] = () => Environment.GetFolderPath(Environment.SpecialFolder.Recent),
        ["Favorites"] = () => Environment.GetFolderPath(Environment.SpecialFolder.Favorites),
        ["Fonts"] = () => Environment.GetFolderPath(Environment.SpecialFolder.Fonts),
        ["Windows"] = () => Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        ["System"] = () => Environment.GetFolderPath(Environment.SpecialFolder.System),
        ["ProgramFiles"] = () => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        ["ProgramFilesX86"] = () => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        ["Common AppData"] = () => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        ["UserProfile"] = () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ["Quick access"] = () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ["QuickAccess"] = () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
    };

    public PathResolutionResult Resolve(string inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            return PathResolutionResult.Failure(ResolvedPathType.Invalid, inputPath ?? string.Empty, "Đường dẫn không được để trống.");
        }

        string trimmed = inputPath.Trim();

        // 1. Virtual Views: This PC
        if (IsThisPcAlias(trimmed))
        {
            return PathResolutionResult.Success(ResolvedPathType.ThisPC, "thispc://", "This PC");
        }

        // 2. Virtual Views: Recycle Bin
        if (IsRecycleBinAlias(trimmed))
        {
            return PathResolutionResult.Success(ResolvedPathType.RecycleBin, "recyclebin://", "Recycle Bin");
        }

        // 3. Environment Variable Expansion (%USERPROFILE%, %APPDATA%, %TEMP%, etc.)
        if (trimmed.Contains('%'))
        {
            try
            {
                string expanded = Environment.ExpandEnvironmentVariables(trimmed);
                if (!string.Equals(expanded, trimmed, StringComparison.Ordinal))
                {
                    trimmed = expanded.Trim();
                }
            }
            catch
            {
                // If expansion fails, continue with original
            }
        }

        // 4. Tilde (~) Expansion for user home directory
        if (trimmed == "~" || trimmed.StartsWith("~/") || trimmed.StartsWith(@"~\"))
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (trimmed == "~")
            {
                trimmed = userProfile;
            }
            else
            {
                string subPath = trimmed[2..].Replace('/', '\\');
                trimmed = Path.Combine(userProfile, subPath);
            }
        }

        // 5. Shell: Names (e.g. shell:Downloads, shell:Desktop, shell:MyComputerFolder)
        if (trimmed.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            string shellName = trimmed[6..].Trim();

            if (IsThisPcAlias(shellName))
            {
                return PathResolutionResult.Success(ResolvedPathType.ThisPC, "thispc://", "This PC");
            }

            if (IsRecycleBinAlias(shellName))
            {
                return PathResolutionResult.Success(ResolvedPathType.RecycleBin, "recyclebin://", "Recycle Bin");
            }

            if (KnownShellFolders.TryGetValue(shellName, out var resolver))
            {
                try
                {
                    string target = resolver();
                    if (!string.IsNullOrEmpty(target) && (Directory.Exists(target) || File.Exists(target)))
                    {
                        return PathResolutionResult.Success(
                            ResolvedPathType.FileSystemDirectory,
                            target,
                            Path.GetFileName(target.TrimEnd('\\')) is { Length: > 0 } fn ? fn : target);
                    }
                    else if (!string.IsNullOrEmpty(target))
                    {
                        // Path resolved logically but may not yet exist on disk
                        return PathResolutionResult.Success(ResolvedPathType.FileSystemDirectory, target, shellName);
                    }
                }
                catch (Exception ex)
                {
                    return PathResolutionResult.Failure(ResolvedPathType.Invalid, trimmed, $"Không thể mở thư mục shell '{shellName}': {ex.Message}");
                }
            }

            return PathResolutionResult.Failure(
                ResolvedPathType.UnsupportedVirtualFolder,
                trimmed,
                $"Thư mục ảo 'shell:{shellName}' không được hỗ trợ hoặc không tồn tại trên hệ thống này.");
        }

        // 6. Long paths (\\?\...)
        if (trimmed.StartsWith(@"\\?\"))
        {
            return PathResolutionResult.Success(
                ResolvedPathType.FileSystemDirectory,
                trimmed,
                Path.GetFileName(trimmed.TrimEnd('\\')) is { Length: > 0 } fn ? fn : trimmed);
        }

        // 7. UNC Paths (\\server\share, \\wsl$\Ubuntu, \\wsl.localhost\Debian)
        if (trimmed.StartsWith(@"\\") || trimmed.StartsWith("//"))
        {
            string normalizedUnc = trimmed.Replace('/', '\\');
            string displayName = GetUncDisplayName(normalizedUnc);
            return PathResolutionResult.Success(ResolvedPathType.FileSystemDirectory, normalizedUnc, displayName);
        }

        // 8. Standard File System Directory / File Path
        try
        {
            string fullPath = Path.GetFullPath(trimmed);
            string displayName = Path.GetFileName(fullPath.TrimEnd('\\'));
            if (string.IsNullOrEmpty(displayName))
            {
                // Drive root like C:\
                displayName = fullPath;
            }

            return PathResolutionResult.Success(ResolvedPathType.FileSystemDirectory, fullPath, displayName);
        }
        catch (Exception ex)
        {
            return PathResolutionResult.Failure(ResolvedPathType.Invalid, trimmed, $"Đường dẫn không hợp lệ: {ex.Message}");
        }
    }

    private static bool IsThisPcAlias(string path)
    {
        return string.Equals(path, "This PC", StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, "thispc://", StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, "::ThisPC", StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, "shell:ThisPC", StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, "shell:MyComputerFolder", StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, "shell:DriveFolder", StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, "My Computer", StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, "Computer", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRecycleBinAlias(string path)
    {
        return string.Equals(path, "Recycle Bin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, "recyclebin://", StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, "::RecycleBin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, "shell:RecycleBinFolder", StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, "shell:RecycleBin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, "Thùng rác", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetDownloadsPath()
    {
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string defaultDownloads = Path.Combine(userProfile, "Downloads");
        return defaultDownloads;
    }

    private static string GetUncDisplayName(string uncPath)
    {
        // e.g. \\server\share -> share (server)
        // e.g. \\wsl$\Ubuntu -> Ubuntu (WSL)
        // e.g. \\wsl.localhost\Ubuntu -> Ubuntu (WSL)
        var parts = uncPath.Trim('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return uncPath;
        if (parts.Length == 1) return parts[0];

        string host = parts[0];
        string share = parts[1];

        if (host.Equals("wsl$", StringComparison.OrdinalIgnoreCase) || host.Equals("wsl.localhost", StringComparison.OrdinalIgnoreCase))
        {
            return $"{share} (WSL)";
        }

        return $"{share} ({host})";
    }
}

