namespace FM.Core.Drives;

/// <summary>
/// Service abstraction for querying system drives, listening to drive changes, and ejecting removable drives.
/// </summary>
public interface IDriveService
{
    /// <summary>
    /// Enumerates all ready drives asynchronously off the UI thread without blocking.
    /// </summary>
    Task<IReadOnlyList<DriveItem>> GetDrivesAsync(CancellationToken ct = default);

    /// <summary>
    /// Safely ejects a removable storage device.
    /// </summary>
    Task<bool> EjectDriveAsync(string drivePath, CancellationToken ct = default);

    /// <summary>
    /// Fires when a drive is attached, removed, or changed.
    /// </summary>
    event EventHandler? DrivesChanged;

    /// <summary>
    /// Triggers the DrivesChanged notification (e.g. from WM_DEVICECHANGE).
    /// </summary>
    void NotifyDrivesChanged();
}

