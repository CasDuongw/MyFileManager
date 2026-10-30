using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FM.App.Services;

public sealed class ThumbnailService : IThumbnailService, IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
        public SIZE(int cx, int cy) { this.cx = cx; this.cy = cy; }
    }

    [Flags]
    private enum SIIGBF
    {
        SIIGBF_RESIZETOFIT = 0x00,
        SIIGBF_BIGGERSIZEOK = 0x01,
        SIIGBF_MEMORYONLY = 0x02,
        SIIGBF_ICONONLY = 0x04,
        SIIGBF_THUMBNAILONLY = 0x08,
        SIIGBF_INCACHEONLY = 0x10
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(
            [In, MarshalAs(UnmanagedType.Struct)] SIZE size,
            [In] SIIGBF flags,
            [Out] out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [Out] out IShellItemImageFactory ppv);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    private class ThumbnailRequest
    {
        public string Path { get; }
        public int Size { get; }
        public TaskCompletionSource<ImageSource?> Tcs { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Ct { get; }

        public ThumbnailRequest(string path, int size, CancellationToken ct)
        {
            Path = path;
            Size = size;
            Ct = ct;
        }
    }

    private readonly IIconService _iconService;
    private readonly ConcurrentDictionary<string, ImageSource> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly BlockingCollection<ThumbnailRequest> _workQueue = new();
    private readonly Thread _workerThread;
    private bool _disposed;

    private static readonly Guid IShellItemImageFactoryGuid = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    public ThumbnailService(IIconService iconService)
    {
        _iconService = iconService ?? throw new ArgumentNullException(nameof(iconService));
        _workerThread = new Thread(ProcessQueue)
        {
            Name = "STA-ThumbnailWorker",
            IsBackground = true
        };
        _workerThread.SetApartmentState(ApartmentState.STA);
        _workerThread.Start();
    }

    public async Task<ImageSource?> GetThumbnailAsync(string path, int size = 96, CancellationToken ct = default)
    {
        if (_disposed || ct.IsCancellationRequested || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string cacheKey = $"{path}_{size}";
        if (_cache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var request = new ThumbnailRequest(path, size, ct);
        _workQueue.Add(request, ct);

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            using (linkedCts.Token.Register(() => request.Tcs.TrySetResult(null)))
            {
                var result = await request.Tcs.Task.ConfigureAwait(false);
                if (result != null)
                {
                    if (_cache.Count < 64)
                    {
                        _cache[cacheKey] = result;
                    }
                    return result;
                }
            }
        }
        catch
        {
            // Fall back to icon on timeout or error
        }

        return await _iconService.GetIconAsync(path, isDirectory: false, isDrive: false, ct: ct).ConfigureAwait(false);
    }

    private void ProcessQueue()
    {
        foreach (var req in _workQueue.GetConsumingEnumerable())
        {
            if (req.Ct.IsCancellationRequested)
            {
                req.Tcs.TrySetResult(null);
                continue;
            }

            ImageSource? image = null;
            IntPtr hBitmap = IntPtr.Zero;

            try
            {
                int hr = SHCreateItemFromParsingName(req.Path, IntPtr.Zero, IShellItemImageFactoryGuid, out var factory);
                if (hr == 0 && factory != null)
                {
                    var sz = new SIZE(req.Size, req.Size);
                    int hrImage = factory.GetImage(sz, SIIGBF.SIIGBF_RESIZETOFIT, out hBitmap);
                    if (hrImage == 0 && hBitmap != IntPtr.Zero)
                    {
                        var bs = Imaging.CreateBitmapSourceFromHBitmap(
                            hBitmap,
                            IntPtr.Zero,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                        bs.Freeze();
                        image = bs;
                    }
                }
            }
            catch
            {
                // Fall back to null
            }
            finally
            {
                if (hBitmap != IntPtr.Zero)
                {
                    DeleteObject(hBitmap);
                }
            }

            req.Tcs.TrySetResult(image);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _workQueue.CompleteAdding();
    }
}

