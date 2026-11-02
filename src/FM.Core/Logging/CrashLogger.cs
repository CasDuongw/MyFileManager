using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using FM.Core.FileSystem;

namespace FM.Core.Logging;

/// <summary>
/// Production crash and unhandled exception logger.
/// Bounded to 5 MB per file with automatic rotation and retention cap.
/// Sanitizes passwords, tokens, and authorization secrets.
/// Completely testable against InMemoryFileSystem.
/// </summary>
public sealed class CrashLogger : ICrashLogger
{
    private readonly IFileSystem _fileSystem;
    private readonly string _logDirectory;
    private readonly long _maxFileSizeBytes;
    private readonly object _syncLock = new();

    private static readonly Regex SecretSanitizerRegex = new(
        @"(?i)(password|passwd|pwd|token|bearer|apikey|api_key|secret|authorization)\s*[:=]\s*([^\s;&""']+)",
        RegexOptions.Compiled);

    public string LogDirectory => _logDirectory;

    public CrashLogger(
        IFileSystem? fileSystem = null,
        string? logDirectory = null,
        long maxFileSizeBytes = 5 * 1024 * 1024) // 5 MB default
    {
        _fileSystem = fileSystem ?? new PhysicalFileSystem();
        _maxFileSizeBytes = maxFileSizeBytes;

        if (string.IsNullOrWhiteSpace(logDirectory))
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _logDirectory = Path.Combine(localAppData, "MyFileManager", "logs");
        }
        else
        {
            _logDirectory = logDirectory;
        }

        try
        {
            if (!_fileSystem.DirectoryExists(_logDirectory))
            {
                _fileSystem.CreateDirectory(_logDirectory);
            }
        }
        catch
        {
            // Non-fatal if folder creation is delayed
        }
    }

    public Task LogExceptionAsync(Exception ex, string context, CancellationToken ct = default)
    {
        return Task.Run(() => LogExceptionSync(ex, context), ct);
    }

    public void LogExceptionSync(Exception ex, string context)
    {
        lock (_syncLock)
        {
            try
            {
                if (!_fileSystem.DirectoryExists(_logDirectory))
                {
                    _fileSystem.CreateDirectory(_logDirectory);
                }

                string logFilePath = GetCurrentLogFilePath();

                var sb = new StringBuilder();
                sb.AppendLine("================================================================================");
                sb.AppendLine($"Timestamp : {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC");
                sb.AppendLine($"Context   : {Sanitize(context)}");
                sb.AppendLine($"Thread ID : {Environment.CurrentManagedThreadId}");
                sb.AppendLine($"OS        : {Environment.OSVersion}");
                sb.AppendLine($"Exception : {ex.GetType().FullName}");
                sb.AppendLine($"Message   : {Sanitize(ex.Message)}");
                sb.AppendLine("Stack Trace:");
                sb.AppendLine(Sanitize(ex.StackTrace ?? "No stack trace available."));

                if (ex.InnerException != null)
                {
                    sb.AppendLine("--- Inner Exception ---");
                    sb.AppendLine($"Type    : {ex.InnerException.GetType().FullName}");
                    sb.AppendLine($"Message : {Sanitize(ex.InnerException.Message)}");
                    sb.AppendLine(Sanitize(ex.InnerException.StackTrace ?? ""));
                }
                sb.AppendLine();

                byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());

                // Append bytes to file via FileSystem
                using (var stream = _fileSystem.OpenAppend(logFilePath))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush();
                }
            }
            catch
            {
                // Never rethrow during emergency logging
            }
        }
    }

    public Task PurgeOldLogsAsync(int maxRetainedFiles = 5, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            lock (_syncLock)
            {
                try
                {
                    if (!_fileSystem.DirectoryExists(_logDirectory)) return;

                    var files = _fileSystem.EnumerateEntries(_logDirectory)
                        .Where(f => !_fileSystem.DirectoryExists(f))
                        .Where(f =>
                        {
                            string name = Path.GetFileName(f);
                            return name.StartsWith("crash_", StringComparison.OrdinalIgnoreCase) &&
                                   name.EndsWith(".log", StringComparison.OrdinalIgnoreCase);
                        })
                        .OrderBy(f => f)
                        .ToList();

                    if (files.Count > maxRetainedFiles)
                    {
                        int toRemove = files.Count - maxRetainedFiles;
                        for (int i = 0; i < toRemove; i++)
                        {
                            try { _fileSystem.DeleteFile(files[i]); } catch { }
                        }
                    }
                }
                catch
                {
                    // Non-fatal
                }
            }
        }, ct);
    }

    private string GetCurrentLogFilePath()
    {
        string dateStr = DateTime.UtcNow.ToString("yyyyMMdd");
        string baseName = $"crash_{dateStr}.log";
        string fullPath = Path.Combine(_logDirectory, baseName);

        // Check if file size exceeds bounded limit
        if (_fileSystem.FileExists(fullPath))
        {
            try
            {
                using var s = _fileSystem.OpenRead(fullPath);
                if (s.Length >= _maxFileSizeBytes)
                {
                    // Rotate to crash_YYYYMMDD_1.log, etc.
                    int index = 1;
                    while (true)
                    {
                        string candidate = Path.Combine(_logDirectory, $"crash_{dateStr}_{index}.log");
                        if (!_fileSystem.FileExists(candidate))
                        {
                            fullPath = candidate;
                            break;
                        }
                        using var cs = _fileSystem.OpenRead(candidate);
                        if (cs.Length < _maxFileSizeBytes)
                        {
                            fullPath = candidate;
                            break;
                        }
                        index++;
                    }
                }
            }
            catch { }
        }

        return fullPath;
    }

    private static string Sanitize(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        return SecretSanitizerRegex.Replace(input, "$1=[REDACTED]");
    }
}
