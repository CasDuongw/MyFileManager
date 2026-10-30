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
/// custom icons (.exe, .lnk, .ico) by full path, drives by root, and folder icons once.
/// All Win32 SHGetFileInfo operations are processed on a dedicated STA background thread
/// to ensure safety with COM-based third-party shell extensions.
/// </summary>
public class IconService : IIconService
{
    private class IconWorkItem
    {
        public string Path { get; }
        public bool IsDirectory { get; }
        public bool IsDrive { get; }
        public bool UseAttributes { get; }
        public TaskCompletionSource<ImageSource?> Tcs { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Ct { get; }

        public IconWorkItem(string path, bool isDirectory, bool isDrive, bool useAttributes, CancellationToken ct)
        {
            Path = path;
            IsDirectory = isDirectory;
            IsDrive = isDrive;
            UseAttributes = useAttributes;
            Ct = ct;
        }
    }

    private static ImageSource? _folderIcon;
    private static readonly object FolderIconLock = new();

    private readonly ConcurrentDictionary<string, ImageSource> _driveIcons = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ImageSource> _extensionCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ImageSource> _customPathCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> CustomIconExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".lnk", ".ico"
    };

    private readonly BlockingCollection<IconWorkItem> _staWorkQueue = new();
    private readonly SemaphoreSlim _concurrencyThrottle = new(4, 4);
    private readonly Thread _staWorkerThread;
    private bool _disposed;

    public IconService()
    {
        _staWorkerThread = new Thread(ProcessStaQueue)
        {
            Name = "STA-ShellIconWorker",
            IsBackground = true
        };
        _staWorkerThread.SetApartmentState(ApartmentState.STA);
        _staWorkerThread.Start();
    }

    public async Task<ImageSource?> GetIconAsync(string fullPath, bool isDirectory, bool isDrive = false, CancellationToken ct = default)
    {
        if (_disposed || ct.IsCancellationRequested) return null;

        // 1. Drive icon: cached by drive root
        if (isDrive)
        {
            string root = Path.GetPathRoot(fullPath) ?? fullPath;
            if (_driveIcons.TryGetValue(root, out var cachedDriveIcon))
            {
                return cachedDriveIcon;
            }

            var icon = await EnqueueWorkAsync(root, isDirectory: true, isDrive: true, useAttributes: false, ct);
            if (icon != null)
            {
                _driveIcons[root] = icon;
            }
            return icon;
        }

        // 2. Directory icon: cached globally once
        if (isDirectory)
        {
            if (_folderIcon != null)
            {
                return _folderIcon;
            }

            lock (FolderIconLock)
            {
                if (_folderIcon != null) return _folderIcon;
            }

            var icon = await EnqueueWorkAsync(fullPath, isDirectory: true, isDrive: false, useAttributes: true, ct);
            if (icon != null)
            {
                lock (FolderIconLock)
                {
                    _folderIcon ??= icon;
                }
            }
            return icon ?? _folderIcon;
        }

        string ext = Path.GetExtension(fullPath);

        // 3. Custom icons (.exe, .lnk, .ico): cached by full path
        if (CustomIconExtensions.Contains(ext))
        {
            if (_customPathCache.TryGetValue(fullPath, out var cachedPathIcon))
            {
                return cachedPathIcon;
            }

            var icon = await EnqueueWorkAsync(fullPath, isDirectory: false, isDrive: false, useAttributes: false, ct);
            if (icon != null)
            {
                _customPathCache[fullPath] = icon;
            }
            return icon;
        }

        // 4. Generic extension icons: cached by extension
        if (_extensionCache.TryGetValue(ext, out var cachedExtIcon))
        {
            return cachedExtIcon;
        }

        string dummyPath = string.IsNullOrEmpty(ext) ? "file" : "dummy" + ext;
        var extIcon = await EnqueueWorkAsync(dummyPath, isDirectory: false, isDrive: false, useAttributes: true, ct);
        if (extIcon != null)
        {
            _extensionCache[ext] = extIcon;
        }
        return extIcon;
    }

    private async Task<ImageSource?> EnqueueWorkAsync(string path, bool isDirectory, bool isDrive, bool useAttributes, CancellationToken ct)
    {
        // Enforce maximum concurrent extractions in-flight to prevent thread pool & queue starvation
        await _concurrencyThrottle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ct.ThrowIfCancellationRequested();
            var workItem = new IconWorkItem(path, isDirectory, isDrive, useAttributes, ct);
            _staWorkQueue.Add(workItem, ct);
            return await workItem.Tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            _concurrencyThrottle.Release();
        }
    }

    private void ProcessStaQueue()
    {
        while (!_disposed)
        {
            IconWorkItem item;
            try
            {
                item = _staWorkQueue.Take();
            }
            catch (InvalidOperationException)
            {
                break;
            }

            if (item.Ct.IsCancellationRequested)
            {
                item.Tcs.TrySetCanceled(item.Ct);
                continue;
            }

            try
            {
                var icon = ExtractShellIconInternal(item.Path, item.IsDirectory, item.IsDrive, item.UseAttributes);
                item.Tcs.TrySetResult(icon);
            }
            catch (Exception ex)
            {
                item.Tcs.TrySetException(ex);
            }
        }
    }

    private static ImageSource? ExtractShellIconInternal(string path, bool isDirectory, bool isDrive, bool useAttributes)
    {
        try
        {
            var shinfo = new SHFILEINFO();
            uint flags = SHGFI_ICON | SHGFI_SMALLICON;

            if (useAttributes && !isDrive)
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
                var bitmap = Imaging.CreateBitmapSourceFromHIcon(
                    shinfo.hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());

                bitmap.Freeze();
                return bitmap;
            }
            finally
            {
                DestroyIcon(shinfo.hIcon);
            }
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _staWorkQueue.CompleteAdding();
        _concurrencyThrottle.Dispose();
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
