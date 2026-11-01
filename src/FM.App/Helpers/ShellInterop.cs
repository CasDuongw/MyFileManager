using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;

namespace FM.App.Helpers;

/// <summary>
/// Native Windows Shell interop utilities for modern context menu operations
/// (Recycle Bin, File Properties dialog, Terminal, VS Code, ZIP compression).
/// </summary>
public static class ShellInterop
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszProgressTitle;
    }

    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_SILENT = 0x0004;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpVerb;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpFile;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpParameters;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }

    private const uint SEE_MASK_INVOKEIDLIST = 0x0000000C;
    private const int SW_SHOW = 5;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);

    /// <summary>
    /// Deletes one or more files/directories by moving them safely to the Windows Recycle Bin.
    /// </summary>
    public static bool MoveToRecycleBin(IEnumerable<string> paths)
    {
        var list = paths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        if (list.Count == 0) return false;

        // SHFileOperation expects double null-terminated string: "file1\0file2\0\0"
        string pFrom = string.Join("\0", list) + "\0\0";

        var fileOp = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = pFrom,
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT
        };

        int ret = SHFileOperation(ref fileOp);
        return ret == 0 && !fileOp.fAnyOperationsAborted;
    }

    /// <summary>
    /// Displays the native Windows shell file/folder properties dialog.
    /// </summary>
    public static bool ShowProperties(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        var info = new SHELLEXECUTEINFO
        {
            cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
            lpVerb = "properties",
            lpFile = path,
            nShow = SW_SHOW,
            fMask = SEE_MASK_INVOKEIDLIST
        };

        return ShellExecuteEx(ref info);
    }

    /// <summary>
    /// Opens the specified directory in Windows Terminal, PowerShell, or cmd.
    /// </summary>
    public static void OpenInTerminal(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return;

        string dir = Directory.Exists(folderPath) ? folderPath : (Path.GetDirectoryName(folderPath) ?? folderPath);
        if (!Directory.Exists(dir)) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "wt.exe",
                Arguments = $"-d \"{dir}\"",
                UseShellExecute = true
            });
        }
        catch
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoExit -Command \"Set-Location '{dir}'\"",
                    UseShellExecute = true
                });
            }
            catch
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    WorkingDirectory = dir,
                    UseShellExecute = true
                });
            }
        }
    }

    /// <summary>
    /// Opens the specified file or folder in Visual Studio Code.
    /// </summary>
    public static void OpenWithCode(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c code \"{path}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }
        catch
        {
            // Ignore if VS Code CLI is not available in PATH
        }
    }

    /// <summary>
    /// Compresses selected files or folders into a standard ZIP archive.
    /// </summary>
    public static string CreateZipArchive(IEnumerable<string> paths, string destinationZip)
    {
        var sourceList = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (sourceList.Count == 0) throw new InvalidOperationException("No valid files or directories to compress.");

        if (File.Exists(destinationZip))
        {
            File.Delete(destinationZip);
        }

        using var zip = ZipFile.Open(destinationZip, ZipArchiveMode.Create);
        foreach (var path in sourceList)
        {
            if (File.Exists(path))
            {
                zip.CreateEntryFromFile(path, Path.GetFileName(path), CompressionLevel.Optimal);
            }
            else if (Directory.Exists(path))
            {
                string dirName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                AddDirectoryToZip(zip, path, dirName);
            }
        }

        return destinationZip;
    }

    private static void AddDirectoryToZip(ZipArchive zip, string sourceDir, string rootName)
    {
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceDir, file);
            string entryName = Path.Combine(rootName, relative).Replace('\\', '/');
            zip.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
        }
    }

    /// <summary>
    /// Displays the Windows "Open With" dialog for the specified file.
    /// </summary>
    public static void ShowOpenWithDialog(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

        try
        {
            var info = new SHELLEXECUTEINFO
            {
                cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
                lpVerb = "openas",
                lpFile = path,
                nShow = SW_SHOW,
                fMask = SEE_MASK_INVOKEIDLIST
            };
            ShellExecuteEx(ref info);
        }
        catch
        {
            try
            {
                Process.Start(new ProcessStartInfo("rundll32.exe", $"shell32.dll,OpenAs_RunDLL \"{path}\"") { UseShellExecute = true });
            }
            catch { }
        }
    }

    /// <summary>
    /// Runs the specified executable or file as Administrator (UAC prompt).
    /// </summary>
    public static void RunAsAdministrator(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
                Verb = "runas"
            });
        }
        catch { }
    }

    /// <summary>
    /// Pins the specified file or folder to the Windows Start Menu.
    /// </summary>
    public static void PinToStart(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            var info = new SHELLEXECUTEINFO
            {
                cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
                lpVerb = "pintostartscreen",
                lpFile = path,
                nShow = SW_SHOW,
                fMask = SEE_MASK_INVOKEIDLIST
            };
            ShellExecuteEx(ref info);
        }
        catch { }
    }

    private static string? _winRarPath;
    private static bool _winRarChecked;

    public static string? GetWinRarPath()
    {
        if (_winRarChecked) return _winRarPath;
        _winRarChecked = true;

        string p64 = @"C:\Program Files\WinRAR\WinRAR.exe";
        string p32 = @"C:\Program Files (x86)\WinRAR\WinRAR.exe";
        if (File.Exists(p64)) _winRarPath = p64;
        else if (File.Exists(p32)) _winRarPath = p32;
        return _winRarPath;
    }

    public static bool IsWinRarInstalled => !string.IsNullOrEmpty(GetWinRarPath());

    public static void WinRarAddGui(IEnumerable<string> paths)
    {
        string? winrar = GetWinRarPath();
        if (winrar == null) return;
        var args = string.Join(" ", paths.Select(p => $"\"{p}\""));
        try
        {
            Process.Start(new ProcessStartInfo(winrar, $"a {args}") { UseShellExecute = true });
        }
        catch { }
    }

    public static void WinRarAddToRar(IEnumerable<string> paths, string destinationRar)
    {
        string? winrar = GetWinRarPath();
        if (winrar == null) return;
        var args = string.Join(" ", paths.Select(p => $"\"{p}\""));
        try
        {
            Process.Start(new ProcessStartInfo(winrar, $"a -r \"{destinationRar}\" {args}") { UseShellExecute = true });
        }
        catch { }
    }

    public static void WinRarExtractHere(string archivePath)
    {
        string? winrar = GetWinRarPath();
        if (winrar == null) return;
        string dir = Path.GetDirectoryName(archivePath) ?? ".";
        try
        {
            Process.Start(new ProcessStartInfo(winrar, $"x -ibck \"{archivePath}\" *.* \"{dir}\\\"") { UseShellExecute = true });
        }
        catch { }
    }

    public static void WinRarExtractToFolder(string archivePath)
    {
        string? winrar = GetWinRarPath();
        if (winrar == null) return;
        string nameWithoutExt = Path.GetFileNameWithoutExtension(archivePath);
        string parent = Path.GetDirectoryName(archivePath) ?? ".";
        string target = Path.Combine(parent, nameWithoutExt);
        try
        {
            Process.Start(new ProcessStartInfo(winrar, $"x -ibck \"{archivePath}\" *.* \"{target}\\\"") { UseShellExecute = true });
        }
        catch { }
    }
}
