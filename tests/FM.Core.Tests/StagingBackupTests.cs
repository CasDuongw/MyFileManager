using System.IO;
using FM.Core.FileSystem;
using FM.Core.Operations.Transfer;

namespace FM.Core.Tests;

public class StagingBackupTests
{
    private readonly InMemoryFileSystem _fs;
    private readonly StagingBackupStore _stager;
    private readonly string _stagingRoot = @"C:\Users\User\AppData\Local\MyFileManager\staging";

    public StagingBackupTests()
    {
        _fs = new InMemoryFileSystem();
        _fs.CreateDirectory(@"C:\Source");
        _fs.CreateDirectory(@"D:\Dest");
        _fs.CreateDirectory(_stagingRoot);
        _stager = new StagingBackupStore(_fs, _stagingRoot);
    }

    [Fact]
    public async Task OverwriteCopy_ThenUndo_RestoresOriginalContentAndModifiedTime()
    {
        // Source file
        _fs.CreateVirtualFile(@"C:\Source\doc.txt", length: 100);
        var srcDate = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        _fs.SetModified(@"C:\Source\doc.txt", srcDate);

        // Existing destination file
        _fs.CreateVirtualFile(@"D:\Dest\doc.txt", length: 500);
        var origDestDate = new DateTime(2025, 12, 1, 8, 0, 0, DateTimeKind.Utc);
        _fs.SetModified(@"D:\Dest\doc.txt", origDestDate);

        var engine = new TransferEngine(_fs, _stager);
        var request = new TransferRequest(
            TransferKind.Copy,
            new[] { @"C:\Source\doc.txt" },
            @"D:\Dest",
            new TransferOptions(DefaultConflict: ConflictAction.Overwrite));

        var result = await engine.RunAsync(request);

        Assert.Equal(TransferState.Completed, result.FinalState);
        var item = Assert.Single(result.Items);
        Assert.Equal(ItemOutcome.Replaced, item.Outcome);
        Assert.NotNull(item.BackupPath);
        Assert.True(_fs.FileExists(item.BackupPath!));

        // Destination was replaced
        var currentDest = _fs.GetInfo(@"D:\Dest\doc.txt");
        Assert.NotNull(currentDest);
        Assert.Equal(100, currentDest.Size);

        // Now Undo
        var undoOp = new TransferUndoOperation(_fs, TransferKind.Copy, result, _stager, request.OperationId);
        Assert.True(undoOp.CanUndo);

        var undoResult = await undoOp.UndoAsync();
        Assert.True(undoResult.Success, undoResult.ErrorMessage);

        // Destination has restored original file
        var restoredDest = _fs.GetInfo(@"D:\Dest\doc.txt");
        Assert.NotNull(restoredDest);
        Assert.Equal(500, restoredDest.Size);
        Assert.Equal(origDestDate, restoredDest.Modified);
    }

    [Fact]
    public async Task OverwriteMove_ThenUndo_RestoresSourceAndDestination()
    {
        _fs.CreateVirtualFile(@"C:\Source\doc.txt", length: 150);
        var srcDate = new DateTime(2026, 2, 1, 12, 0, 0, DateTimeKind.Utc);
        _fs.SetModified(@"C:\Source\doc.txt", srcDate);

        _fs.CreateVirtualFile(@"D:\Dest\doc.txt", length: 600);
        var origDestDate = new DateTime(2025, 11, 1, 9, 0, 0, DateTimeKind.Utc);
        _fs.SetModified(@"D:\Dest\doc.txt", origDestDate);

        var engine = new TransferEngine(_fs, _stager);
        var request = new TransferRequest(
            TransferKind.Move,
            new[] { @"C:\Source\doc.txt" },
            @"D:\Dest",
            new TransferOptions(DefaultConflict: ConflictAction.Overwrite));

        var result = await engine.RunAsync(request);

        Assert.Equal(TransferState.Completed, result.FinalState);
        Assert.False(_fs.FileExists(@"C:\Source\doc.txt")); // Source moved
        Assert.Equal(150, _fs.GetInfo(@"D:\Dest\doc.txt")!.Size); // Destination replaced

        // Undo move
        var undoOp = new TransferUndoOperation(_fs, TransferKind.Move, result, _stager, request.OperationId);
        Assert.True(undoOp.CanUndo);

        var undoResult = await undoOp.UndoAsync();
        Assert.True(undoResult.Success, undoResult.ErrorMessage);

        // Source is back
        Assert.True(_fs.FileExists(@"C:\Source\doc.txt"));
        Assert.Equal(150, _fs.GetInfo(@"C:\Source\doc.txt")!.Size);

        // Destination original is restored
        Assert.True(_fs.FileExists(@"D:\Dest\doc.txt"));
        Assert.Equal(600, _fs.GetInfo(@"D:\Dest\doc.txt")!.Size);
    }

