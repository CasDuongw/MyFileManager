namespace FM.Core.Watching;

/// <summary>
/// Factory for creating folder watchers. Enables replacing physical watchers with test fakes.
/// </summary>
public interface IFolderWatcherFactory
{
    /// <summary>
    /// Creates and returns a folder watcher for the specified directory path.
    /// </summary>
    /// <param name="folderPath">The absolute path of the directory to watch.</param>
    /// <returns>An active <see cref="IFolderWatcher"/>.</returns>
    IFolderWatcher CreateWatcher(string folderPath);
}

