namespace FM.Core.Logging;

/// <summary>
/// Abstraction for logging crashes and unhandled exceptions safely and boundedly.
/// </summary>
public interface ICrashLogger
{
    string LogDirectory { get; }

    Task LogExceptionAsync(Exception ex, string context, CancellationToken ct = default);

    void LogExceptionSync(Exception ex, string context);

    Task PurgeOldLogsAsync(int maxRetainedFiles = 5, CancellationToken ct = default);
}

