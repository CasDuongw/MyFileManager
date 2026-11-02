namespace FM.Core.Watching;

/// <summary>
/// Abstraction for watching file system changes in a single folder.
/// Implementations can wrap System.IO.FileSystemWatcher, low-frequency polling, or test fakes.
/// </summary>
public interface IFolderWatcher : IDisposable
{
    /// <summary>
    /// Gets the absolute directory path being watched.
    /// </summary>
    string WatchedPath { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the watcher raises events.
    /// </summary>
    bool EnableRaisingEvents { get; set; }

    /// <summary>
    /// Occurs when a file or directory in the watched folder is created, deleted, renamed, or modified.
    /// </summary>
    event EventHandler<FolderChangeEventArgs>? Changed;

    /// <summary>
    /// Occurs when an error occurs while watching, such as a buffer overflow.
    /// </summary>
    event EventHandler<FolderWatcherErrorEventArgs>? Error;

    /// <summary>
    /// Occurs when the watched folder itself has been deleted or is no longer accessible.
    /// </summary>
    event EventHandler? FolderDeleted;
}

