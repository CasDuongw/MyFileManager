using System.IO;
using FM.Core.FileSystem;
using FM.Core.Operations.Transfer;

namespace FM.Core.Tests;

public class UndoJournalTests
{
    private readonly InMemoryFileSystem _fs;
    private readonly string _journalPath = @"C:\AppData\journal\undo-journal.jsonl";
    private readonly FileUndoJournalStore _store;

    public UndoJournalTests()
    {
        _fs = new InMemoryFileSystem();
        _fs.CreateDirectory(@"C:\AppData\journal");
        _store = new FileUndoJournalStore(_fs, _journalPath);
    }

    [Fact]
    public async Task AppendAndLoad_PersistsCommittedOperation()
    {
        var opId = Guid.NewGuid();
        var entry = new PersistentUndoEntry(
            opId,
            JournalCodec.CurrentSchemaVersion,
            JournalState.Pending,
            DateTime.UtcNow,
            TransferKind.Copy,
            Array.Empty<JournalItemEntry>(),
            OwnerPane: "Left");

        await _store.AppendPendingEntryAsync(entry);

        var item = new JournalItemEntry(
            TransferKind.Copy,
            @"C:\src.txt",
            @"D:\dst.txt",
            100,
            DateTime.UtcNow,
            ItemOutcome.Copied,
            IsDirectory: false);

        await _store.AppendItemAsync(opId, item);
        await _store.CommitEntryAsync(opId);

        var loaded = await _store.LoadEntriesAsync();
        var committed = Assert.Single(loaded);
        Assert.Equal(opId, committed.OperationId);
        Assert.Equal("Left", committed.OwnerPane);
        Assert.Single(committed.Items);
    }

    [Fact]
    public async Task ConcurrentAppends_TwoJobs_WriteCleanlyWithoutInterleaving()
    {
        var op1 = Guid.NewGuid();
        var op2 = Guid.NewGuid();

        await _store.AppendPendingEntryAsync(new PersistentUndoEntry(op1, 1, JournalState.Pending, DateTime.UtcNow, TransferKind.Copy, Array.Empty<JournalItemEntry>()));
        await _store.AppendPendingEntryAsync(new PersistentUndoEntry(op2, 1, JournalState.Pending, DateTime.UtcNow, TransferKind.Move, Array.Empty<JournalItemEntry>()));

        // Concurrently append 50 items each
        var task1 = Task.Run(async () =>
        {
            for (int i = 0; i < 50; i++)
            {
                await _store.AppendItemAsync(op1, new JournalItemEntry(TransferKind.Copy, $@"C:\f1_{i}.txt", $@"D:\f1_{i}.txt", i, null, ItemOutcome.Copied));
            }
            await _store.CommitEntryAsync(op1);
        });

        var task2 = Task.Run(async () =>
        {
            for (int i = 0; i < 50; i++)
            {
                await _store.AppendItemAsync(op2, new JournalItemEntry(TransferKind.Move, $@"C:\f2_{i}.txt", $@"D:\f2_{i}.txt", i, null, ItemOutcome.Moved));
            }
            await _store.CommitEntryAsync(op2);
        });

        await Task.WhenAll(task1, task2);

        var loaded = await _store.LoadEntriesAsync();
        Assert.Equal(2, loaded.Count);

        var entry1 = loaded.First(e => e.OperationId == op1);
        Assert.Equal(50, entry1.Items.Count);

        var entry2 = loaded.First(e => e.OperationId == op2);
        Assert.Equal(50, entry2.Items.Count);
    }

    [Fact]
    public async Task CrashRecovery_DestMissing_RestoresBackup()
    {
        var opId = Guid.NewGuid();
        string stagingRoot = @"C:\staging";
        _fs.CreateDirectory(stagingRoot);
        var stager = new StagingBackupStore(_fs, stagingRoot);

        // Simulated crash state: Pending entry, backup exists in staging, dest is missing!
        string backupFile = Path.Combine(stagingRoot, opId.ToString("D"), "0_file.txt");
        _fs.CreateVirtualFile(backupFile, 250);

        await _store.AppendPendingEntryAsync(new PersistentUndoEntry(opId, 1, JournalState.Pending, DateTime.UtcNow, TransferKind.Copy, Array.Empty<JournalItemEntry>()));
        await _store.AppendItemAsync(opId, new JournalItemEntry(
            TransferKind.Copy,
            @"C:\Source\file.txt",
            @"D:\Dest\file.txt",
            250,
            DateTime.UtcNow,
            ItemOutcome.Replaced,
            IsDirectory: false,
            BackupPath: backupFile));

        Assert.False(_fs.FileExists(@"D:\Dest\file.txt")); // Missing destination before recovery

        // Run recovery
        int recovered = await _store.RecoverPendingEntriesAsync(_fs, stager);

        Assert.Equal(1, recovered);
        Assert.True(_fs.FileExists(@"D:\Dest\file.txt")); // Restored!
        Assert.Equal(250, _fs.GetInfo(@"D:\Dest\file.txt")!.Size);

        // Entry should now be committed in journal
        var loaded = await _store.LoadEntriesAsync();
        Assert.Single(loaded);
        Assert.Equal(JournalState.Committed, loaded[0].State);
    }

