namespace FM.Core.Watching;

/// <summary>
/// A no-op folder watcher that raises no events and consumes no resources.
/// Useful as a default for in-memory file systems or disabled auto-refresh.
/// </summary>
public class NullFolderWatcher : IFolderWatcher
{
    public string WatchedPath { get; }
    public bool EnableRaisingEvents { get; set; }

    public event EventHandler<FolderChangeEventArgs>? Changed
    {
        add { }
        remove { }
    }

    public event EventHandler<FolderWatcherErrorEventArgs>? Error
    {
        add { }
        remove { }
    }

    public event EventHandler? FolderDeleted
    {
        add { }
        remove { }
    }

    public NullFolderWatcher(string watchedPath = "")
    {
        WatchedPath = watchedPath;
    }

    public void Dispose()
    {
        // No-op
    }
}

/// <summary>
/// A factory producing <see cref="NullFolderWatcher"/> instances.
/// </summary>
public class NullFolderWatcherFactory : IFolderWatcherFactory
{
    public static readonly NullFolderWatcherFactory Instance = new();

    public IFolderWatcher CreateWatcher(string folderPath) => new NullFolderWatcher(folderPath);
}
