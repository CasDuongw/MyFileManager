using FM.Tests.Common;

namespace FM.Core.Tests;

public class TestWorkspaceTests
{
    [Fact]
    public void TestWorkspace_CreatesRootAndCleansUpOnDispose()
    {
        string rootPath;
        using (var workspace = new TestWorkspace("test_lifecycle"))
        {
            rootPath = workspace.RootPath;
            Assert.True(Directory.Exists(rootPath));
            Assert.Contains("FM_test_lifecycle", rootPath);

            workspace.CreateTextFile("test.txt", "hello");
            Assert.Equal(1, workspace.FileCount);
            Assert.True(workspace.TotalBytes > 0);
        }

        Assert.False(Directory.Exists(rootPath));
    }

    [Fact]
    public void TestWorkspace_EnforcesFileCountBudget()
    {
        using var workspace = new TestWorkspace("test_count");

        for (int i = 0; i < TestWorkspace.MaxFiles; i++)
        {
            workspace.CreateTextFile($"file_{i}.txt", "a");
        }

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            workspace.CreateTextFile("exceeded.txt", "b");
        });

        Assert.Contains("budget exceeded", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TestWorkspace_EnforcesByteSizeBudget()
    {
        using var workspace = new TestWorkspace("test_bytes");

        // Attempt to create a file exceeding 1 MiB limit
        byte[] largeBytes = new byte[TestWorkspace.MaxBytes + 1];

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            workspace.CreateFile("huge.bin", largeBytes);
        });

        Assert.Contains("budget exceeded", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}

