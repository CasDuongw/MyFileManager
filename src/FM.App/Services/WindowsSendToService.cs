using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using FM.Core.ShellIntegration;

namespace FM.App.Services;

public class WindowsSendToService : ISendToService
{
    private List<SendToTarget>? _cachedTargets;

    public IReadOnlyList<SendToTarget> GetTargets()
    {
        if (_cachedTargets != null) return _cachedTargets;

        var list = new List<SendToTarget>();
        if (!OperatingSystem.IsWindows())
        {
            _cachedTargets = list;
            return _cachedTargets;
        }

        try
        {
            string sendToDir = Environment.GetFolderPath(Environment.SpecialFolder.SendTo);
            if (Directory.Exists(sendToDir))
            {
                foreach (string file in Directory.EnumerateFiles(sendToDir))
                {
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    string fileName = Path.GetFileNameWithoutExtension(file);

                    if (ext == ".desklink")
                    {
                        list.Add(new SendToTarget(
                            DisplayName: "Desktop (create shortcut)",
                            Path: file,
                            IsDesktopShortcut: true));
                    }
                    else if (ext == ".zfsendtarget")
                    {
                        list.Add(new SendToTarget(
                            DisplayName: "Compressed (zipped) folder",
                            Path: file,
                            IsZipCompressor: true));
                    }
                    else if (ext == ".mydocs")
                    {
                        string docsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                        list.Add(new SendToTarget(
                            DisplayName: "Documents",
                            Path: file,
                            ResolvedDirectory: docsPath));
                    }
                    else if (ext == ".lnk")
                    {
                        string? target = ResolveShortcutTarget(file);
                        list.Add(new SendToTarget(
                            DisplayName: fileName,
                            Path: file,
                            ResolvedDirectory: Directory.Exists(target) ? target : null));
                    }
                }
            }

            // Removable drives as SendTo targets (matching Windows Explorer)
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType == DriveType.Removable && drive.IsReady)
                {
                    string label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "USB Drive" : drive.VolumeLabel;
                    list.Add(new SendToTarget(
                        DisplayName: $"{label} ({drive.Name.TrimEnd('\\')})",
                        Path: drive.RootDirectory.FullName,
                        ResolvedDirectory: drive.RootDirectory.FullName));
                }
            }
        }
        catch { }

        _cachedTargets = list;
        return _cachedTargets;
    }

    public async Task<bool> ExecuteSendToAsync(
        SendToTarget target,
        IReadOnlyList<string> sourcePaths,
        Func<IReadOnlyList<string>, string, Task> copyToFolderAction,
        Func<IReadOnlyList<string>, Task> zipAction,
        CancellationToken ct = default)
    {
        if (sourcePaths == null || sourcePaths.Count == 0) return false;

        if (target.IsDesktopShortcut)
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            foreach (var src in sourcePaths)
            {
                string name = Path.GetFileNameWithoutExtension(src);
                string shortcutPath = Path.Combine(desktop, $"{name} - Shortcut.lnk");
                CreateShortcutFile(src, shortcutPath);
            }
            return true;
        }

        if (target.IsZipCompressor)
        {
            await zipAction(sourcePaths);
            return true;
        }

        if (!string.IsNullOrEmpty(target.ResolvedDirectory))
        {
            await copyToFolderAction(sourcePaths, target.ResolvedDirectory);
            return true;
        }

        // Executable or shortcut handler
        try
        {
            string args = string.Join(" ", sourcePaths.Select(p => $"\"{p}\""));
            Process.Start(new ProcessStartInfo(target.Path, args) { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        void IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([Out, MarshalAs(UnmanagedType.LPWStr)] string ppszFileName);
    }

    public static void CreateShortcutFile(string targetPath, string shortcutPath, string? workingDir = null, string? description = null)
    {
        if (!OperatingSystem.IsWindows()) return;
        IShellLinkW? link = null;
        try
        {
            link = (IShellLinkW)new ShellLink();
            link.SetPath(targetPath);
            if (!string.IsNullOrEmpty(workingDir)) link.SetWorkingDirectory(workingDir);
            else if (File.Exists(targetPath)) link.SetWorkingDirectory(Path.GetDirectoryName(targetPath) ?? "");
            if (!string.IsNullOrEmpty(description)) link.SetDescription(description);
            var file = (IPersistFile)link;
            file.Save(shortcutPath, false);
        }
        catch { }
        finally
        {
            if (link != null)
            {
                try { Marshal.FinalReleaseComObject(link); } catch { }
            }
        }
    }

    internal static string? ResolveShortcutTarget(string shortcutPath)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(shortcutPath)) return null;
        IShellLinkW? link = null;
        try
        {
            link = (IShellLinkW)new ShellLink();
            var file = (IPersistFile)link;
            file.Load(shortcutPath, 0);
            var sb = new StringBuilder(1024);
            link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
            string result = sb.ToString();
            return string.IsNullOrWhiteSpace(result) ? null : result;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (link != null)
            {
                try { Marshal.FinalReleaseComObject(link); } catch { }
            }
        }
    }
}

