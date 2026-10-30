using System.IO;
using FM.Core.FileSystem;
using FM.Core.Operations.Transfer;
using FM.Tests.Common;

namespace FM.Core.Tests;

public class JournalRealDiskTests
{
    [Fact]
    public async Task RealDisk_AppendAndReload_MatchesCommitted()
    {
        using var ws = new TestWorkspace("JournalAppendReload");
        var fs = new PhysicalFileSystem();
        string journalPath = Path.Combine(ws.RootPath, "test-journal.jsonl");

        var store = new FileUndoJournalStore(fs, journalPath);
        var opId = Guid.NewGuid();

        await store.AppendPendingEntryAsync(new PersistentUndoEntry(
            opId,
            JournalCodec.CurrentSchemaVersion,
            JournalState.Pending,
            DateTime.UtcNow,
            TransferKind.Copy,
            Array.Empty<JournalItemEntry>(),
            OwnerPane: "Left"));

        await store.AppendItemAsync(opId, new JournalItemEntry(
            TransferKind.Copy,
            @"C:\a.txt",
            @"D:\a.txt",
            42,
            DateTime.UtcNow,
            ItemOutcome.Copied));

        await store.CommitEntryAsync(opId);

        // Reload from disk
        var reloadStore = new FileUndoJournalStore(fs, journalPath);
        var entries = await reloadStore.LoadEntriesAsync();

        var entry = Assert.Single(entries);
        Assert.Equal(opId, entry.OperationId);
        Assert.Equal("Left", entry.OwnerPane);
        Assert.Single(entry.Items);
        Assert.Equal(42, entry.Items[0].Size);
    }

    [Fact]
    public async Task RealDisk_TornTail_RecoversWithoutError()
    {
        using var ws = new TestWorkspace("JournalTornTail");
        var fs = new PhysicalFileSystem();
        string journalPath = Path.Combine(ws.RootPath, "torn-journal.jsonl");

        var store = new FileUndoJournalStore(fs, journalPath);
        var opId = Guid.NewGuid();

        await store.AppendPendingEntryAsync(new PersistentUndoEntry(opId, 1, JournalState.Pending, DateTime.UtcNow, TransferKind.Copy, Array.Empty<JournalItemEntry>()));
        await store.CommitEntryAsync(opId);

        // Intentionally append half of a line to simulate power loss torn write
        await File.AppendAllTextAsync(journalPath, "{\"v\":1,\"t\":\"it");

        var reloadStore = new FileUndoJournalStore(fs, journalPath);
        var entries = await reloadStore.LoadEntriesAsync();

        var entry = Assert.Single(entries);
        Assert.Equal(opId, entry.OperationId);
        Assert.Equal(JournalState.Committed, entry.State);
    }

    [Fact]
    public async Task RealDisk_DegradedMode_FallbackToMemoryOnLock()
    {
        using var ws = new TestWorkspace("JournalLockedFallback");
        var fs = new PhysicalFileSystem();
        string journalPath = Path.Combine(ws.RootPath, "locked-journal.jsonl");

        // Write an initial file
        File.WriteAllText(journalPath, "");

        // Lock file exclusively to simulate another instance holding write lock
        using var exclusiveLock = new FileStream(journalPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        string? warningMessage = null;
        var store = new FileUndoJournalStore(fs, journalPath)
        {
            WarningHandler = msg => warningMessage = msg
        };

        var opId = Guid.NewGuid();
        // Should not crash, degrades gracefully to in-memory store
        await store.AppendPendingEntryAsync(new PersistentUndoEntry(opId, 1, JournalState.Pending, DateTime.UtcNow, TransferKind.Copy, Array.Empty<JournalItemEntry>()));
        await store.CommitEntryAsync(opId);

        var loaded = await store.LoadEntriesAsync();
        Assert.Single(loaded);
        Assert.Equal(opId, loaded[0].OperationId);
        Assert.NotNull(warningMessage);
        Assert.Contains("Undo history won't survive a restart", warningMessage);
    }
}
