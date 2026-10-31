using FM.Core.FileSystem;
using FM.Core.Operations.Transfer;

namespace FM.Core.Tests;

public class TransferEngineTests
{
    private class MockInteraction : ITransferInteraction
    {
        public Func<ConflictInfo, ConflictDecision>? ConflictHandler { get; set; }
        public Func<FailureInfo, FailureDecision>? FailureHandler { get; set; }

        public ValueTask<ConflictDecision> OnConflictAsync(ConflictInfo info, CancellationToken ct)
        {
            if (ConflictHandler != null) return ValueTask.FromResult(ConflictHandler(info));
            return ValueTask.FromResult(new ConflictDecision(ConflictAction.KeepBoth));
        }

        public ValueTask<FailureDecision> OnFailureAsync(FailureInfo info, CancellationToken ct)
        {
            if (FailureHandler != null) return ValueTask.FromResult(FailureHandler(info));
            return ValueTask.FromResult(new FailureDecision(FailureAction.Skip));
        }
    }

    private class SyncProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;
        public SyncProgress(Action<T> handler) => _handler = handler;
        public void Report(T value) => _handler(value);
    }

    [Fact]
    public async Task Copy_5GBVirtualFile_MonotonicProgressAndZeroDisk()
    {
        var fs = new InMemoryFileSystem();
        long fiveGigabytes = 5L * 1024 * 1024 * 1024;
        fs.CreateVirtualFile(@"C:\source\large.iso", fiveGigabytes);
        fs.CreateDirectory(@"D:\dest");

        var engine = new TransferEngine(fs);
        var progressReports = new List<TransferProgress>();
        var progress = new Progress<TransferProgress>(p => progressReports.Add(p));

        var request = new TransferRequest(
            TransferKind.Copy,
            new[] { @"C:\source\large.iso" },
            @"D:\dest",
            new TransferOptions(BufferSize: 64 * 1024 * 1024, VerifyAfterCopy: false)); // 64 MB chunk for fast in-memory execution

        var result = await engine.RunAsync(request, progress);

        Assert.Equal(TransferState.Completed, result.FinalState);
        Assert.Single(result.Items);
        Assert.Equal(ItemOutcome.Copied, result.Items[0].Outcome);

        var destInfo = fs.GetInfo(@"D:\dest\large.iso");
        Assert.NotNull(destInfo);
        Assert.Equal(fiveGigabytes, destInfo.Size);

        // Verify monotonic progress
        var transferringReports = progressReports.Where(p => p.Phase == TransferPhase.Transferring).ToList();
        Assert.NotEmpty(transferringReports);

        long lastBytes = 0;
        foreach (var p in transferringReports)
        {
            Assert.True(p.OverallBytes >= lastBytes);
            lastBytes = p.OverallBytes;
        }
        Assert.Equal(fiveGigabytes, lastBytes);
    }

    [Fact]
    public async Task Copy_CancelMidFile_DeletesTempFile()
    {
        var fs = new InMemoryFileSystem();
        fs.CreateVirtualFile(@"C:\source\file.bin", 100 * 1024 * 1024);
        fs.CreateDirectory(@"D:\dest");

        using var cts = new CancellationTokenSource();
        var engine = new TransferEngine(fs);

        var progress = new Progress<TransferProgress>(p =>
        {
            if (p.CurrentFileBytes > 10 * 1024 * 1024)
            {
                cts.Cancel();
            }
        });

        var request = new TransferRequest(
            TransferKind.Copy,
            new[] { @"C:\source\file.bin" },
            @"D:\dest",
            new TransferOptions(BufferSize: 1024 * 1024));

        var result = await engine.RunAsync(request, progress, ct: cts.Token);

        Assert.Equal(TransferState.Cancelled, result.FinalState);
        // Ensure .fmpart temp file is deleted
        Assert.False(fs.FileExists(@"D:\dest\file.bin.fmpart"));
        Assert.False(fs.FileExists(@"D:\dest\file.bin"));
    }

    [Fact]
    public async Task Copy_CancelBetweenFiles_ReturnsCancelledWithCompletedItemsAndCommitsJournal()
    {
        var fs = new InMemoryFileSystem();
        fs.CreateVirtualFile(@"C:\source\file1.bin", 100);
        fs.CreateVirtualFile(@"C:\source\file2.bin", 100);
        fs.CreateDirectory(@"D:\dest");
        fs.CreateDirectory(@"C:\journal");

        var journal = new FileUndoJournalStore(fs, @"C:\journal");
        using var cts = new CancellationTokenSource();
        var engine = new TransferEngine(fs, journal: journal);

        var progress = new SyncProgress<TransferProgress>(p =>
        {
            if (p.FilesDone == 1)
            {
                cts.Cancel();
            }
        });

        var request = new TransferRequest(
            TransferKind.Copy,
            new[] { @"C:\source\file1.bin", @"C:\source\file2.bin" },
            @"D:\dest",
            new TransferOptions(BufferSize: 1024));

        var result = await engine.RunAsync(request, progress, ct: cts.Token);

        Assert.Equal(TransferState.Cancelled, result.FinalState);
        Assert.Single(result.Items);
        Assert.Equal(ItemOutcome.Copied, result.Items[0].Outcome);

        // Verify journal was committed, not left pending
        var entries = await journal.LoadEntriesAsync();
        var committed = Assert.Single(entries);
        Assert.Equal(request.OperationId, committed.OperationId);
        Assert.Equal(JournalState.Committed, committed.State);
        Assert.Single(committed.Items);
    }

    [Fact]
    public async Task Move_SameVolume_CancelBetweenFiles_ReturnsCancelledWithCompletedItemsAndCommitsJournal()
    {
        var fs = new InMemoryFileSystem();
        fs.CreateVirtualFile(@"C:\source\file1.bin", 100);
        fs.CreateVirtualFile(@"C:\source\file2.bin", 100);
        fs.CreateDirectory(@"C:\dest");
        fs.CreateDirectory(@"C:\journal");

        var journal = new FileUndoJournalStore(fs, @"C:\journal");
        using var cts = new CancellationTokenSource();
        var engine = new TransferEngine(fs, journal: journal);

        var progress = new SyncProgress<TransferProgress>(p =>
        {
            if (p.FilesDone == 1)
            {
                cts.Cancel();
            }
        });

        var request = new TransferRequest(
            TransferKind.Move,
            new[] { @"C:\source\file1.bin", @"C:\source\file2.bin" },
            @"C:\dest",
            new TransferOptions());

        var result = await engine.RunAsync(request, progress, ct: cts.Token);

        Assert.Equal(TransferState.Cancelled, result.FinalState);
        Assert.Single(result.Items);
        Assert.Equal(ItemOutcome.Moved, result.Items[0].Outcome);

        var entries = await journal.LoadEntriesAsync();
        var committed = Assert.Single(entries);
        Assert.Equal(request.OperationId, committed.OperationId);
        Assert.Equal(JournalState.Committed, committed.State);
        Assert.Single(committed.Items);
    }

    [Fact]
    public async Task Copy_CancelWhilePaused_ReturnsCancelledAndAbandonsJournal()
    {
        var fs = new InMemoryFileSystem();
        fs.CreateVirtualFile(@"C:\source\file1.bin", 100);
        fs.CreateDirectory(@"D:\dest");
        fs.CreateDirectory(@"C:\journal");

        var journal = new FileUndoJournalStore(fs, @"C:\journal");
        using var cts = new CancellationTokenSource();
        var gate = new PauseGate();
        gate.Pause();

        var engine = new TransferEngine(fs, journal: journal);

        var request = new TransferRequest(
            TransferKind.Copy,
            new[] { @"C:\source\file1.bin" },
            @"D:\dest",
            new TransferOptions());

        var runTask = engine.RunAsync(request, gate: gate, ct: cts.Token);
        await Task.Yield();
        cts.Cancel();

        var result = await runTask;
        Assert.Equal(TransferState.Cancelled, result.FinalState);
        Assert.Empty(result.Items);

        // Journal should be abandoned, so no committed entries exist
        var entries = await journal.LoadEntriesAsync();
        Assert.Empty(entries);
    }

    [Fact]
    public async Task Copy_FailureInjection_WithRetry_SucceedsAfterRetry()
    {
        var fs = new InMemoryFileSystem();
        fs.CreateVirtualFile(@"C:\data.bin", 200);
        fs.CreateDirectory(@"D:\backup");

        // Fail at byte 50 once
        fs.InjectFailure(@"C:\data.bin", 50, new IOException("Transient read error"), count: 1);

        var engine = new TransferEngine(fs);
        var request = new TransferRequest(
            TransferKind.Copy,
            new[] { @"C:\data.bin" },
            @"D:\backup",
            new TransferOptions(MaxRetries: 2, BufferSize: 64));

        var result = await engine.RunAsync(request);

        Assert.Equal(TransferState.Completed, result.FinalState);
        Assert.Single(result.Items);
        Assert.Equal(ItemOutcome.Copied, result.Items[0].Outcome);
        Assert.True(fs.FileExists(@"D:\backup\data.bin"));
    }

    [Fact]
    public async Task Copy_Conflict_KeepBoth_RenamesTarget()
    {
        var fs = new InMemoryFileSystem();
        fs.CreateVirtualFile(@"C:\file.txt", 100);
        fs.CreateDirectory(@"D:\dest");
        fs.CreateVirtualFile(@"D:\dest\file.txt", 200);

        var engine = new TransferEngine(fs);
        var request = new TransferRequest(
            TransferKind.Copy,
            new[] { @"C:\file.txt" },
            @"D:\dest",
            new TransferOptions(DefaultConflict: ConflictAction.KeepBoth));

        var result = await engine.RunAsync(request);

        Assert.Equal(TransferState.Completed, result.FinalState);
        Assert.Single(result.Items);
        Assert.Equal(ItemOutcome.KeptBoth, result.Items[0].Outcome);
        Assert.Equal(@"D:\dest\file (2).txt", result.Items[0].DestinationPath);
        Assert.True(fs.FileExists(@"D:\dest\file.txt"));
        Assert.True(fs.FileExists(@"D:\dest\file (2).txt"));
    }

    [Fact]
    public async Task Copy_Conflict_Interaction_ApplyToAll()
    {
        var fs = new InMemoryFileSystem();
        fs.CreateVirtualFile(@"C:\a.txt", 10);
        fs.CreateVirtualFile(@"C:\b.txt", 20);
        fs.CreateDirectory(@"D:\dest");
        fs.CreateVirtualFile(@"D:\dest\a.txt", 99);
        fs.CreateVirtualFile(@"D:\dest\b.txt", 99);

        int promptCount = 0;
        var interaction = new MockInteraction
        {
            ConflictHandler = info =>
            {
                promptCount++;
                return new ConflictDecision(ConflictAction.Overwrite, ApplyToAll: true);
            }
        };

        var engine = new TransferEngine(fs);
        var request = new TransferRequest(
            TransferKind.Copy,
            new[] { @"C:\a.txt", @"C:\b.txt" },
            @"D:\dest",
            new TransferOptions(),
            interaction);

        var result = await engine.RunAsync(request);

        Assert.Equal(TransferState.Completed, result.FinalState);
        Assert.Equal(1, promptCount); // Only prompted once due to ApplyToAll
        Assert.All(result.Items, i => Assert.Equal(ItemOutcome.Replaced, i.Outcome));
    }

    [Fact]
    public async Task Move_SameVolume_UsesAtomicRenameWithoutReadingStreams()
    {
        var fs = new InMemoryFileSystem();
        fs.CreateVirtualFile(@"C:\Source\doc.txt", 500);
        fs.CreateDirectory(@"C:\Destination");

        // Inject failure on OpenRead to guarantee stream is NOT opened for same-volume move
        fs.InjectFailure(@"C:\Source\doc.txt", 0, new InvalidOperationException("Stream should not be opened"));

        var engine = new TransferEngine(fs);
        var request = new TransferRequest(
            TransferKind.Move,
            new[] { @"C:\Source\doc.txt" },
            @"C:\Destination",
            new TransferOptions());

        var result = await engine.RunAsync(request);

        Assert.Equal(TransferState.Completed, result.FinalState);
        Assert.Single(result.Items);
        Assert.Equal(ItemOutcome.Moved, result.Items[0].Outcome);
        Assert.False(fs.FileExists(@"C:\Source\doc.txt"));
        Assert.True(fs.FileExists(@"C:\Destination\doc.txt"));
    }

    [Fact]
    public async Task Move_CrossVolume_DeletesSourceOnlyAfterVerification()
    {
        var fs = new InMemoryFileSystem();
        fs.CreateVirtualFile(@"C:\data.bin", 1024);
        fs.CreateDirectory(@"D:\target");

        var engine = new TransferEngine(fs);
        var request = new TransferRequest(
            TransferKind.Move,
            new[] { @"C:\data.bin" },
            @"D:\target",
            new TransferOptions(VerifyAfterCopy: true));

        var result = await engine.RunAsync(request);

        Assert.Equal(TransferState.Completed, result.FinalState);
        Assert.Single(result.Items);
        Assert.Equal(ItemOutcome.Moved, result.Items[0].Outcome);

        // Source is deleted only after successful verification
        Assert.False(fs.FileExists(@"C:\data.bin"));
        Assert.True(fs.FileExists(@"D:\target\data.bin"));
    }

    [Fact]
    public async Task Move_SameVolume_PauseResumeAndCancel_WorksCorrectly()
    {
        var fs = new InMemoryFileSystem();
        fs.CreateVirtualFile(@"C:\Source\doc1.txt", 100);
        fs.CreateVirtualFile(@"C:\Source\doc2.txt", 100);
        fs.CreateDirectory(@"C:\Destination");

        var engine = new TransferEngine(fs);
        var gate = new PauseGate();
        gate.Pause(); // Pause before starting

        var request = new TransferRequest(
            TransferKind.Move,
            new[] { @"C:\Source\doc1.txt", @"C:\Source\doc2.txt" },
            @"C:\Destination",
            new TransferOptions());

        var runTask = engine.RunAsync(request, gate: gate);
        await Task.Yield();

        // While paused, no files should have been moved yet
        Assert.False(runTask.IsCompleted);
        Assert.False(fs.FileExists(@"C:\Destination\doc1.txt"));

        // Resume -> moves files to completion
        gate.Resume();
        var result = await runTask;

        Assert.Equal(TransferState.Completed, result.FinalState);
        Assert.Equal(2, result.Items.Count);
        Assert.True(fs.FileExists(@"C:\Destination\doc1.txt"));
        Assert.True(fs.FileExists(@"C:\Destination\doc2.txt"));

        // Now test cancellation while paused
        fs.CreateVirtualFile(@"C:\Source\doc3.txt", 100);
        var cts = new CancellationTokenSource();
        gate.Pause();

        var request2 = new TransferRequest(
            TransferKind.Move,
            new[] { @"C:\Source\doc3.txt" },
            @"C:\Destination",
            new TransferOptions());

        var runTask2 = engine.RunAsync(request2, gate: gate, ct: cts.Token);
        await Task.Yield();
        cts.Cancel();

        var result2 = await runTask2;
        Assert.Equal(TransferState.Cancelled, result2.FinalState);
        Assert.Empty(result2.Items);
        Assert.False(fs.FileExists(@"C:\Destination\doc3.txt"));
    }
}

