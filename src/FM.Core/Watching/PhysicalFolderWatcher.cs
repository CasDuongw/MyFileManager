using System.IO;

namespace FM.Core.Watching;

/// <summary>
/// Physical file system watcher implementation wrapping <see cref="FileSystemWatcher"/>.
/// Configured with a 64 KB internal buffer to handle bursts without dropping events.
/// </summary>
public class PhysicalFolderWatcher : IFolderWatcher
{
    private FileSystemWatcher? _watcher;
    private bool _disposed;
    private readonly object _lock = new();

    public string WatchedPath { get; }

    public bool EnableRaisingEvents
    {
        get
        {
            lock (_lock)
            {
                return _watcher?.EnableRaisingEvents ?? false;
            }
        }
        set
        {
            lock (_lock)
            {
                if (_disposed || _watcher == null) return;
                try
                {
                    _watcher.EnableRaisingEvents = value;
                }
                catch (Exception ex)
                {
                    Error?.Invoke(this, new FolderWatcherErrorEventArgs(ex));
                }
            }
        }
    }

    public event EventHandler<FolderChangeEventArgs>? Changed;
    public event EventHandler<FolderWatcherErrorEventArgs>? Error;
    public event EventHandler? FolderDeleted;

    public PhysicalFolderWatcher(string folderPath)
    {
        WatchedPath = folderPath ?? throw new ArgumentNullException(nameof(folderPath));

        try
        {
            if (Directory.Exists(folderPath))
            {
                _watcher = new FileSystemWatcher(folderPath)
                {
                    NotifyFilter = NotifyFilters.FileName |
                                   NotifyFilters.DirectoryName |
                                   NotifyFilters.Size |
                                   NotifyFilters.LastWrite,
                    IncludeSubdirectories = false,
                    InternalBufferSize = 65536 // 64 KB buffer (default is 8 KB)
                };

                _watcher.Created += OnCreated;
                _watcher.Deleted += OnDeleted;
                _watcher.Renamed += OnRenamed;
                _watcher.Changed += OnChanged;
                _watcher.Error += OnError;

                _watcher.EnableRaisingEvents = true;
            }
            else
            {
                // Folder does not exist on disk
                FolderDeleted?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            Error?.Invoke(this, new FolderWatcherErrorEventArgs(ex));
        }
    }

    private void OnCreated(object sender, FileSystemEventArgs e)
    {
        if (_disposed) return;
        Changed?.Invoke(this, new FolderChangeEventArgs(FolderChangeType.Created, e.FullPath));
    }

    private void OnDeleted(object sender, FileSystemEventArgs e)
    {
        if (_disposed) return;
        if (string.Equals(e.FullPath, WatchedPath, StringComparison.OrdinalIgnoreCase))
        {
            FolderDeleted?.Invoke(this, EventArgs.Empty);
            return;
        }

        Changed?.Invoke(this, new FolderChangeEventArgs(FolderChangeType.Deleted, e.FullPath));
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        if (_disposed) return;
        Changed?.Invoke(this, new FolderChangeEventArgs(FolderChangeType.Renamed, e.FullPath, e.OldFullPath));
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        if (_disposed) return;
        Changed?.Invoke(this, new FolderChangeEventArgs(FolderChangeType.Changed, e.FullPath));
    }

    private void OnError(object sender, ErrorEventArgs e)
    {
        if (_disposed) return;
        var ex = e.GetException();
        bool isOverflow = ex is InternalBufferOverflowException;
        Error?.Invoke(this, new FolderWatcherErrorEventArgs(ex, isOverflow));
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;

            if (_watcher != null)
            {
                try
                {
                    _watcher.EnableRaisingEvents = false;
                    _watcher.Created -= OnCreated;
                    _watcher.Deleted -= OnDeleted;
                    _watcher.Renamed -= OnRenamed;
                    _watcher.Changed -= OnChanged;
                    _watcher.Error -= OnError;
                    _watcher.Dispose();
                }
                catch
                {
                    // Suppress any cleanup errors
                }
                _watcher = null;
            }
        }
    }
}

