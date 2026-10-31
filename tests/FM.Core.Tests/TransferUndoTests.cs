using FM.Core.FileSystem;
using FM.Core.Operations.Transfer;

namespace FM.Core.Tests;

public class TransferUndoTests
{
    [Fact]
    public async Task UndoCopy_DeletesCreatedFilesAndEmptyDirectories()
    {
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory(@"C:\dest\sub");
        var now = DateTime.UtcNow;

        fs.CreateVirtualFile(@"C:\dest\sub\file1.txt", 100);
        fs.SetModified(@"C:\dest\sub\file1.txt", now);

        var result = new TransferResult(
            TransferState.Completed,
            new[]
            {
                new TransferItemResult(@"C:\src\sub", @"C:\dest\sub", IsDirectory: true, ItemOutcome.Copied, 0, now, null),
                new TransferItemResult(@"C:\src\sub\file1.txt", @"C:\dest\sub\file1.txt", IsDirectory: false, ItemOutcome.Copied, 100, now, null)
            });

        var undoOp = new TransferUndoOperation(fs, TransferKind.Copy, result);
        Assert.True(undoOp.CanUndo);

        var undoResult = await undoOp.UndoAsync();
        Assert.True(undoResult.Success);

        Assert.False(fs.FileExists(@"C:\dest\sub\file1.txt"));
        Assert.False(fs.DirectoryExists(@"C:\dest\sub"));
        Assert.True(fs.DirectoryExists(@"C:\dest"));
    }

    [Fact]
    public async Task UndoCopy_SkipsModifiedFiles_PreservesDirectoriesContainingThem()
    {
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory(@"C:\dest\sub");
        var originalTime = DateTime.UtcNow.AddMinutes(-10);
        var modifiedTime = DateTime.UtcNow;

        fs.CreateVirtualFile(@"C:\dest\sub\file1.txt", 100);
        fs.SetModified(@"C:\dest\sub\file1.txt", modifiedTime); // modified after copy

        var result = new TransferResult(
            TransferState.Completed,
            new[]
            {
                new TransferItemResult(@"C:\src\sub", @"C:\dest\sub", IsDirectory: true, ItemOutcome.Copied, 0, originalTime, null),
                new TransferItemResult(@"C:\src\sub\file1.txt", @"C:\dest\sub\file1.txt", IsDirectory: false, ItemOutcome.Copied, 100, originalTime, null)
            });

        var undoOp = new TransferUndoOperation(fs, TransferKind.Copy, result);
        var undoResult = await undoOp.UndoAsync();

        Assert.False(undoResult.Success);
        Assert.Contains("modified after copy", undoResult.ErrorMessage);
        Assert.True(fs.FileExists(@"C:\dest\sub\file1.txt"));
        Assert.True(fs.DirectoryExists(@"C:\dest\sub"));
    }

    [Fact]
    public async Task UndoMove_RestoresFilesToOriginalLocation()
    {
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory(@"D:\dest");
        var now = DateTime.UtcNow;

        fs.CreateVirtualFile(@"D:\dest\file1.txt", 200);
        fs.SetModified(@"D:\dest\file1.txt", now);

        var result = new TransferResult(
            TransferState.Completed,
            new[]
            {
                new TransferItemResult(@"C:\src\file1.txt", @"D:\dest\file1.txt", IsDirectory: false, ItemOutcome.Moved, 200, now, null)
            });

        var undoOp = new TransferUndoOperation(fs, TransferKind.Move, result);
        Assert.True(undoOp.CanUndo);

        var undoResult = await undoOp.UndoAsync();
        Assert.True(undoResult.Success);

        Assert.False(fs.FileExists(@"D:\dest\file1.txt"));
        Assert.True(fs.FileExists(@"C:\src\file1.txt"));
    }

    [Fact]
    public async Task UndoMove_FailsIfOriginalLocationOccupied()
    {
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory(@"C:\src");
        fs.CreateDirectory(@"D:\dest");
        var now = DateTime.UtcNow;

        fs.CreateVirtualFile(@"D:\dest\file1.txt", 200);
        fs.SetModified(@"D:\dest\file1.txt", now);

        // Another file appeared at C:\src\file1.txt
        fs.CreateVirtualFile(@"C:\src\file1.txt", 50);

        var result = new TransferResult(
            TransferState.Completed,
            new[]
            {
                new TransferItemResult(@"C:\src\file1.txt", @"D:\dest\file1.txt", IsDirectory: false, ItemOutcome.Moved, 200, now, null)
            });

        var undoOp = new TransferUndoOperation(fs, TransferKind.Move, result);
        var undoResult = await undoOp.UndoAsync();

        Assert.False(undoResult.Success);
        Assert.Contains("already occupied", undoResult.ErrorMessage);
        Assert.True(fs.FileExists(@"D:\dest\file1.txt"));
    }

    [Fact]
    public async Task UndoMove_FailsIfDestinationFileModified()
    {
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory(@"D:\dest");
        var originalTime = DateTime.UtcNow.AddMinutes(-5);

        fs.CreateVirtualFile(@"D:\dest\file1.txt", 300); // Size changed
        fs.SetModified(@"D:\dest\file1.txt", originalTime);

        var result = new TransferResult(
            TransferState.Completed,
            new[]
            {
                new TransferItemResult(@"C:\src\file1.txt", @"D:\dest\file1.txt", IsDirectory: false, ItemOutcome.Moved, 200, originalTime, null)
            });

        var undoOp = new TransferUndoOperation(fs, TransferKind.Move, result);
        var undoResult = await undoOp.UndoAsync();

        Assert.False(undoResult.Success);
        Assert.Contains("was modified after move", undoResult.ErrorMessage);
        Assert.True(fs.FileExists(@"D:\dest\file1.txt"));
    }

