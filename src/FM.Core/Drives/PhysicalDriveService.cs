using System.IO;
using System.Runtime.InteropServices;

namespace FM.Core.Drives;

/// <summary>
/// Production drive service using System.IO.DriveInfo off the UI thread with bounded timeouts
/// and shell eject capabilities.
/// </summary>
public sealed class PhysicalDriveService : IDriveService
{
    public static readonly PhysicalDriveService Instance = new();

    public event EventHandler? DrivesChanged;

    public void NotifyDrivesChanged()
    {
        DrivesChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<IReadOnlyList<DriveItem>> GetDrivesAsync(CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var result = new List<DriveItem>();
            DriveInfo[] drives;
            try
            {
                drives = DriveInfo.GetDrives();
            }
            catch
            {
                return (IReadOnlyList<DriveItem>)result;
            }

            foreach (var d in drives)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    // Interrogate drive properties with bounded timeout protection
                    var queryTask = Task.Run(() =>
                    {
                        if (!d.IsReady)
                        {
                            return new DriveItem(
                                d.Name,
                                string.Empty,
                                d.DriveType,
                                totalBytes: 0,
                                freeBytes: 0,
                                fileSystem: string.Empty,
                                isReady: false);
                        }

                        string label = string.Empty;
                        try { label = d.VolumeLabel; } catch { }

                        string fs = string.Empty;
                        try { fs = d.DriveFormat; } catch { }

                        long total = 0;
                        try { total = d.TotalSize; } catch { }

                        long free = 0;
                        try { free = d.TotalFreeSpace; } catch { }

                        return new DriveItem(d.Name, label, d.DriveType, total, free, fs, isReady: true);
                    }, ct);

                    // 800 ms safety threshold per drive to prevent unresponsive network/optical drives from hanging
                    if (queryTask.Wait(800, ct))
                    {
                        var item = queryTask.Result;
                        if (item.IsReady)
                        {
                            result.Add(item);
                        }
                    }
                }
                catch
                {
                    // Non-critical if individual drive fails interrogation
                }
            }

            return (IReadOnlyList<DriveItem>)result;
        }, ct).ConfigureAwait(false);
    }

    public async Task<bool> EjectDriveAsync(string drivePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(drivePath)) return false;

        return await Task.Run(() =>
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    string driveLetter = drivePath.TrimEnd('\\');
                    string volumePath = $@"\\.\{driveLetter}";

                    const uint GENERIC_READ = 0x80000000;
                    const uint GENERIC_WRITE = 0x40000000;
                    const uint FILE_SHARE_READ = 0x1;
                    const uint FILE_SHARE_WRITE = 0x2;
                    const uint OPEN_EXISTING = 3;
                    const uint FSCTL_LOCK_VOLUME = 0x00090018;
                    const uint FSCTL_DISMOUNT_VOLUME = 0x00090020;
                    const uint IOCTL_STORAGE_EJECT_MEDIA = 0x002D4808;

                    IntPtr handle = CreateFile(
                        volumePath,
                        GENERIC_READ | GENERIC_WRITE,
                        FILE_SHARE_READ | FILE_SHARE_WRITE,
                        IntPtr.Zero,
                        OPEN_EXISTING,
                        0,
                        IntPtr.Zero);

                    if (handle != IntPtr.Zero && handle.ToInt64() != -1)
                    {
                        try
                        {
                            DeviceIoControl(handle, FSCTL_LOCK_VOLUME, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);
                            DeviceIoControl(handle, FSCTL_DISMOUNT_VOLUME, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);
                            bool ejected = DeviceIoControl(handle, IOCTL_STORAGE_EJECT_MEDIA, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);

                            NotifyDrivesChanged();
                            return ejected;
                        }
                        finally
                        {
                            CloseHandle(handle);
                        }
                    }
                }
            }
            catch
            {
                // Fall back gracefully
            }

            return false;
        }, ct).ConfigureAwait(false);
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern bool DeviceIoControl(
        IntPtr hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);
}

