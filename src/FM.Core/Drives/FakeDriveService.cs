namespace FM.Core.Drives;

/// <summary>
/// Controllable in-memory drive service for unit testing without touching physical disks.
/// </summary>
public sealed class FakeDriveService : IDriveService
{
    private readonly List<DriveItem> _drives = new();

    public event EventHandler? DrivesChanged;
    public List<string> EjectedDrives { get; } = new();

    public FakeDriveService(IEnumerable<DriveItem>? initialDrives = null)
    {
        if (initialDrives != null)
        {
            _drives.AddRange(initialDrives);
        }
        else
        {
            // Default common drives for test environments
            _drives.Add(new DriveItem(@"C:\", "Windows", System.IO.DriveType.Fixed, 500_000_000_000L, 200_000_000_000L, "NTFS", true));
            _drives.Add(new DriveItem(@"D:\", "Data", System.IO.DriveType.Fixed, 1_000_000_000_000L, 600_000_000_000L, "NTFS", true));
        }
    }

    public Task<IReadOnlyList<DriveItem>> GetDrivesAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<DriveItem>>(_drives.ToList());
    }

    public Task<bool> EjectDriveAsync(string drivePath, CancellationToken ct = default)
    {
        EjectedDrives.Add(drivePath);
        var existing = _drives.FirstOrDefault(d => string.Equals(d.Path, drivePath, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            _drives.Remove(existing);
            NotifyDrivesChanged();
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public void AddDrive(DriveItem drive)
    {
        _drives.Add(drive);
        NotifyDrivesChanged();
    }

    public void RemoveDrive(string drivePath)
    {
        _drives.RemoveAll(d => string.Equals(d.Path, drivePath, StringComparison.OrdinalIgnoreCase));
        NotifyDrivesChanged();
    }

    public void NotifyDrivesChanged()
    {
        DrivesChanged?.Invoke(this, EventArgs.Empty);
    }
}

