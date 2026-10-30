using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FM.App.Services;

/// <summary>
/// Retrieves Windows shell icons via SHGetFileInfo. Caches generic extension icons,
/// unique executable/shortcut icons by path, and folder icons once.
/// Freezes all BitmapSources to enable safe multi-threaded usage.
/// </summary>
public class IconService : IIconService
{
    private static ImageSource? _folderIcon;
    private static readonly object FolderIconLock = new();

    private readonly ConcurrentDictionary<string, ImageSource> _extensionCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ImageSource> _customPathCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> CustomIconExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".lnk", ".ico"
    };

    public ValueTask<ImageSource?> GetIconAsync(string fullPath, bool isDirectory, CancellationToken ct = default)
    {
        // 1. Directory icon: cached globally once
        if (isDirectory)
        {
            if (_folderIcon != null)
            {
                return ValueTask.FromResult<ImageSource?>(_folderIcon);
            }

            lock (FolderIconLock)
            {
                _folderIcon ??= ExtractShellIcon(fullPath, isDirectory: true, useAttributes: true);
                return ValueTask.FromResult<ImageSource?>(_folderIcon);
            }
        }

        string ext = Path.GetExtension(fullPath);

        // 2. Custom icons (.exe, .lnk, .ico): cached by full path
        if (CustomIconExtensions.Contains(ext))
        {
            if (_customPathCache.TryGetValue(fullPath, out var cachedPathIcon))
            {
                return ValueTask.FromResult<ImageSource?>(cachedPathIcon);
            }

            return new ValueTask<ImageSource?>(Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                var icon = ExtractShellIcon(fullPath, isDirectory: false, useAttributes: false);
                if (icon != null)
                {
                    _customPathCache[fullPath] = icon;
                }
                return icon;
            }, ct));
        }

        // 3. Generic extension icons: cached by extension
        if (_extensionCache.TryGetValue(ext, out var cachedExtIcon))
        {
            return ValueTask.FromResult<ImageSource?>(cachedExtIcon);
        }

        return new ValueTask<ImageSource?>(Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            // Use dummy extension path with SHGFI_USEFILEATTRIBUTES to avoid hitting disk
            string dummyPath = string.IsNullOrEmpty(ext) ? "file" : "dummy" + ext;
            var icon = ExtractShellIcon(dummyPath, isDirectory: false, useAttributes: true);
            if (icon != null)
            {
                _extensionCache[ext] = icon;
            }
            return icon;
        }, ct));
    }

    private static ImageSource? ExtractShellIcon(string path, bool isDirectory, bool useAttributes)
    {
        try
        {
            var shinfo = new SHFILEINFO();
            uint flags = SHGFI_ICON | SHGFI_SMALLICON;

            if (useAttributes)
            {
                flags |= SHGFI_USEFILEATTRIBUTES;
            }

            uint attributes = isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;

            IntPtr res = SHGetFileInfo(path, attributes, ref shinfo, (uint)Marshal.SizeOf(shinfo), flags);
            if (res == IntPtr.Zero || shinfo.hIcon == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                // Create WPF BitmapSource from the HICON
                var bitmap = Imaging.CreateBitmapSourceFromHIcon(
                    shinfo.hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());

                // Freeze is mandatory to allow background thread creation and cross-thread UI consumption
                bitmap.Freeze();
                return bitmap;
            }
            finally
            {
                // Clean up native Win32 icon handle to prevent GDI / User handle leaks
                DestroyIcon(shinfo.hIcon);
            }
        }
        catch
        {
            return null;
        }
    }

    #region Win32 P/Invoke

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_SMALLICON = 0x000000001;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    #endregion
}
