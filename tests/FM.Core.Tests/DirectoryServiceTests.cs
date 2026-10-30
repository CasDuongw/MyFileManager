using System.Diagnostics;
using FM.Core.Models;
using FM.Core.Services;
using FM.Tests.Common;

namespace FM.Core.Tests;

/// <summary>
/// Shared fixture creating a single directory structure with &lt;= 50 files total for all DirectoryService read tests.
/// </summary>
public sealed class DirectoryTestFixture : IDisposable
{
    public TestWorkspace Workspace { get; }
    public string EmptyPath { get; }
    public string PopulatedPath { get; }
    public const int PopulatedFileCount = 30;

    public DirectoryTestFixture()
    {
        Workspace = new TestWorkspace("DirSvcFixture");
        EmptyPath = Workspace.CreateDirectory("empty");
        PopulatedPath = Workspace.CreateDirectory("populated");

        for (int i = 0; i < PopulatedFileCount; i++)
        {
            Workspace.CreateTextFile(Path.Combine("populated", $"file_{i:D2}.txt"), "data");
        }
    }

    public void Dispose()
    {
        Workspace.Dispose();
    }
}

public class DirectoryServiceTests : IClassFixture<DirectoryTestFixture>
{
    private readonly DirectoryTestFixture _fixture;
    private readonly DirectoryService _service;

    public DirectoryServiceTests(DirectoryTestFixture fixture)
    {
        _fixture = fixture;
        _service = new DirectoryService();
    }

    [Fact]
    public async Task EmptyDirectory_ReturnsNoItems()
    {
        var items = new List<FileEntry>();
        await foreach (var batch in _service.EnumerateBatchesAsync(_fixture.EmptyPath))
        {
            items.AddRange(batch);
        }

        Assert.Empty(items);
    }

    [Fact]
    public async Task NonexistentPath_ThrowsDirectoryNotFoundException()
    {
        string fakePath = Path.Combine(_fixture.EmptyPath, "Does_Not_Exist_" + Guid.NewGuid().ToString("N"));

        await Assert.ThrowsAsync<DirectoryNotFoundException>(async () =>
        {
            await foreach (var _ in _service.EnumerateBatchesAsync(fakePath))
            {
            }
        });
    }

    [Fact]
    [Trait("Category", "Acl")]
    public async Task InaccessibleSubfolder_SkippedWithoutCrash()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var workspace = new TestWorkspace("AclSubfolder");
        workspace.CreateTextFile("normal.txt", "Hello World");

        string lockedDir = workspace.CreateDirectory("LockedFolder");
        workspace.CreateTextFile(Path.Combine("LockedFolder", "child.txt"), "child data");

        // Deny list/read ONLY for the current user
        var psi = new ProcessStartInfo("icacls", $"\"{lockedDir}\" /deny \"{Environment.UserName}:(RD)\" /Q")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        };
        using (var p = Process.Start(psi))
        {
            p?.WaitForExit();
        }

        try
        {
            var entries = new List<FileEntry>();
            var exception = await Record.ExceptionAsync(async () =>
            {
                await foreach (var batch in _service.EnumerateBatchesAsync(workspace.RootPath))
                {
                    entries.AddRange(batch);
                }
            });

            // DirectoryService guarantees top-level enumeration does not crash when encountering an inaccessible subfolder.
            // None of the inaccessible subfolder's children appear in the parent enumeration.
            Assert.Null(exception);
            Assert.Contains(entries, e => e.Name == "normal.txt");
            Assert.DoesNotContain(entries, e => e.Name == "child.txt");

            // Attempting to enumerate the inaccessible folder directly throws UnauthorizedAccessException
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            {
                await foreach (var _ in _service.EnumerateBatchesAsync(lockedDir))
                {
                }
            });
        }
        finally
        {
            ResetAclWithRetry(lockedDir);
        }
    }

    [Fact]
    [Trait("Category", "Acl")]
    public async Task DeniedRootDirectory_ThrowsUnauthorizedAccessException()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var workspace = new TestWorkspace("AclDeniedRoot");
        string deniedDir = workspace.CreateDirectory("DeniedRootFolder");
        workspace.CreateTextFile(Path.Combine("DeniedRootFolder", "secret.txt"), "secret data");

        // Deny list/read ONLY for the current user
        var psi = new ProcessStartInfo("icacls", $"\"{deniedDir}\" /deny \"{Environment.UserName}:(RD)\" /Q")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        };
        using (var p = Process.Start(psi))
        {
            p?.WaitForExit();
        }

        try
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            {
                await foreach (var _ in _service.EnumerateBatchesAsync(deniedDir))
                {
                }
            });
        }
        finally
        {
            ResetAclWithRetry(deniedDir);
        }
    }

    [Fact]
    public async Task CancellationMidEnumeration_StopsEnumerationAndThrowsCancellation()
    {
        using var cts = new CancellationTokenSource();
        var items = new List<FileEntry>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var batch in _service.EnumerateBatchesAsync(_fixture.PopulatedPath, batchSize: 5, cts.Token))
            {
                items.AddRange(batch);
                if (items.Count >= 5)
                {
                    cts.Cancel();
                }
            }
        });

        Assert.True(items.Count >= 5);
        Assert.True(items.Count < DirectoryTestFixture.PopulatedFileCount);
    }

    private class StrictSyncContext : SynchronizationContext
    {
        public int PostedCallbacks { get; private set; }

        public override void Post(SendOrPostCallback d, object? state)
        {
            PostedCallbacks++;
            d(state);
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            PostedCallbacks++;
            d(state);
        }
    }

    [Fact]
    public async Task EnumerateBatchesAsync_DoesNotRunEnumerationWorkOnCallerSynchronizationContext()
    {
        var customContext = new StrictSyncContext();
        var originalContext = SynchronizationContext.Current;

        try
        {
            SynchronizationContext.SetSynchronizationContext(customContext);

            var batches = new List<IReadOnlyList<FileEntry>>();
            await foreach (var batch in _service.EnumerateBatchesAsync(_fixture.PopulatedPath, batchSize: 5).ConfigureAwait(false))
            {
                batches.Add(batch);
            }

            Assert.NotEmpty(batches);
            Assert.Equal(DirectoryTestFixture.PopulatedFileCount, batches.Sum(b => b.Count));
            Assert.Equal(0, customContext.PostedCallbacks);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    private static void ResetAclWithRetry(string path)
    {
        if (!OperatingSystem.IsWindows()) return;

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var resetPsi = new ProcessStartInfo("icacls", $"\"{path}\" /reset /Q")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var resetP = Process.Start(resetPsi);
                resetP?.WaitForExit();
                if (resetP?.ExitCode == 0) return;
            }
            catch
            {
                // Retry after brief delay
            }
            Thread.Sleep(50 * attempt);
        }
    }
}
