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
}