    [Fact]
    public void ReplacedOutcome_MarkedNonUndoable()
    {
        var fs = new InMemoryFileSystem();
        var result = new TransferResult(
            TransferState.Completed,
            new[]
            {
                new TransferItemResult(@"C:\src\file1.txt", @"D:\dest\file1.txt", IsDirectory: false, ItemOutcome.Replaced, 100, DateTime.UtcNow, null)
            });

        var undoOp = new TransferUndoOperation(fs, TransferKind.Copy, result);
        Assert.False(undoOp.CanUndo);
        Assert.Contains("Overwritten files cannot be restored without backup staging", undoOp.NonUndoableReason);
    }

    [Fact]
    public void ReplacedOutcome_WithBackup_IsUndoable()
    {
        var fs = new InMemoryFileSystem();
        var stager = new StagingBackupStore(fs, @"C:\staging");
        fs.CreateVirtualFile(@"C:\staging\backup.bak", 100);

        var result = new TransferResult(
            TransferState.Completed,
            new[]
            {
                new TransferItemResult(@"C:\src\file1.txt", @"D:\dest\file1.txt", IsDirectory: false, ItemOutcome.Replaced, 100, DateTime.UtcNow, null, @"C:\staging\backup.bak")
            });

        var undoOp = new TransferUndoOperation(fs, TransferKind.Copy, result, stager);
        Assert.True(undoOp.CanUndo);
    }

    [Fact]
    public void NoTransferredItems_MarkedNonUndoable()
    {
        var fs = new InMemoryFileSystem();
        var result = new TransferResult(
            TransferState.CompletedWithErrors,
            new[]
            {
                new TransferItemResult(@"C:\src\file1.txt", @"D:\dest\file1.txt", IsDirectory: false, ItemOutcome.Failed, 100, null, "Disk full"),
                new TransferItemResult(@"C:\src\file2.txt", @"D:\dest\file2.txt", IsDirectory: false, ItemOutcome.Skipped, 100, null, null)
            });

        var undoOp = new TransferUndoOperation(fs, TransferKind.Copy, result);
        Assert.False(undoOp.CanUndo);
        Assert.Equal("No items were transferred.", undoOp.NonUndoableReason);
    }

    [Fact]
    public async Task EndToEnd_CopyAndUndo_WithTransferEngine()
    {
        var fs = new InMemoryFileSystem();
        fs.CreateVirtualFile(@"C:\src\hello.txt", 1024);
        fs.CreateDirectory(@"C:\dest");

        var engine = new TransferEngine(fs);
        var request = new TransferRequest(
            TransferKind.Copy,
            new[] { @"C:\src\hello.txt" },
            @"C:\dest",
            new TransferOptions(VerifyAfterCopy: false));

        var transferResult = await engine.RunAsync(request);
        Assert.Equal(TransferState.Completed, transferResult.FinalState);
        Assert.True(fs.FileExists(@"C:\dest\hello.txt"));

        var undoOp = new TransferUndoOperation(fs, TransferKind.Copy, transferResult);
        Assert.True(undoOp.CanUndo);

        var undoResult = await undoOp.UndoAsync();
        Assert.True(undoResult.Success);
        Assert.False(fs.FileExists(@"C:\dest\hello.txt"));
        Assert.True(fs.FileExists(@"C:\src\hello.txt"));
    }

    [Fact]
    public async Task UndoMove_CrossVolume_UsesVerifiedReverseTransfer()
    {
        var fs = new InMemoryFileSystem();
        var now = DateTime.UtcNow;
        fs.CreateDirectory(@"D:\dest");
        fs.CreateVirtualFile(@"D:\dest\large.iso", 10 * 1024);
        fs.SetModified(@"D:\dest\large.iso", now);

        var result = new TransferResult(
            TransferState.Completed,
            new[]
            {
                new TransferItemResult(@"C:\src\nested\large.iso", @"D:\dest\large.iso", IsDirectory: false, ItemOutcome.Moved, 10 * 1024, now, null)
            });

        // Original source parent folder C:\src\nested does not exist
        Assert.False(fs.DirectoryExists(@"C:\src\nested"));

        var undoOp = new TransferUndoOperation(fs, TransferKind.Move, result);
        var undoResult = await undoOp.UndoAsync();

        Assert.True(undoResult.Success, undoResult.ErrorMessage);
        Assert.True(fs.FileExists(@"C:\src\nested\large.iso"));
        Assert.False(fs.FileExists(@"D:\dest\large.iso"));
    }

    [Fact]
    public async Task Undo_Cancellation_CancelsGracefully()
    {
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory(@"C:\dest");
        fs.CreateVirtualFile(@"C:\dest\file.txt", 100);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancelled

        var result = new TransferResult(
            TransferState.Completed,
            new[]
            {
                new TransferItemResult(@"C:\src\file.txt", @"C:\dest\file.txt", false, ItemOutcome.Copied, 100, DateTime.UtcNow, null)
            });

        var undoOp = new TransferUndoOperation(fs, TransferKind.Copy, result);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => undoOp.UndoAsync(cts.Token));
    }
}