    [Fact]
    public async Task SameVolumeMove_Replaced_StagesAndUndoesCleanly()
    {
        _fs.CreateDirectory(@"C:\Dest");
        _fs.CreateVirtualFile(@"C:\Source\doc.txt", length: 200);
        _fs.CreateVirtualFile(@"C:\Dest\doc.txt", length: 800);

        var engine = new TransferEngine(_fs, _stager);
        var request = new TransferRequest(
            TransferKind.Move,
            new[] { @"C:\Source\doc.txt" },
            @"C:\Dest",
            new TransferOptions(DefaultConflict: ConflictAction.Overwrite));

        var result = await engine.RunAsync(request);
        Assert.Equal(TransferState.Completed, result.FinalState);
        Assert.Equal(200, _fs.GetInfo(@"C:\Dest\doc.txt")!.Size);

        var undoOp = new TransferUndoOperation(_fs, TransferKind.Move, result, _stager, request.OperationId);
        Assert.True(undoOp.CanUndo);

        var undoResult = await undoOp.UndoAsync();
        Assert.True(undoResult.Success, undoResult.ErrorMessage);

        Assert.True(_fs.FileExists(@"C:\Source\doc.txt"));
        Assert.Equal(200, _fs.GetInfo(@"C:\Source\doc.txt")!.Size);
        Assert.True(_fs.FileExists(@"C:\Dest\doc.txt"));
        Assert.Equal(800, _fs.GetInfo(@"C:\Dest\doc.txt")!.Size);
    }

    [Fact]
    public async Task DestinationModifiedAfterCopy_UndoDoesNotOverwrite()
    {
        _fs.CreateVirtualFile(@"C:\Source\doc.txt", length: 100);
        _fs.CreateVirtualFile(@"D:\Dest\doc.txt", length: 500);

        var engine = new TransferEngine(_fs, _stager);
        var request = new TransferRequest(
            TransferKind.Copy,
            new[] { @"C:\Source\doc.txt" },
            @"D:\Dest",
            new TransferOptions(DefaultConflict: ConflictAction.Overwrite));

        var result = await engine.RunAsync(request);
        Assert.Equal(TransferState.Completed, result.FinalState);

        // External modification to destination file
        _fs.CreateVirtualFile(@"D:\Dest\doc.txt", length: 999);
        _fs.SetModified(@"D:\Dest\doc.txt", DateTime.UtcNow.AddMinutes(10));

        var undoOp = new TransferUndoOperation(_fs, TransferKind.Copy, result, _stager, request.OperationId);
        var undoResult = await undoOp.UndoAsync();

        // Should refuse to overwrite modified file
        Assert.False(undoResult.Success);
        Assert.Contains("modified after copy", undoResult.ErrorMessage);
        Assert.Equal(999, _fs.GetInfo(@"D:\Dest\doc.txt")!.Size); // Preserved
    }

    [Fact]
    public async Task StagingFailure_FailsItem_DestinationRemainsIntact()
    {
        _fs.CreateVirtualFile(@"C:\Source\doc.txt", length: 100);
        _fs.CreateVirtualFile(@"D:\Dest\doc.txt", length: 500);

        // Block same-volume staging to test cross-volume staging failure
        _fs.CreateVirtualFile(@"D:\.fm-staging", 0);
        _fs.CreateVirtualFile(@"D:\Dest\.fm-staging", 0);

        // Inject failure on staging read/write
        _fs.InjectFailure(@"D:\Dest\doc.txt", 0, new IOException("Locked file"), count: 5);

        var engine = new TransferEngine(_fs, _stager);
        var request = new TransferRequest(
            TransferKind.Copy,
            new[] { @"C:\Source\doc.txt" },
            @"D:\Dest",
            new TransferOptions(DefaultConflict: ConflictAction.Overwrite));

        var result = await engine.RunAsync(request);

        Assert.Equal(TransferState.CompletedWithErrors, result.FinalState);
        var item = Assert.Single(result.Items);
        Assert.Equal(ItemOutcome.Failed, item.Outcome);

        // Destination was never overwritten or deleted
        var destInfo = _fs.GetInfo(@"D:\Dest\doc.txt");
        Assert.NotNull(destInfo);
        Assert.Equal(500, destInfo.Size);
    }

