using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FM.App.Helpers;

/// <summary>
/// Provides complete, native Windows Shell Context Menu integration (IContextMenu, IContextMenu2, IContextMenu3).
/// Displays the authentic Windows Explorer context menu with all installed shell extensions
/// (WinRAR, 7-Zip, Git, VS Code, Defender, Send To, Open With, etc.).
/// </summary>
public static class ShellContextMenu
{
    #region COM Interfaces & Guids

    private static readonly Guid IID_IShellFolder = new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid IID_IContextMenu = new("000214e4-0000-0000-c000-000000000046");
    private static readonly Guid IID_IContextMenu2 = new("000214f4-0000-0000-c000-000000000046");
    private static readonly Guid IID_IContextMenu3 = new("bcfce0a0-ec17-11d0-8d10-00a0c90f2719");

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214E6-0000-0000-C000-000000000046")]
    private interface IShellFolder
    {
        [PreserveSig]
        int ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName, ref uint pchEaten, out IntPtr ppidl, ref uint pdwAttributes);
        [PreserveSig]
        int EnumObjects(IntPtr hwnd, uint grfFlags, out IntPtr ppenumIDList);
        [PreserveSig]
        int BindToObject(IntPtr pidl, IntPtr pbc, [In] ref Guid riid, out IntPtr ppv);
        [PreserveSig]
        int BindToStorage(IntPtr pidl, IntPtr pbc, [In] ref Guid riid, out IntPtr ppv);
        [PreserveSig]
        int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
        [PreserveSig]
        int CreateViewObject(IntPtr hwndOwner, [In] ref Guid riid, out IntPtr ppv);
        [PreserveSig]
        int GetAttributesOf(uint cidl, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IntPtr[] apidl, ref uint rgfInOut);
        [PreserveSig]
        int GetUIObjectOf(IntPtr hwndOwner, uint cidl, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] IntPtr[] apidl, [In] ref Guid riid, ref uint rgfReserved, out IntPtr ppv);
        [PreserveSig]
        int GetDisplayNameOf(IntPtr pidl, uint uFlags, IntPtr pName);
        [PreserveSig]
        int SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszName, uint uFlags, out IntPtr ppidlOut);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214e4-0000-0000-c000-000000000046")]
    private interface IContextMenu
    {
        [PreserveSig]
        int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        [PreserveSig]
        int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
        [PreserveSig]
        int GetCommandString(UIntPtr idcmd, uint uflags, IntPtr reserved, [MarshalAs(UnmanagedType.LPArray)] byte[] commandstring, uint cch);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214f4-0000-0000-c000-000000000046")]
    private interface IContextMenu2 : IContextMenu
    {
        [PreserveSig]
        new int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        [PreserveSig]
        new int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
        [PreserveSig]
        new int GetCommandString(UIntPtr idcmd, uint uflags, IntPtr reserved, [MarshalAs(UnmanagedType.LPArray)] byte[] commandstring, uint cch);
        [PreserveSig]
        int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("bcfce0a0-ec17-11d0-8d10-00a0c90f2719")]
    private interface IContextMenu3 : IContextMenu2
    {
        [PreserveSig]
        new int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        [PreserveSig]
        new int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
        [PreserveSig]
        new int GetCommandString(UIntPtr idcmd, uint uflags, IntPtr reserved, [MarshalAs(UnmanagedType.LPArray)] byte[] commandstring, uint cch);
        [PreserveSig]
        new int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
        [PreserveSig]
        int HandleMenuMsg2(uint uMsg, IntPtr wParam, IntPtr lParam, ref IntPtr plResult);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CMINVOKECOMMANDINFOEX
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        [MarshalAs(UnmanagedType.LPStr)]
        public string? lpParameters;
        [MarshalAs(UnmanagedType.LPStr)]
        public string? lpDirectory;
        public int nShow;
        public uint dwHotKey;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.LPStr)]
        public string? lpTitle;
        public IntPtr lpVerbW;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpParametersW;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpDirectoryW;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpTitleW;
        public POINT ptInvoke;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    #endregion

    #region Win32 Constants & P/Invokes

    private const uint CMF_NORMAL = 0x00000000;
    private const uint CMF_EXPLORE = 0x00000004;

    private const uint TPM_LEFTBUTTON = 0x0000;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_RETURNCMD = 0x0100;

    private const uint CMIC_MASK_UNICODE = 0x00004000;
    private const int SW_SHOWNORMAL = 1;

    private const int WM_INITMENUPOPUP = 0x0117;
    private const int WM_DRAWITEM = 0x002B;
    private const int WM_MEASUREITEM = 0x002C;
    private const int WM_MENUCHAR = 0x0120;

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int SHGetDesktopFolder(out IShellFolder ppshf);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHParseDisplayName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszName,
        IntPtr pbc,
        out IntPtr ppidl,
        uint sfgaoIn,
        out uint psfgaoOut);

    [DllImport("shell32.dll")]
    private static extern void ILFree(IntPtr pidl);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint TrackPopupMenuEx(
        IntPtr hmenu,
        uint fuFlags,
        int x,
        int y,
        IntPtr hwnd,
        IntPtr lptpm);

    #endregion

    /// <summary>
    /// Displays the native Windows Shell context menu for one or more files/folders.
    /// </summary>
    public static void ShowContextMenu(IReadOnlyList<string> paths, System.Windows.Point screenPoint, IntPtr hwndOwner)
    {
        if (paths == null || paths.Count == 0) return;
        var validPaths = paths.Where(p => !string.IsNullOrWhiteSpace(p) && (File.Exists(p) || Directory.Exists(p))).ToList();
        if (validPaths.Count == 0) return;

        // Group by directory so we get UI object from common parent
        string firstPath = validPaths[0];
        string? parentDir = Path.GetDirectoryName(firstPath);
        if (string.IsNullOrEmpty(parentDir))
        {
            // Root drive, parent is Desktop
            ShowContextMenuFromDesktop(validPaths, screenPoint, hwndOwner);
            return;
        }

        IntPtr parentFolderPtr = IntPtr.Zero;
        IShellFolder? parentFolder = null;
        IntPtr parentPidl = IntPtr.Zero;
        var childPidls = new List<IntPtr>();
        IntPtr contextMenuPtr = IntPtr.Zero;
        IntPtr hMenu = IntPtr.Zero;

        try
        {
            if (SHGetDesktopFolder(out var desktop) != 0 || desktop == null) return;

            int hr = SHParseDisplayName(parentDir, IntPtr.Zero, out parentPidl, 0, out _);
            if (hr != 0 || parentPidl == IntPtr.Zero) return;

            var shellFolderGuid = IID_IShellFolder;
            hr = desktop.BindToObject(parentPidl, IntPtr.Zero, ref shellFolderGuid, out parentFolderPtr);
            if (hr != 0 || parentFolderPtr == IntPtr.Zero) return;

            parentFolder = (IShellFolder)Marshal.GetObjectForIUnknown(parentFolderPtr);

            foreach (var path in validPaths)
            {
                string fileName = Path.GetFileName(path);
                uint eaten = 0;
                uint attribs = 0;
                hr = parentFolder.ParseDisplayName(hwndOwner, IntPtr.Zero, fileName, ref eaten, out IntPtr childPidl, ref attribs);
                if (hr == 0 && childPidl != IntPtr.Zero)
                {
                    childPidls.Add(childPidl);
                }
            }

            if (childPidls.Count == 0) return;

            var contextMenuGuid = IID_IContextMenu;
            uint reserved = 0;
            hr = parentFolder.GetUIObjectOf(hwndOwner, (uint)childPidls.Count, childPidls.ToArray(), ref contextMenuGuid, ref reserved, out contextMenuPtr);
            if (hr != 0 || contextMenuPtr == IntPtr.Zero) return;

            var iContextMenu = (IContextMenu)Marshal.GetObjectForIUnknown(contextMenuPtr);
            var iContextMenu2 = iContextMenu as IContextMenu2;
            var iContextMenu3 = iContextMenu as IContextMenu3;

            hMenu = CreatePopupMenu();
            if (hMenu == IntPtr.Zero) return;

            iContextMenu.QueryContextMenu(hMenu, 0, 1, 0x7FFF, CMF_NORMAL | CMF_EXPLORE);

            // Install temporary window hook to forward owner-drawn items & submenus
            HwndSourceHook hook = (IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                if (msg == WM_INITMENUPOPUP || msg == WM_DRAWITEM || msg == WM_MEASUREITEM || msg == WM_MENUCHAR)
                {
                    if (iContextMenu3 != null)
                    {
                        IntPtr lResult = IntPtr.Zero;
                        if (iContextMenu3.HandleMenuMsg2((uint)msg, wParam, lParam, ref lResult) == 0)
                        {
                            handled = true;
                            return lResult;
                        }
                    }
                    else if (iContextMenu2 != null)
                    {
                        if (iContextMenu2.HandleMenuMsg((uint)msg, wParam, lParam) == 0)
                        {
                            handled = true;
                            return IntPtr.Zero;
                        }
                    }
                }
                return IntPtr.Zero;
            };

            var source = HwndSource.FromHwnd(hwndOwner);
            source?.AddHook(hook);

            try
            {
                uint cmd = TrackPopupMenuEx(
                    hMenu,
                    TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_LEFTBUTTON,
                    (int)screenPoint.X,
                    (int)screenPoint.Y,
                    hwndOwner,
                    IntPtr.Zero);

                if (cmd > 0)
                {
                    var invokeInfo = new CMINVOKECOMMANDINFOEX
                    {
                        cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
                        fMask = CMIC_MASK_UNICODE,
                        hwnd = hwndOwner,
                        lpVerb = (IntPtr)(cmd - 1),
                        lpVerbW = (IntPtr)(cmd - 1),
                        nShow = SW_SHOWNORMAL
                    };
                    iContextMenu.InvokeCommand(ref invokeInfo);
                }
            }
            finally
            {
                source?.RemoveHook(hook);
            }
        }
        catch
        {
            // Safe fallback
        }
        finally
        {
            if (hMenu != IntPtr.Zero) DestroyMenu(hMenu);
            if (contextMenuPtr != IntPtr.Zero) Marshal.Release(contextMenuPtr);
            foreach (var pidl in childPidls) ILFree(pidl);
            if (parentFolderPtr != IntPtr.Zero) Marshal.Release(parentFolderPtr);
            if (parentPidl != IntPtr.Zero) ILFree(parentPidl);
        }
    }

    /// <summary>
    /// Displays the native Windows Shell context menu for top-level root items.
    /// </summary>
    private static void ShowContextMenuFromDesktop(IReadOnlyList<string> paths, System.Windows.Point screenPoint, IntPtr hwndOwner)
    {
        if (SHGetDesktopFolder(out var desktop) != 0 || desktop == null) return;

        var pidls = new List<IntPtr>();
        IntPtr contextMenuPtr = IntPtr.Zero;
        IntPtr hMenu = IntPtr.Zero;

        try
        {
            foreach (var path in paths)
            {
                int hr = SHParseDisplayName(path, IntPtr.Zero, out IntPtr pidl, 0, out _);
                if (hr == 0 && pidl != IntPtr.Zero)
                {
                    pidls.Add(pidl);
                }
            }

            if (pidls.Count == 0) return;

            var contextMenuGuid = IID_IContextMenu;
            uint reserved = 0;
            int hrMenu = desktop.GetUIObjectOf(hwndOwner, (uint)pidls.Count, pidls.ToArray(), ref contextMenuGuid, ref reserved, out contextMenuPtr);
            if (hrMenu != 0 || contextMenuPtr == IntPtr.Zero) return;

            var iContextMenu = (IContextMenu)Marshal.GetObjectForIUnknown(contextMenuPtr);
            hMenu = CreatePopupMenu();
            if (hMenu == IntPtr.Zero) return;

            iContextMenu.QueryContextMenu(hMenu, 0, 1, 0x7FFF, CMF_NORMAL | CMF_EXPLORE);

            uint cmd = TrackPopupMenuEx(
                hMenu,
                TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_LEFTBUTTON,
                (int)screenPoint.X,
                (int)screenPoint.Y,
                hwndOwner,
                IntPtr.Zero);

            if (cmd > 0)
            {
                var invokeInfo = new CMINVOKECOMMANDINFOEX
                {
                    cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
                    fMask = CMIC_MASK_UNICODE,
                    hwnd = hwndOwner,
                    lpVerb = (IntPtr)(cmd - 1),
                    lpVerbW = (IntPtr)(cmd - 1),
                    nShow = SW_SHOWNORMAL
                };
                iContextMenu.InvokeCommand(ref invokeInfo);
            }
        }
        catch
        {
            // Safe fallback
        }
        finally
        {
            if (hMenu != IntPtr.Zero) DestroyMenu(hMenu);
            if (contextMenuPtr != IntPtr.Zero) Marshal.Release(contextMenuPtr);
            foreach (var pidl in pidls) ILFree(pidl);
        }
    }

    /// <summary>
    /// Displays the native Windows Shell background context menu for a folder.
    /// </summary>
    public static void ShowBackgroundContextMenu(string folderPath, System.Windows.Point screenPoint, IntPtr hwndOwner)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath)) return;

        IntPtr folderPidl = IntPtr.Zero;
        IntPtr folderPtr = IntPtr.Zero;
        IntPtr contextMenuPtr = IntPtr.Zero;
        IntPtr hMenu = IntPtr.Zero;

        try
        {
            if (SHGetDesktopFolder(out var desktop) != 0 || desktop == null) return;

            int hr = SHParseDisplayName(folderPath, IntPtr.Zero, out folderPidl, 0, out _);
            if (hr != 0 || folderPidl == IntPtr.Zero) return;

            var shellFolderGuid = IID_IShellFolder;
            hr = desktop.BindToObject(folderPidl, IntPtr.Zero, ref shellFolderGuid, out folderPtr);
            if (hr != 0 || folderPtr == IntPtr.Zero) return;

            var folder = (IShellFolder)Marshal.GetObjectForIUnknown(folderPtr);
            var contextMenuGuid = IID_IContextMenu;
            hr = folder.CreateViewObject(hwndOwner, ref contextMenuGuid, out contextMenuPtr);
            if (hr != 0 || contextMenuPtr == IntPtr.Zero) return;

            var iContextMenu = (IContextMenu)Marshal.GetObjectForIUnknown(contextMenuPtr);
            hMenu = CreatePopupMenu();
            if (hMenu == IntPtr.Zero) return;

            iContextMenu.QueryContextMenu(hMenu, 0, 1, 0x7FFF, CMF_NORMAL | CMF_EXPLORE);

            uint cmd = TrackPopupMenuEx(
                hMenu,
                TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_LEFTBUTTON,
                (int)screenPoint.X,
                (int)screenPoint.Y,
                hwndOwner,
                IntPtr.Zero);

            if (cmd > 0)
            {
                var invokeInfo = new CMINVOKECOMMANDINFOEX
                {
                    cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
                    fMask = CMIC_MASK_UNICODE,
                    hwnd = hwndOwner,
                    lpVerb = (IntPtr)(cmd - 1),
                    lpVerbW = (IntPtr)(cmd - 1),
                    nShow = SW_SHOWNORMAL
                };
                iContextMenu.InvokeCommand(ref invokeInfo);
            }
        }
        catch
        {
            // Safe fallback
        }
        finally
        {
            if (hMenu != IntPtr.Zero) DestroyMenu(hMenu);
            if (contextMenuPtr != IntPtr.Zero) Marshal.Release(contextMenuPtr);
            if (folderPtr != IntPtr.Zero) Marshal.Release(folderPtr);
            if (folderPidl != IntPtr.Zero) ILFree(folderPidl);
        }
    }
}
