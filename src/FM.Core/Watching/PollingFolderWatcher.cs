using FM.Core.FileSystem;
using FM.Core.Models;

namespace FM.Core.Watching;

/// <summary>
/// Low-frequency polling watcher for network shares, virtual folders, or paths
/// where physical OS file system watchers are unreliable or unsupported.
/// Caps enumeration to prevent high I/O overhead on large network shares.
/// </summary>
public class PollingFolderWatcher : IFolderWatcher
{
    private readonly IFileSystem _fileSystem;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _interval;
    private readonly int _entryCap;
    private readonly ITimer? _timer;
    private readonly object _lock = new();

    private Dictionary<string, (long Size, DateTime Modified, bool IsDir)> _snapshot = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;
    private bool _isPolling;

    public string WatchedPath { get; }
    public bool EnableRaisingEvents { get; set; } = true;

    public event EventHandler<FolderChangeEventArgs>? Changed;
    public event EventHandler<FolderWatcherErrorEventArgs>? Error;
    public event EventHandler? FolderDeleted;

    public PollingFolderWatcher(
        string folderPath,
        IFileSystem? fileSystem = null,
        TimeProvider? timeProvider = null,
        TimeSpan? interval = null,
        int entryCap = 500)
    {
        WatchedPath = folderPath ?? throw new ArgumentNullException(nameof(folderPath));
        _fileSystem = fileSystem ?? new PhysicalFileSystem();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _interval = interval ?? TimeSpan.FromSeconds(3);
        _entryCap = entryCap;

        // Take initial snapshot
        TakeSnapshot();

        // Start polling timer
        _timer = _timeProvider.CreateTimer(OnPollTimer, null, _interval, _interval);
    }

    private void OnPollTimer(object? state)
    {
        lock (_lock)
        {
            if (_disposed || !EnableRaisingEvents || _isPolling) return;
            _isPolling = true;
        }

        try
        {
            PollChanges();
        }
        catch (Exception ex)
        {
            Error?.Invoke(this, new FolderWatcherErrorEventArgs(ex));
        }
        finally
        {
            lock (_lock)
            {
                _isPolling = false;
            }
        }
    }

    private void PollChanges()
    {
        if (!_fileSystem.DirectoryExists(WatchedPath))
        {
            FolderDeleted?.Invoke(this, EventArgs.Empty);
            return;
        }

        var newSnapshot = new Dictionary<string, (long Size, DateTime Modified, bool IsDir)>(StringComparer.OrdinalIgnoreCase);
        int count = 0;

        try
        {
            foreach (var entryPath in _fileSystem.EnumerateEntries(WatchedPath))
            {
                if (++count > _entryCap) break;

                var info = _fileSystem.GetInfo(entryPath);
                if (info != null && info.Exists)
                {
                    newSnapshot[entryPath] = (info.Size, info.Modified, info.IsDirectory);
                }
            }
        }
        catch (Exception ex)
        {
            Error?.Invoke(this, new FolderWatcherErrorEventArgs(ex));
            return;
        }

        Dictionary<string, (long Size, DateTime Modified, bool IsDir)> oldSnapshot;
        lock (_lock)
        {
            if (_disposed) return;
            oldSnapshot = _snapshot;
            _snapshot = newSnapshot;
        }

        // Detect Added items
        foreach (var kvp in newSnapshot)
        {
            if (!oldSnapshot.ContainsKey(kvp.Key))
            {
                Changed?.Invoke(this, new FolderChangeEventArgs(FolderChangeType.Created, kvp.Key));
            }
            else
            {
                var old = oldSnapshot[kvp.Key];
                if (old.Size != kvp.Value.Size || old.Modified != kvp.Value.Modified)
                {
                    Changed?.Invoke(this, new FolderChangeEventArgs(FolderChangeType.Changed, kvp.Key));
                }
            }
        }

        // Detect Deleted items
        foreach (var kvp in oldSnapshot)
        {
            if (!newSnapshot.ContainsKey(kvp.Key))
            {
                Changed?.Invoke(this, new FolderChangeEventArgs(FolderChangeType.Deleted, kvp.Key));
            }
        }
    }

    private void TakeSnapshot()
    {
        try
        {
            if (!_fileSystem.DirectoryExists(WatchedPath)) return;

            int count = 0;
            foreach (var entryPath in _fileSystem.EnumerateEntries(WatchedPath))
            {
                if (++count > _entryCap) break;

                var info = _fileSystem.GetInfo(entryPath);
                if (info != null && info.Exists)
                {
                    _snapshot[entryPath] = (info.Size, info.Modified, info.IsDirectory);
                }
            }
        }
        catch
        {
            // Ignore initial snapshot errors
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _timer?.Dispose();
            _snapshot.Clear();
        }
    }
}