    [Fact]
    public async Task SelfOverwrite_IsSkippedSafely()
    {
        _fs.CreateVirtualFile(@"C:\Source\doc.txt", length: 100);

        var engine = new TransferEngine(_fs, _stager);
        var request = new TransferRequest(
            TransferKind.Move,
            new[] { @"C:\Source\doc.txt" },
            @"C:\Source",
            new TransferOptions(DefaultConflict: ConflictAction.Overwrite));

        var result = await engine.RunAsync(request);

        Assert.Equal(TransferState.Completed, result.FinalState);
        var item = Assert.Single(result.Items);
        Assert.Equal(ItemOutcome.Skipped, item.Outcome);
        Assert.Equal(100, _fs.GetInfo(@"C:\Source\doc.txt")!.Size);
    }

    [Fact]
    public async Task BackupPurged_UndoFailsCleanly()
    {
        _fs.CreateVirtualFile(@"C:\Source\doc.txt", length: 100);
        _fs.CreateVirtualFile(@"D:\Dest\doc.txt", length: 500);

        var engine = new TransferEngine(_fs, _stager);
        var request = new TransferRequest(
            TransferKind.Copy,
            new[] { @"C:\Source\doc.txt" },
            @"D:\Dest",
            new TransferOptions(DefaultConflict: ConflictAction.Overwrite));

        var result = await engine.RunAsync(request);
        var item = Assert.Single(result.Items);

        // Purge staging folder manually
        _fs.Delete(item.BackupPath!);

        var undoOp = new TransferUndoOperation(_fs, TransferKind.Copy, result, _stager, request.OperationId);
        var undoResult = await undoOp.UndoAsync();

        Assert.False(undoResult.Success);
        Assert.Contains("missing or was purged", undoResult.ErrorMessage);
    }

    [Fact]
    public async Task StagerNull_MaintainsV03Behavior_ReplacedIsNonUndoable()
    {
        _fs.CreateVirtualFile(@"C:\Source\doc.txt", length: 100);
        _fs.CreateVirtualFile(@"D:\Dest\doc.txt", length: 500);

        // No stager passed
        var engine = new TransferEngine(_fs, stager: null);
        var request = new TransferRequest(
            TransferKind.Copy,
            new[] { @"C:\Source\doc.txt" },
            @"D:\Dest",
            new TransferOptions(DefaultConflict: ConflictAction.Overwrite));

        var result = await engine.RunAsync(request);
        Assert.Equal(TransferState.Completed, result.FinalState);

        var undoOp = new TransferUndoOperation(_fs, TransferKind.Copy, result, stager: null, request.OperationId);
        Assert.False(undoOp.CanUndo);
        Assert.Contains("Overwritten files cannot be restored without backup staging", undoOp.NonUndoableReason);
    }

    [Fact]
    public async Task Purge_DeletesOldOperations_EnforcesQuota()
    {
        var op1 = Guid.NewGuid();
        var op2 = Guid.NewGuid();

        string file1 = Path.Combine(_stagingRoot, op1.ToString("D"), "file1.txt");
        _fs.CreateVirtualFile(file1, length: 1000);
        _fs.SetModified(file1, DateTime.UtcNow.AddDays(-10));
        _fs.SetModified(Path.Combine(_stagingRoot, op1.ToString("D")), DateTime.UtcNow.AddDays(-10));

        string file2 = Path.Combine(_stagingRoot, op2.ToString("D"), "file2.txt");
        _fs.CreateVirtualFile(file2, length: 500);
        _fs.SetModified(file2, DateTime.UtcNow.AddHours(-1));
        _fs.SetModified(Path.Combine(_stagingRoot, op2.ToString("D")), DateTime.UtcNow.AddHours(-1));

        // Purge older than 7 days
        await _stager.PurgeAsync(new StagingPolicy(MaxAge: TimeSpan.FromDays(7), MaxTotalBytes: 10_000));

        Assert.False(_fs.DirectoryExists(Path.Combine(_stagingRoot, op1.ToString("D"))));
        Assert.True(_fs.DirectoryExists(Path.Combine(_stagingRoot, op2.ToString("D"))));
    }

