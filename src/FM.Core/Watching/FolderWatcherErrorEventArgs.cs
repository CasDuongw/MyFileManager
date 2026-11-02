namespace FM.Core.Watching;

/// <summary>
/// Provides data for watcher error events, including buffer overflows.
/// </summary>
public class FolderWatcherErrorEventArgs : EventArgs
{
    public Exception? Exception { get; }
    public bool IsBufferOverflow { get; }

    public FolderWatcherErrorEventArgs(Exception? exception, bool isBufferOverflow = false)
    {
        Exception = exception;
        IsBufferOverflow = isBufferOverflow;
    }
}

