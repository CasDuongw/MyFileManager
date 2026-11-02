using System.IO;
using System.Text;
using FM.Core.FileSystem;
using FM.Core.Logging;

namespace FM.Core.Tests;

public class CrashLoggerTests
{
    private readonly InMemoryFileSystem _fs;
    private readonly string _logDir = @"C:\AppData\Logs";
    private readonly CrashLogger _logger;

    public CrashLoggerTests()
    {
        _fs = new InMemoryFileSystem();
        _logger = new CrashLogger(_fs, _logDir, maxFileSizeBytes: 1024); // 1 KB cap for testing rotation
    }

    [Fact]
    public void LogExceptionSync_WritesFormattedExceptionToLogFile()
    {
        var ex = new InvalidOperationException("Test operation failure");

        _logger.LogExceptionSync(ex, "Unit Test Context");

        var files = _fs.EnumerateEntries(_logDir).Where(f => Path.GetFileName(f).StartsWith("crash_")).ToList();
        Assert.Single(files);

        string content;
        using (var stream = _fs.OpenRead(files[0]))
        using (var reader = new StreamReader(stream, Encoding.UTF8))
        {
            content = reader.ReadToEnd();
        }

        Assert.Contains("Test operation failure", content);
        Assert.Contains("System.InvalidOperationException", content);
        Assert.Contains("Unit Test Context", content);
    }

    [Fact]
    public void LogException_SanitizesSensitiveInformation()
    {
        var ex = new Exception("Connection string: server=localhost;password=MySuperSecretPassword;token=secret_token_123;");

        _logger.LogExceptionSync(ex, "DB Connection");

        var files = _fs.EnumerateEntries(_logDir).Where(f => Path.GetFileName(f).StartsWith("crash_")).ToList();
        var logFile = Assert.Single(files);

        string content;
        using (var stream = _fs.OpenRead(logFile))
        using (var reader = new StreamReader(stream, Encoding.UTF8))
        {
            content = reader.ReadToEnd();
        }

        Assert.DoesNotContain("MySuperSecretPassword", content);
        Assert.DoesNotContain("secret_token_123", content);
        Assert.Contains("[REDACTED]", content);
    }

    [Fact]
    public void LogException_RotatesWhenSizeExceedsCap()
    {
        // Log multiple exceptions so size exceeds 1024 bytes
        for (int i = 0; i < 6; i++)
        {
            _logger.LogExceptionSync(new Exception($"Iteration {i} error with some padding stack information: {new string('x', 200)}"), "Rotation test");
        }

        var files = _fs.EnumerateEntries(_logDir).Where(f => Path.GetFileName(f).StartsWith("crash_")).ToList();
        Assert.True(files.Count >= 2, $"Expected at least 2 rotated log files, but found {files.Count}");
    }

    [Fact]
    public async Task PurgeOldLogsAsync_RetainsOnlySpecifiedCount()
    {
        _fs.CreateVirtualFile(Path.Combine(_logDir, "crash_20261001.log"), 100);
        _fs.CreateVirtualFile(Path.Combine(_logDir, "crash_20261002.log"), 100);
        _fs.CreateVirtualFile(Path.Combine(_logDir, "crash_20261003.log"), 100);
        _fs.CreateVirtualFile(Path.Combine(_logDir, "crash_20261004.log"), 100);
        _fs.CreateVirtualFile(Path.Combine(_logDir, "crash_20261005.log"), 100);

        Assert.Equal(5, _fs.EnumerateEntries(_logDir).Where(f => Path.GetFileName(f).StartsWith("crash_")).Count());

        await _logger.PurgeOldLogsAsync(maxRetainedFiles: 2);

        var remaining = _fs.EnumerateEntries(_logDir).Where(f => Path.GetFileName(f).StartsWith("crash_")).ToList();
        Assert.Equal(2, remaining.Count);
        Assert.Contains(remaining, f => f.EndsWith("crash_20261004.log"));
        Assert.Contains(remaining, f => f.EndsWith("crash_20261005.log"));
    }
}
