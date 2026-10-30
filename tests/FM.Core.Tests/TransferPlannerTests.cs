using FM.Core.FileSystem;
using FM.Core.Operations.Transfer;

namespace FM.Core.Tests;

public class TransferPlannerTests
{
    [Fact]
    public void DestinationInsideSource_ThrowsInvalidOperationException()
    {
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory(@"C:\SourceFolder\Nested\Target");

        var request = new TransferRequest(
            TransferKind.Copy,
            new[] { @"C:\SourceFolder" },
            @"C:\SourceFolder\Nested\Target",
            new TransferOptions());

        var ex = Assert.Throws<InvalidOperationException>(() => TransferPlanner.CreatePlan(fs, request));
        Assert.Contains("subdirectory of itself", ex.Message);
    }

    [Fact]
    public void DestinationEqualToSource_OnMove_ThrowsInvalidOperationException()
    {
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory(@"C:\SourceFolder");

        var request = new TransferRequest(
            TransferKind.Move,
            new[] { @"C:\SourceFolder" },
            @"C:\SourceFolder",
            new TransferOptions());

        var ex = Assert.Throws<InvalidOperationException>(() => TransferPlanner.CreatePlan(fs, request));
        Assert.Contains("Cannot move directory", ex.Message);
    }

    [Fact]
    public void NonexistentSource_ThrowsFileNotFoundException()
    {
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory(@"C:\Dest");

        var request = new TransferRequest(
            TransferKind.Copy,
            new[] { @"C:\GhostFile.txt" },
            @"C:\Dest",
            new TransferOptions());

        Assert.Throws<FileNotFoundException>(() => TransferPlanner.CreatePlan(fs, request));
    }

    [Fact]
    public void SameDirectoryCopy_ResolvesKeepBothName()
    {
        var fs = new InMemoryFileSystem();
        fs.CreateVirtualFile(@"C:\Folder\doc.txt", 100);

        var request = new TransferRequest(
            TransferKind.Copy,
            new[] { @"C:\Folder\doc.txt" },
            @"C:\Folder",
            new TransferOptions());

        var plan = TransferPlanner.CreatePlan(fs, request);

        Assert.Single(plan.Items);
        Assert.Equal("doc (2).txt", plan.Items[0].RelativeDestinationPath);
        Assert.Equal(100, plan.OverallTotalBytes);
        Assert.Equal(1, plan.FilesTotal);
    }

    [Fact]
    public void InsufficientFreeSpace_FailsEarly()
    {
        var fs = new InMemoryFileSystem();
        fs.CreateVirtualFile(@"C:\big.iso", 500 * 1024 * 1024); // 500 MB
        fs.SetVolumeFreeSpace("D:", 200 * 1024 * 1024);         // 200 MB free

        var request = new TransferRequest(
            TransferKind.Copy,
            new[] { @"C:\big.iso" },
            @"D:\Backup",
            new TransferOptions());

        var ex = Assert.Throws<IOException>(() => TransferPlanner.CreatePlan(fs, request));
        Assert.Contains("Insufficient disk space", ex.Message);
    }

    [Fact]
    public void DirectoryExpansion_ComputesTotals_AndSkipsReparsePoints()
    {
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory(@"C:\Source\Sub");
        fs.CreateVirtualFile(@"C:\Source\a.txt", 1000);
        fs.CreateVirtualFile(@"C:\Source\Sub\b.txt", 2000);

        // Add a reparse point inside
        fs.CreateReparsePoint(@"C:\Source\Sub\SymlinkFolder", isDirectory: true);
        fs.CreateVirtualFile(@"C:\Source\Sub\SymlinkFolder\invisible.txt", 5000);

        var request = new TransferRequest(
            TransferKind.Copy,
            new[] { @"C:\Source" },
            @"D:\Dest",
            new TransferOptions());

        var plan = TransferPlanner.CreatePlan(fs, request);

        // a.txt (1000) + b.txt (2000) = 3000 bytes. invisible.txt in symlink is skipped!
        Assert.Equal(3000, plan.OverallTotalBytes);
        Assert.Equal(2, plan.FilesTotal);
        Assert.Contains(plan.Items, i => i.RelativeDestinationPath == Path.Combine("Source", "a.txt"));
        Assert.Contains(plan.Items, i => i.RelativeDestinationPath == Path.Combine("Source", "Sub", "b.txt"));
        Assert.DoesNotContain(plan.Items, i => i.RelativeDestinationPath.Contains("invisible.txt"));
    }
}
