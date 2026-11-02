using FM.Core.FileSystem;

namespace FM.Core.Watching;

/// <summary>
/// Creates folder watchers for the physical operating system.
/// Uses <see cref="PhysicalFolderWatcher"/> for local drives, and automatically falls back
/// to <see cref="PollingFolderWatcher"/> for network UNC shares or paths where physical hooks fail.
/// </summary>
public class PhysicalFolderWatcherFactory : IFolderWatcherFactory
{
    private readonly IFileSystem _fileSystem;
    private readonly TimeProvider _timeProvider;

    public PhysicalFolderWatcherFactory(IFileSystem? fileSystem = null, TimeProvider? timeProvider = null)
    {
        _fileSystem = fileSystem ?? new PhysicalFileSystem();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public IFolderWatcher CreateWatcher(string folderPath)
    {
        ArgumentNullException.ThrowIfNull(folderPath);

        // UNC paths (e.g. \\server\share or \\wsl$\) often do not support physical FileSystemWatcher change notifications
        bool isUnc = folderPath.StartsWith(@"\\", StringComparison.Ordinal) ||
                     folderPath.StartsWith("//", StringComparison.Ordinal);

        if (isUnc)
        {
            return new PollingFolderWatcher(folderPath, _fileSystem, _timeProvider);
        }

        try
        {
            var watcher = new PhysicalFolderWatcher(folderPath);
            return watcher;
        }
        catch
        {
            // Fall back to polling if physical creation failed (e.g. access rights, mapped network drive)
            return new PollingFolderWatcher(folderPath, _fileSystem, _timeProvider);
        }
    }
}

