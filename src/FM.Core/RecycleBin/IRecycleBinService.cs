namespace FM.Core.RecycleBin;

/// <summary>
/// Service abstraction for enumerating, restoring, permanently deleting, and emptying Recycle Bin contents.
/// </summary>
public interface IRecycleBinService
{
    Task<IReadOnlyList<RecycleBinItem>> GetItemsAsync(CancellationToken ct = default);
    Task<bool> RestoreItemAsync(string id, CancellationToken ct = default);
    Task<bool> PermanentlyDeleteItemAsync(string id, CancellationToken ct = default);
    Task<bool> EmptyRecycleBinAsync(bool confirm = true, CancellationToken ct = default);
    event EventHandler? RecycleBinChanged;
}

