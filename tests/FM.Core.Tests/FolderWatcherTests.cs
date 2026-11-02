using FM.Core.FileSystem;
using FM.Core.Tests.Fakes;
using FM.Core.Watching;
using Xunit;

namespace FM.Core.Tests;

public class FolderWatcherTests
{
    [Fact]
    public void Coalesce_EmptyEvents_ReturnsEmptyResult()
    {
        var result = FolderChangeCoalescer.Coalesce(Array.Empty<FolderChangeEventArgs>());
        Assert.Empty(result.Added);
        Assert.Empty(result.Removed);
        Assert.Empty(result.Renamed);
        Assert.Empty(result.Modified);
        Assert.False(result.RequiresFullRefresh);
    }

    [Fact]
    public void Coalesce_CreatedThenDeleted_CancelsOut()
    {
        var events = new[]
        {
            new FolderChangeEventArgs(FolderChangeType.Created, @"C:\test\temp.txt"),
            new FolderChangeEventArgs(FolderChangeType.Deleted, @"C:\test\temp.txt")
        };

        var result = FolderChangeCoalescer.Coalesce(events);
        Assert.Empty(result.Added);
        Assert.Empty(result.Removed);
        Assert.Empty(result.Modified);
    }

    [Fact]
    public void Coalesce_CreatedThenModified_RemainsCreatedOnly()
    {
        var events = new[]
        {
            new FolderChangeEventArgs(FolderChangeType.Created, @"C:\test\doc.txt"),
            new FolderChangeEventArgs(FolderChangeType.Changed, @"C:\test\doc.txt"),
            new FolderChangeEventArgs(FolderChangeType.Changed, @"C:\test\doc.txt")
        };

        var result = FolderChangeCoalescer.Coalesce(events);
        Assert.Single(result.Added);
        Assert.Equal(@"C:\test\doc.txt", result.Added[0]);
        Assert.Empty(result.Modified);
    }

    [Fact]
    public void Coalesce_MultipleModifications_DeduplicatedToOne()
    {
        var events = new[]
        {
            new FolderChangeEventArgs(FolderChangeType.Changed, @"C:\test\file.bin"),
            new FolderChangeEventArgs(FolderChangeType.Changed, @"C:\test\file.bin"),
            new FolderChangeEventArgs(FolderChangeType.Changed, @"C:\test\file.bin")
        };

        var result = FolderChangeCoalescer.Coalesce(events);
        Assert.Single(result.Modified);
        Assert.Equal(@"C:\test\file.bin", result.Modified[0]);
    }

    [Fact]
    public void Coalesce_ChainedRenames_CollapsesToNetRename()
    {
        var events = new[]
        {
            new FolderChangeEventArgs(FolderChangeType.Renamed, @"C:\test\b.txt", @"C:\test\a.txt"),
            new FolderChangeEventArgs(FolderChangeType.Renamed, @"C:\test\c.txt", @"C:\test\b.txt")
        };

        var result = FolderChangeCoalescer.Coalesce(events);
        Assert.Single(result.Renamed);
        Assert.Equal(@"C:\test\a.txt", result.Renamed[0].OldPath);
        Assert.Equal(@"C:\test\c.txt", result.Renamed[0].NewPath);
    }

    [Fact]
    public void Coalesce_CreatedThenRenamed_BecomesCreatedWithNewName()
    {
        var events = new[]
        {
            new FolderChangeEventArgs(FolderChangeType.Created, @"C:\test\new.tmp"),
            new FolderChangeEventArgs(FolderChangeType.Renamed, @"C:\test\final.txt", @"C:\test\new.tmp")
        };

        var result = FolderChangeCoalescer.Coalesce(events);
        Assert.Single(result.Added);
        Assert.Equal(@"C:\test\final.txt", result.Added[0]);
        Assert.Empty(result.Renamed);
    }

    [Fact]
    public void Coalesce_BurstExceedingCap_RequiresFullRefresh()
    {
        var events = Enumerable.Range(0, 100)
            .Select(i => new FolderChangeEventArgs(FolderChangeType.Created, $@"C:\test\file_{i}.txt"))
            .ToList();

        var result = FolderChangeCoalescer.Coalesce(events, maxBatchSize: 50);
        Assert.True(result.RequiresFullRefresh);
    }

    [Fact]
    public void FakeFolderWatcher_FiresAllEventsCorrectly()
    {
        var watcher = new FakeFolderWatcher(@"C:\test");
        FolderChangeEventArgs? changeReceived = null;
        FolderWatcherErrorEventArgs? errorReceived = null;
        bool deletedReceived = false;

        watcher.Changed += (_, e) => changeReceived = e;
        watcher.Error += (_, e) => errorReceived = e;
        watcher.FolderDeleted += (_, _) => deletedReceived = true;

        watcher.TriggerCreated(@"C:\test\new.txt");
        Assert.NotNull(changeReceived);
        Assert.Equal(FolderChangeType.Created, changeReceived.ChangeType);
        Assert.Equal(@"C:\test\new.txt", changeReceived.FullPath);

        watcher.TriggerError(new InvalidOperationException("Boom"), isBufferOverflow: true);
        Assert.NotNull(errorReceived);
        Assert.True(errorReceived.IsBufferOverflow);

        watcher.TriggerFolderDeleted();
        Assert.True(deletedReceived);

        watcher.Dispose();
        Assert.True(watcher.IsDisposed);
    }

    [Fact]
    public void PollingFolderWatcher_DetectsChangesViaTimeProvider()
    {
        var memFs = new InMemoryFileSystem();
        memFs.CreateDirectory(@"C:\data");
        memFs.CreateFile(@"C:\data\f1.txt", new byte[] { 1, 2, 3 });

        var timeProvider = new TestTimeProvider();
        using var watcher = new PollingFolderWatcher(@"C:\data", memFs, timeProvider, TimeSpan.FromSeconds(2));

        var detected = new List<FolderChangeEventArgs>();
        watcher.Changed += (_, e) => detected.Add(e);

        // Advance time with no changes
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        Assert.Empty(detected);

        // Add a new file in memory
        memFs.CreateFile(@"C:\data\f2.txt", new byte[] { 4, 5 });
        timeProvider.Advance(TimeSpan.FromSeconds(2));

        Assert.Single(detected);
        Assert.Equal(FolderChangeType.Created, detected[0].ChangeType);
        Assert.Equal(@"C:\data\f2.txt", detected[0].FullPath);

        // Delete the first file
        detected.Clear();
        memFs.DeleteFile(@"C:\data\f1.txt");
        timeProvider.Advance(TimeSpan.FromSeconds(2));

        Assert.Single(detected);
        Assert.Equal(FolderChangeType.Deleted, detected[0].ChangeType);
        Assert.Equal(@"C:\data\f1.txt", detected[0].FullPath);
    }

    [Fact]
    public void PollingFolderWatcher_FolderDeleted_FiresFolderDeletedEvent()
    {
        var memFs = new InMemoryFileSystem();
        memFs.CreateDirectory(@"C:\data");

        var timeProvider = new TestTimeProvider();
        using var watcher = new PollingFolderWatcher(@"C:\data", memFs, timeProvider, TimeSpan.FromSeconds(2));

        bool folderDeletedFired = false;
        watcher.FolderDeleted += (_, _) => folderDeletedFired = true;

        memFs.DeleteDirectory(@"C:\data");
        timeProvider.Advance(TimeSpan.FromSeconds(2));

        Assert.True(folderDeletedFired);
    }
}
