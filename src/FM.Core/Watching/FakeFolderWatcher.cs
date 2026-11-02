namespace FM.Core.Watching;

/// <summary>
/// Controllable in-memory fake folder watcher for automated tests.
/// Allows tests to simulate file additions, deletions, renames, modifications,
/// buffer overflows, and folder deletion without touching disk.
/// </summary>
public class FakeFolderWatcher : IFolderWatcher
{
    public string WatchedPath { get; }
    public bool EnableRaisingEvents { get; set; } = true;
    public bool IsDisposed { get; private set; }

    public event EventHandler<FolderChangeEventArgs>? Changed;
    public event EventHandler<FolderWatcherErrorEventArgs>? Error;
    public event EventHandler? FolderDeleted;

    public FakeFolderWatcher(string watchedPath)
    {
        WatchedPath = watchedPath;
    }

    public void TriggerCreated(string fullPath)
    {
        if (IsDisposed || !EnableRaisingEvents) return;
        Changed?.Invoke(this, new FolderChangeEventArgs(FolderChangeType.Created, fullPath));
    }

    public void TriggerDeleted(string fullPath)
    {
        if (IsDisposed || !EnableRaisingEvents) return;
        Changed?.Invoke(this, new FolderChangeEventArgs(FolderChangeType.Deleted, fullPath));
    }

    public void TriggerRenamed(string newFullPath, string oldFullPath)
    {
        if (IsDisposed || !EnableRaisingEvents) return;
        Changed?.Invoke(this, new FolderChangeEventArgs(FolderChangeType.Renamed, newFullPath, oldFullPath));
    }

    public void TriggerChanged(string fullPath)
    {
        if (IsDisposed || !EnableRaisingEvents) return;
        Changed?.Invoke(this, new FolderChangeEventArgs(FolderChangeType.Changed, fullPath));
    }

    public void TriggerError(Exception? exception = null, bool isBufferOverflow = false)
    {
        if (IsDisposed || !EnableRaisingEvents) return;
        Error?.Invoke(this, new FolderWatcherErrorEventArgs(exception, isBufferOverflow));
    }

    public void TriggerFolderDeleted()
    {
        if (IsDisposed || !EnableRaisingEvents) return;
        FolderDeleted?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        IsDisposed = true;
    }
}

/// <summary>
/// Factory producing <see cref="FakeFolderWatcher"/> instances and tracking them for tests.
/// </summary>
public class FakeFolderWatcherFactory : IFolderWatcherFactory
{
    private readonly List<FakeFolderWatcher> _watchers = new();
    private readonly object _lock = new();

    public IReadOnlyList<FakeFolderWatcher> Watchers
    {
        get
        {
            lock (_lock) return _watchers.ToList();
        }
    }

    public FakeFolderWatcher? LastCreatedWatcher
    {
        get
        {
            lock (_lock) return _watchers.LastOrDefault();
        }
    }

    public IFolderWatcher CreateWatcher(string folderPath)
    {
        lock (_lock)
        {
            var watcher = new FakeFolderWatcher(folderPath);
            _watchers.Add(watcher);
            return watcher;
        }
    }
}

