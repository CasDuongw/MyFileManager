using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace FM.Core.RecycleBin;

/// <summary>
/// Production Windows Recycle Bin service interacting with Windows Shell COM interfaces off the UI thread.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsRecycleBinService : IRecycleBinService
{
    public static readonly WindowsRecycleBinService Instance = new();

    public event EventHandler? RecycleBinChanged;

    public async Task<IReadOnlyList<RecycleBinItem>> GetItemsAsync(CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var result = new List<RecycleBinItem>();

            if (!OperatingSystem.IsWindows())
            {
                return (IReadOnlyList<RecycleBinItem>)result;
            }

            try
            {
                Type? shellAppType = Type.GetTypeFromProgID("Shell.Application");
                if (shellAppType == null) return (IReadOnlyList<RecycleBinItem>)result;

                dynamic shell = Activator.CreateInstance(shellAppType)!;
                dynamic rb = shell.NameSpace(10); // 10 = ssfBITBUCKET
                if (rb == null) return (IReadOnlyList<RecycleBinItem>)result;

                dynamic items = rb.Items();
                int count = items.Count;

                for (int i = 0; i < count; i++)
                {
                    if (ct.IsCancellationRequested) break;

                    try
                    {
                        dynamic it = items.Item(i);
                        string name = (string)it.Name ?? string.Empty;
                        string path = (string)it.Path ?? string.Empty;
                        string originalLoc = (string)rb.GetDetailsOf(it, 1) ?? string.Empty;
                        string dateDeletedRaw = (string)rb.GetDetailsOf(it, 2) ?? string.Empty;
                        string itemType = (string)rb.GetDetailsOf(it, 4) ?? string.Empty;

                        bool isDir = (bool)it.IsFolder;

                        long size = 0;
                        try
                        {
                            size = Convert.ToInt64(it.Size);
                        }
                        catch
                        {
                            // Some folder sizes or special files
                        }

                        DateTime dt = DateTime.MinValue;
                        if (!string.IsNullOrWhiteSpace(dateDeletedRaw))
                        {
                            // Clean Unicode marks that Windows Shell may insert
                            string cleanDate = dateDeletedRaw.Replace("\u200E", "").Replace("\u200F", "").Replace("?", "").Trim();
                            DateTime.TryParse(cleanDate, out dt);
                        }

                        result.Add(new RecycleBinItem(
                            id: path,
                            name: name,
                            originalLocation: originalLoc,
                            dateDeleted: dt,
                            size: size,
                            itemType: itemType,
                            isDirectory: isDir));
                    }
                    catch
                    {
                        // Ignore individual item read errors
                    }
                }
            }
            catch
            {
                // Fall back gracefully
            }

            return (IReadOnlyList<RecycleBinItem>)result;
        }, ct).ConfigureAwait(false);
    }

    public async Task<bool> RestoreItemAsync(string id, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id) || !OperatingSystem.IsWindows())
            return false;

        return await Task.Run(() =>
        {
            try
            {
                Type? shellAppType = Type.GetTypeFromProgID("Shell.Application");
                if (shellAppType == null) return false;

                dynamic shell = Activator.CreateInstance(shellAppType)!;
                dynamic rb = shell.NameSpace(10);
                if (rb == null) return false;

                foreach (dynamic it in rb.Items())
                {
                    if (string.Equals((string)it.Path, id, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals((string)it.Name, id, StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (dynamic v in it.Verbs())
                        {
                            string vName = ((string)v.Name ?? string.Empty).ToLowerInvariant();
                            if (vName.Contains("restore") || vName.Contains("phục hồi") || vName.Contains("khôi phục"))
                            {
                                v.DoIt();
                                RecycleBinChanged?.Invoke(this, EventArgs.Empty);
                                return true;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Graceful fallback
            }

            return false;
        }, ct).ConfigureAwait(false);
    }

    public async Task<bool> PermanentlyDeleteItemAsync(string id, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id) || !OperatingSystem.IsWindows())
            return false;

        return await Task.Run(() =>
        {
            try
            {
                Type? shellAppType = Type.GetTypeFromProgID("Shell.Application");
                if (shellAppType == null) return false;

                dynamic shell = Activator.CreateInstance(shellAppType)!;
                dynamic rb = shell.NameSpace(10);
                if (rb == null) return false;

                foreach (dynamic it in rb.Items())
                {
                    if (string.Equals((string)it.Path, id, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals((string)it.Name, id, StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (dynamic v in it.Verbs())
                        {
                            string vName = ((string)v.Name ?? string.Empty).ToLowerInvariant();
                            if (vName.Contains("delete") || vName.Contains("xóa"))
                            {
                                v.DoIt();
                                RecycleBinChanged?.Invoke(this, EventArgs.Empty);
                                return true;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Graceful fallback
            }

            return false;
        }, ct).ConfigureAwait(false);
    }

    public async Task<bool> EmptyRecycleBinAsync(bool confirm = true, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows()) return false;

        return await Task.Run(() =>
        {
            try
            {
                uint flags = confirm ? 0u : 0x00000001u;
                int hr = SHEmptyRecycleBin(IntPtr.Zero, null, flags);
                if (hr == 0) // S_OK
                {
                    RecycleBinChanged?.Invoke(this, EventArgs.Empty);
                    return true;
                }
            }
            catch
            {
                // Graceful fallback
            }

            return false;
        }, ct).ConfigureAwait(false);
    }

    [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);
}