    [Fact]
    public async Task Staging_SameVolume_UsesVolumeRootStaging_AndMarksHidden()
    {
        var opId = Guid.NewGuid();
        _fs.CreateVirtualFile(@"D:\Folder\file.txt", 100);

        var staged = await _stager.StageAsync(@"D:\Folder\file.txt", opId, 0);

        // Should stage under D:\.fm-staging\<opId>\
        Assert.StartsWith(@"D:\.fm-staging\", staged.BackupPath, StringComparison.OrdinalIgnoreCase);
        Assert.True(_fs.FileExists(staged.BackupPath));

        var rootInfo = _fs.GetInfo(@"D:\.fm-staging");
        Assert.NotNull(rootInfo);
        Assert.True((rootInfo.Attributes & FileAttributes.Hidden) != 0);
    }

    [Fact]
    public async Task Staging_FallbackToLocalAppData_WhenVolumeRootsFail_FiresWarning()
    {
        string? warningMessage = null;
        _stager.WarningHandler = msg => warningMessage = msg;

        // Simulate volume root & destination folder failure by creating files at those paths
        _fs.CreateVirtualFile(@"E:\.fm-staging", 0); // Blocking file preventing dir creation
        _fs.CreateVirtualFile(@"E:\Dest\.fm-staging", 0);
        _fs.CreateVirtualFile(@"E:\Dest\file.txt", 50);

        var opId = Guid.NewGuid();
        var staged = await _stager.StageAsync(@"E:\Dest\file.txt", opId, 0);

        Assert.StartsWith(_stagingRoot, staged.BackupPath, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(warningMessage);
        Assert.Contains("falling back", warningMessage);
    }

    [Fact]
    public async Task PurgeAsync_PreservesLiveEntries_AndDeletesOrphanedStagingOlderThan24Hours()
    {
        var liveOp = Guid.NewGuid();
        var orphanedOldOp = Guid.NewGuid();
        var orphanedRecentOp = Guid.NewGuid();

        // Staging under D:\.fm-staging\
        string liveDir = Path.Combine(@"D:\.fm-staging", liveOp.ToString("D"));
        _fs.CreateDirectory(liveDir);
        _fs.CreateVirtualFile(Path.Combine(liveDir, "0_doc.txt"), 100);
        _fs.SetModified(liveDir, DateTime.UtcNow.AddDays(-2));

        string oldOrphanDir = Path.Combine(@"D:\.fm-staging", orphanedOldOp.ToString("D"));
        _fs.CreateDirectory(oldOrphanDir);
        string oldOrphanFile = Path.Combine(oldOrphanDir, "0_doc.txt");
        _fs.CreateVirtualFile(oldOrphanFile, 100);
        _fs.SetModified(oldOrphanFile, DateTime.UtcNow.AddHours(-25));
        _fs.SetModified(oldOrphanDir, DateTime.UtcNow.AddHours(-25)); // > 24 hours orphaned

        string recentOrphanDir = Path.Combine(@"D:\.fm-staging", orphanedRecentOp.ToString("D"));
        _fs.CreateDirectory(recentOrphanDir);
        _fs.CreateVirtualFile(Path.Combine(recentOrphanDir, "0_doc.txt"), 100);
        _fs.SetModified(recentOrphanDir, DateTime.UtcNow.AddHours(-2)); // < 24 hours

        var liveIds = new HashSet<Guid> { liveOp };
        await _stager.PurgeAsync(StagingPolicy.Default, liveIds);

        // Live must be preserved
        Assert.True(_fs.DirectoryExists(liveDir));
        // Orphaned > 24h must be deleted
        Assert.False(_fs.DirectoryExists(oldOrphanDir));
        // Recent orphan (< 24h) preserved for active/recent transactions
        Assert.True(_fs.DirectoryExists(recentOrphanDir));
    }
}
