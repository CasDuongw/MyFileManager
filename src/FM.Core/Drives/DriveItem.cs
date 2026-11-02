using System.IO;

namespace FM.Core.Drives;

/// <summary>
/// Immutable snapshot representing a storage drive (Fixed, Removable, Network, Optical).
/// </summary>
public sealed record DriveItem
{
    public string Path { get; init; }
    public string DriveLetter { get; init; }
    public string VolumeLabel { get; init; }
    public string DisplayName { get; init; }
    public DriveType DriveType { get; init; }
    public long TotalBytes { get; init; }
    public long FreeBytes { get; init; }
    public long UsedBytes => Math.Max(0, TotalBytes - FreeBytes);
    public double PercentUsed => TotalBytes > 0 ? (double)UsedBytes / TotalBytes * 100.0 : 0.0;
    public string FileSystem { get; init; }
    public bool IsReady { get; init; }
    public bool IsRemovable => DriveType == DriveType.Removable || DriveType == DriveType.CDRom;

    public string FormattedFreeSpaceText
    {
        get
        {
            if (!IsReady || TotalBytes <= 0) return "Không có sẵn";
            return $"{FormatBytes(FreeBytes)} còn trống trên {FormatBytes(TotalBytes)}";
        }
    }

    public DriveItem(
        string path,
        string volumeLabel,
        DriveType driveType,
        long totalBytes,
        long freeBytes,
        string fileSystem = "NTFS",
        bool isReady = true)
    {
        Path = path ?? string.Empty;
        DriveLetter = Path.TrimEnd('\\');
        VolumeLabel = volumeLabel ?? string.Empty;
        DriveType = driveType;
        TotalBytes = totalBytes;
        FreeBytes = freeBytes;
        FileSystem = fileSystem ?? string.Empty;
        IsReady = isReady;

        DisplayName = string.IsNullOrWhiteSpace(VolumeLabel)
            ? DriveLetter
            : $"{VolumeLabel} ({DriveLetter})";
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 0) return "0 B";
        string[] suffixes = { "B", "KB", "MB", "GB", "TB", "PB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024m) >= 1 && counter < suffixes.Length - 1)
        {
            number /= 1024m;
            counter++;
        }
        return $"{number:F1} {suffixes[counter]}";
    }
}

