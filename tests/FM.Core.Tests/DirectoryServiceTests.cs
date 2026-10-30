using System.Diagnostics;
using FM.Core.Models;
using FM.Core.Services;

namespace FM.Core.Tests;

public class DirectoryServiceTests : IDisposable
{
    private readonly string _testRoot;
    private readonly DirectoryService _service;

    public DirectoryServiceTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "FM_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);
        _service = new DirectoryService();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    [Fact]
    public async Task EmptyDirectory_ReturnsNoItems()
    {
        var items = new List<FileEntry>();
        await foreach (var item in _service.EnumerateAsync(_testRoot))
        {
            items.Add(item);
        }

        Assert.Empty(items);
    }

    [Fact]
    public async Task NonexistentPath_ThrowsDirectoryNotFoundException()
    {
        string fakePath = Path.Combine(_testRoot, "Does_Not_Exist_" + Guid.NewGuid().ToString("N"));

        await Assert.ThrowsAsync<DirectoryNotFoundException>(async () =>
        {
            await foreach (var _ in _service.EnumerateAsync(fakePath))
            {
            }
        });
    }

    [Fact]
    public async Task InaccessibleSubfolder_SkippedWithoutCrash()
    {
        // Create normal file
        string normalFile = Path.Combine(_testRoot, "normal.txt");
        await File.WriteAllTextAsync(normalFile, "Hello World");

        // Create a subfolder and lock it down using icacls on Windows
        string lockedDir = Path.Combine(_testRoot, "LockedFolder");
        Directory.CreateDirectory(lockedDir);

        bool aclApplied = false;
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var psi = new ProcessStartInfo("icacls", $"\"{lockedDir}\" /deny Everyone:(OI)(CI)(F) /Q")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var p = Process.Start(psi);
                p?.WaitForExit();
                aclApplied = true;
            }
            catch
            {
                // If icacls fails, test continues with normal check
            }
        }

        try
        {
            var entries = new List<FileEntry>();
            await foreach (var entry in _service.EnumerateAsync(_testRoot))
            {
                entries.Add(entry);
            }

            // Normal file must always be enumerated successfully
            Assert.Contains(entries, e => e.Name == "normal.txt");
        }
        finally
        {
            if (aclApplied && OperatingSystem.IsWindows())
            {
                try
                {
                    var resetPsi = new ProcessStartInfo("icacls", $"\"{lockedDir}\" /reset /Q")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    using var p = Process.Start(resetPsi);
                    p?.WaitForExit();
                }
                catch
                {
                    // Best effort cleanup
                }
            }
        }
    }

    [Fact]
    public async Task CancellationMidEnumeration_StopsEnumerationAndThrowsCancellation()
    {
        // Create multiple files
        for (int i = 0; i < 50; i++)
        {
            File.WriteAllText(Path.Combine(_testRoot, $"file_{i}.txt"), "content");
        }

        using var cts = new CancellationTokenSource();
        var items = new List<FileEntry>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var item in _service.EnumerateAsync(_testRoot, cts.Token))
            {
                items.Add(item);
                if (items.Count >= 5)
                {
                    cts.Cancel();
                }
            }
        });

        Assert.True(items.Count >= 5);
        Assert.True(items.Count < 50);
    }
}
