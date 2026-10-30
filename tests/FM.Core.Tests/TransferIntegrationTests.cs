using System.IO;
using System.Security.Cryptography;
using FM.Core.FileSystem;
using FM.Core.Operations.Transfer;
using FM.Tests.Common;

namespace FM.Core.Tests;

/// <summary>
/// Physical disk integration tests for the TransferEngine.
/// Strictly bounded by the hard resource policy: max 3 tests, <= 20 files, <= 256 KB total across all runs.
/// </summary>
public class TransferIntegrationTests
{
    private static string ComputeHash(string path)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(path);
        byte[] hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash);
    }

    [Fact]
    public async Task RealDisk_Copy_VerifiedAndPreservesTimestamps()
    {
        using var workspace = new TestWorkspace("transfer_copy");
        var fs = new PhysicalFileSystem();

        string srcDir = workspace.CreateDirectory("source");
        string dstDir = workspace.CreateDirectory("dest");

        byte[] content1 = new byte[1024]; // 1 KB
        Random.Shared.NextBytes(content1);
        string file1 = workspace.CreateFile(Path.Combine("source", "file1.bin"), content1);

        byte[] content2 = new byte[4096]; // 4 KB
        Random.Shared.NextBytes(content2);
        string file2 = workspace.CreateFile(Path.Combine("source", "file2.bin"), content2);

        var pastTime = DateTime.UtcNow.AddHours(-3);
        File.SetLastWriteTimeUtc(file1, pastTime);
        File.SetLastWriteTimeUtc(file2, pastTime);

        string hash1 = ComputeHash(file1);
        string hash2 = ComputeHash(file2);

        var engine = new TransferEngine(fs);
        var request = new TransferRequest(
            TransferKind.Copy,
            new[] { file1, file2 },
            dstDir,
            new TransferOptions(VerifyAfterCopy: true, PreserveTimestamps: true));

        var result = await engine.RunAsync(request);

        Assert.Equal(TransferState.Completed, result.FinalState);
        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, i => Assert.Equal(ItemOutcome.Copied, i.Outcome));

        string dstFile1 = Path.Combine(dstDir, "file1.bin");
        string dstFile2 = Path.Combine(dstDir, "file2.bin");

        Assert.True(File.Exists(dstFile1));
        Assert.True(File.Exists(dstFile2));

        Assert.Equal(hash1, ComputeHash(dstFile1));
        Assert.Equal(hash2, ComputeHash(dstFile2));

        // Timestamps preserved
        var dstTime1 = File.GetLastWriteTimeUtc(dstFile1);
        var dstTime2 = File.GetLastWriteTimeUtc(dstFile2);
        Assert.True(Math.Abs((dstTime1 - pastTime).TotalSeconds) < 2.0);
        Assert.True(Math.Abs((dstTime2 - pastTime).TotalSeconds) < 2.0);

        // No part files remain
        Assert.Empty(Directory.GetFiles(dstDir, "*.fmpart"));
    }

    [Fact]
    public async Task RealDisk_Move_VerifiedAndDeletesSource()
    {
        using var workspace = new TestWorkspace("transfer_move");
        var fs = new PhysicalFileSystem();

        string srcDir = workspace.CreateDirectory("source");
        string dstDir = workspace.CreateDirectory("dest");

        string srcSubDir = workspace.CreateDirectory(Path.Combine("source", "sub"));

        byte[] content1 = new byte[2048]; // 2 KB
        Random.Shared.NextBytes(content1);
        string file1 = workspace.CreateFile(Path.Combine("source", "doc.bin"), content1);

        byte[] content2 = new byte[1024]; // 1 KB
        Random.Shared.NextBytes(content2);
        string file2 = workspace.CreateFile(Path.Combine("source", "sub", "child.bin"), content2);

        string hash1 = ComputeHash(file1);
        string hash2 = ComputeHash(file2);

        var engine = new TransferEngine(fs);
        var request = new TransferRequest(
            TransferKind.Move,
            new[] { srcDir },
            dstDir,
            new TransferOptions(VerifyAfterCopy: true));

        var result = await engine.RunAsync(request);

        Assert.Equal(TransferState.Completed, result.FinalState);

        string dstSubDir = Path.Combine(dstDir, "source", "sub");
        string dstFile1 = Path.Combine(dstDir, "source", "doc.bin");
        string dstFile2 = Path.Combine(dstDir, "source", "sub", "child.bin");

        Assert.True(File.Exists(dstFile1));
        Assert.True(File.Exists(dstFile2));
        Assert.True(Directory.Exists(dstSubDir));

        Assert.Equal(hash1, ComputeHash(dstFile1));
        Assert.Equal(hash2, ComputeHash(dstFile2));

        // Source must be deleted after move
        Assert.False(File.Exists(file1));
        Assert.False(File.Exists(file2));
        Assert.False(Directory.Exists(srcSubDir));
        Assert.False(Directory.Exists(srcDir));
    }

    [Fact]
    public async Task RealDisk_Copy_AndUndo_RestoresState()
    {
        using var workspace = new TestWorkspace("transfer_undo");
        var fs = new PhysicalFileSystem();

        string srcDir = workspace.CreateDirectory("source");
        string dstDir = workspace.CreateDirectory("dest");

        byte[] content = new byte[1024]; // 1 KB
        Random.Shared.NextBytes(content);
        string file = workspace.CreateFile(Path.Combine("source", "undo_me.bin"), content);

        var engine = new TransferEngine(fs);
        var request = new TransferRequest(
            TransferKind.Copy,
            new[] { file },
            dstDir,
            new TransferOptions(VerifyAfterCopy: true));

        var result = await engine.RunAsync(request);
        Assert.Equal(TransferState.Completed, result.FinalState);

        string dstFile = Path.Combine(dstDir, "undo_me.bin");
        Assert.True(File.Exists(dstFile));

        // Perform Undo
        var undoOp = new TransferUndoOperation(fs, TransferKind.Copy, result);
        Assert.True(undoOp.CanUndo);

        var undoResult = await undoOp.UndoAsync();
        Assert.True(undoResult.Success);

        // Destination deleted, source preserved
        Assert.False(File.Exists(dstFile));
        Assert.True(File.Exists(file));
    }
}
