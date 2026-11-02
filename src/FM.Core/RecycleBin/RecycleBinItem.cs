namespace FM.Core.RecycleBin;

/// <summary>
/// Snapshot of an item residing in the Recycle Bin.
/// </summary>
public sealed record RecycleBinItem
{
    public string Id { get; init; }
    public string Name { get; init; }
    public string OriginalLocation { get; init; }
    public string OriginalPath => !string.IsNullOrEmpty(OriginalLocation) ? System.IO.Path.Combine(OriginalLocation, Name) : Name;
    public DateTime DateDeleted { get; init; }
    public long Size { get; init; }
    public string ItemType { get; init; }
    public bool IsDirectory { get; init; }

    public string FormattedSize => IsDirectory ? string.Empty : FormatBytes(Size);
    public string FormattedDateDeleted => DateDeleted != DateTime.MinValue ? DateDeleted.ToString("yyyy-MM-dd HH:mm") : string.Empty;

    public RecycleBinItem(
        string id,
        string name,
        string originalLocation,
        DateTime dateDeleted,
        long size,
        string itemType,
        bool isDirectory)
    {
        Id = id ?? string.Empty;
        Name = name ?? string.Empty;
        OriginalLocation = originalLocation ?? string.Empty;
        DateDeleted = dateDeleted;
        Size = size;
        ItemType = itemType ?? (isDirectory ? "Thư mục" : "Tệp tin");
        IsDirectory = isDirectory;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 0) return "0 B";
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
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