    [Fact]
    public async Task CrashRecovery_DestExists_KeepsBackupAndCommits()
    {
        var opId = Guid.NewGuid();
        string stagingRoot = @"C:\staging";
        _fs.CreateDirectory(stagingRoot);
        var stager = new StagingBackupStore(_fs, stagingRoot);

        string backupFile = Path.Combine(stagingRoot, opId.ToString("D"), "0_file.txt");
        _fs.CreateVirtualFile(backupFile, 250);
        _fs.CreateVirtualFile(@"D:\Dest\file.txt", 100); // Destination already exists

        await _store.AppendPendingEntryAsync(new PersistentUndoEntry(opId, 1, JournalState.Pending, DateTime.UtcNow, TransferKind.Copy, Array.Empty<JournalItemEntry>()));
        await _store.AppendItemAsync(opId, new JournalItemEntry(
            TransferKind.Copy,
            @"C:\Source\file.txt",
            @"D:\Dest\file.txt",
            100,
            DateTime.UtcNow,
            ItemOutcome.Replaced,
            IsDirectory: false,
            BackupPath: backupFile));

        int recovered = await _store.RecoverPendingEntriesAsync(_fs, stager);

        Assert.Equal(1, recovered);
        Assert.True(_fs.FileExists(backupFile)); // Staging backup preserved for Undo
        Assert.Equal(100, _fs.GetInfo(@"D:\Dest\file.txt")!.Size);
    }

    [Fact]
    public async Task CrashRecovery_NoUndoableItems_AbandonsEntry()
    {
        var opId = Guid.NewGuid();
        await _store.AppendPendingEntryAsync(new PersistentUndoEntry(opId, 1, JournalState.Pending, DateTime.UtcNow, TransferKind.Copy, Array.Empty<JournalItemEntry>()));
        await _store.AppendItemAsync(opId, new JournalItemEntry(
            TransferKind.Copy,
            @"C:\Source\file.txt",
            @"D:\Dest\file.txt",
            100,
            DateTime.UtcNow,
            ItemOutcome.Failed, // Failed only!
            IsDirectory: false));

        int recovered = await _store.RecoverPendingEntriesAsync(_fs, null);

        Assert.Equal(0, recovered);

        // Should not be in loaded committed entries
        var loaded = await _store.LoadEntriesAsync();
        Assert.Empty(loaded);
    }

    [Fact]
    public async Task CompactAsync_RewritesJournal_DropsUndoneAndAgedEntries()
    {
        var op1 = Guid.NewGuid();
        var op2 = Guid.NewGuid();

        await _store.AppendPendingEntryAsync(new PersistentUndoEntry(op1, 1, JournalState.Pending, DateTime.UtcNow.AddDays(-10), TransferKind.Copy, Array.Empty<JournalItemEntry>()));
        await _store.CommitEntryAsync(op1);

        await _store.AppendPendingEntryAsync(new PersistentUndoEntry(op2, 1, JournalState.Pending, DateTime.UtcNow.AddMinutes(-5), TransferKind.Copy, Array.Empty<JournalItemEntry>()));
        await _store.CommitEntryAsync(op2);

        await _store.CompactAsync(TimeSpan.FromDays(7), maxEntries: 50);

        var loaded = await _store.LoadEntriesAsync();
        var remaining = Assert.Single(loaded);
        Assert.Equal(op2, remaining.OperationId);
    }

    [Fact]
    public async Task TransferEngine_WithJournal_WritesFullLifecycle()
    {
        _fs.CreateDirectory(@"C:\Src");
        _fs.CreateDirectory(@"D:\Dst");
        _fs.CreateVirtualFile(@"C:\Src\data.bin", 50);

        var engine = new TransferEngine(_fs, stager: null, journal: _store);
        var request = new TransferRequest(
            TransferKind.Copy,
            new[] { @"C:\Src\data.bin" },
            @"D:\Dst",
            new TransferOptions(DefaultConflict: ConflictAction.KeepBoth));

        var result = await engine.RunAsync(request);
        Assert.Equal(TransferState.Completed, result.FinalState);

        var loaded = await _store.LoadEntriesAsync();
        var op = Assert.Single(loaded);
        Assert.Equal(request.OperationId, op.OperationId);
        Assert.Equal(JournalState.Committed, op.State);
        var item = Assert.Single(op.Items);
        Assert.Equal(@"C:\Src\data.bin", item.SourcePath);
    }
}
