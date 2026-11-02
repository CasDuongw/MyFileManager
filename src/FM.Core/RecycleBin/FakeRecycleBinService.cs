using FM.Core.FileSystem;

namespace FM.Core.RecycleBin;

/// <summary>
/// Controllable in-memory Recycle Bin service for unit tests (zero disk I/O, zero real deletions).
/// </summary>
public sealed class FakeRecycleBinService : IRecycleBinService
{
    private readonly List<RecycleBinItem> _items = new();

    public event EventHandler? RecycleBinChanged;
    public List<string> RestoredItemIds { get; } = new();
    public List<string> DeletedItemIds { get; } = new();
    public int EmptyCount { get; private set; }
    public IFileSystem? FileSystem { get; set; }

    public FakeRecycleBinService(IEnumerable<RecycleBinItem>? initialItems = null)
    {
        if (initialItems != null)
        {
            _items.AddRange(initialItems);
        }
    }

    public Task<IReadOnlyList<RecycleBinItem>> GetItemsAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<RecycleBinItem>>(_items.ToList());
    }

    public Task<bool> RestoreItemAsync(string id, CancellationToken ct = default)
    {
        RestoredItemIds.Add(id);
        var existing = _items.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase) ||
                                                  string.Equals(i.Name, id, StringComparison.OrdinalIgnoreCase) ||
                                                  string.Equals(i.OriginalPath, id, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            _items.Remove(existing);
            if (FileSystem != null && !string.IsNullOrEmpty(existing.OriginalPath))
            {
                if (!FileSystem.Exists(existing.OriginalPath))
                {
                    using (FileSystem.CreateFileForWrite(existing.OriginalPath)) { }
                }
            }
            RecycleBinChanged?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public Task<bool> PermanentlyDeleteItemAsync(string id, CancellationToken ct = default)
    {
        DeletedItemIds.Add(id);
        var existing = _items.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase) ||
                                                  string.Equals(i.Name, id, StringComparison.OrdinalIgnoreCase) ||
                                                  string.Equals(i.OriginalPath, id, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            _items.Remove(existing);
            RecycleBinChanged?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public Task<bool> EmptyRecycleBinAsync(bool confirm = true, CancellationToken ct = default)
    {
        EmptyCount++;
        _items.Clear();
        RecycleBinChanged?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(true);
    }

    public void AddItem(RecycleBinItem item)
    {
        _items.Add(item);
        RecycleBinChanged?.Invoke(this, EventArgs.Empty);
    }
}

