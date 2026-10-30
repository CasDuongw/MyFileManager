using FM.Core.FileSystem;
using FM.Core.Operations;
using FM.Tests.Common;

namespace FM.Core.Tests;

public class FileOperationsTests
{
    private const string Root = @"C:\TestRoot";

    [Fact]
    public async Task CreateFolder_HappyPath_CreatesDirectoryAndUndoDeletesIt()
    {
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory(Root);

        var op = new CreateFolderOperation(fs, Root, "NewFolder");
        var result = await op.DoAsync();

        Assert.True(result.Success);
        Assert.True(fs.DirectoryExists(@"C:\TestRoot\NewFolder"));

        var undoResult = await op.UndoAsync();
        Assert.True(undoResult.Success);
        Assert.False(fs.DirectoryExists(@"C:\TestRoot\NewFolder"));
    }

    [Fact]
    public void CreateFolder_AutoNumbering_GeneratesUniqueNames()
    {
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory(Root);

        string name1 = CreateFolderOperation.GetDefaultUniqueFolderName(fs, Root);
        Assert.Equal("New folder", name1);
        fs.CreateDirectory(Path.Combine(Root, name1));

        string name2 = CreateFolderOperation.GetDefaultUniqueFolderName(fs, Root);
        Assert.Equal("New folder (2)", name2);
        fs.CreateDirectory(Path.Combine(Root, name2));

        string name3 = CreateFolderOperation.GetDefaultUniqueFolderName(fs, Root);
        Assert.Equal("New folder (3)", name3);
    }

    [Fact]
    public async Task CreateFolder_Collision_FailsGracefully()
    {
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory(Root);
        fs.CreateDirectory(@"C:\TestRoot\Existing");

        var op = new CreateFolderOperation(fs, Root, "Existing");
        var result = await op.DoAsync();

        Assert.False(result.Success);
        Assert.Contains("already exists", result.ErrorMessage);
    }

    [Fact]
    public async Task CreateFolder_UndoWhenFolderNotEmpty_FailsSafelyWithoutDeletingFiles()
    {
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory(Root);

        var op = new CreateFolderOperation(fs, Root, "MyFolder");
        var createResult = await op.DoAsync();
        Assert.True(createResult.Success);

        // Add a file inside the newly created folder
        fs.CreateFile(@"C:\TestRoot\MyFolder\user_document.txt");

        // Attempt undo
        var undoResult = await op.UndoAsync();
        Assert.False(undoResult.Success);
        Assert.Contains("not empty", undoResult.ErrorMessage);

        // Verify folder and user file are intact
        Assert.True(fs.DirectoryExists(@"C:\TestRoot\MyFolder"));
        Assert.True(fs.FileExists(@"C:\TestRoot\MyFolder\user_document.txt"));
    }

    [Fact]
    public async Task RenameFile_HappyPath_RenamesAndUndoes()
    {
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory(Root);
        fs.CreateFile(@"C:\TestRoot\old.txt");

        var op = new RenameOperation(fs, @"C:\TestRoot\old.txt", "new.txt", isDirectory: false);
        var doResult = await op.DoAsync();

        Assert.True(doResult.Success);
        Assert.False(fs.FileExists(@"C:\TestRoot\old.txt"));
        Assert.True(fs.FileExists(@"C:\TestRoot\new.txt"));

        var undoResult = await op.UndoAsync();
        Assert.True(undoResult.Success);
        Assert.True(fs.FileExists(@"C:\TestRoot\old.txt"));
        Assert.False(fs.FileExists(@"C:\TestRoot\new.txt"));
    }

    [Fact]
    public async Task RenameFolder_HappyPath_RenamesAndUndoes()
    {
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory(Root);
        fs.CreateDirectory(@"C:\TestRoot\OldFolder");
        fs.CreateFile(@"C:\TestRoot\OldFolder\nested.txt");

        var op = new RenameOperation(fs, @"C:\TestRoot\OldFolder", "NewFolder", isDirectory: true);
        var doResult = await op.DoAsync();

        Assert.True(doResult.Success);
        Assert.False(fs.DirectoryExists(@"C:\TestRoot\OldFolder"));
        Assert.True(fs.DirectoryExists(@"C:\TestRoot\NewFolder"));
        Assert.True(fs.FileExists(@"C:\TestRoot\NewFolder\nested.txt"));

        var undoResult = await op.UndoAsync();
        Assert.True(undoResult.Success);
        Assert.True(fs.DirectoryExists(@"C:\TestRoot\OldFolder"));
        Assert.True(fs.FileExists(@"C:\TestRoot\OldFolder\nested.txt"));
        Assert.False(fs.DirectoryExists(@"C:\TestRoot\NewFolder"));
    }

    [Fact]
    public async Task Rename_CollisionOnRename_FailsGracefully()
    {
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory(Root);
        fs.CreateFile(@"C:\TestRoot\file1.txt");
        fs.CreateFile(@"C:\TestRoot\file2.txt");

        var op = new RenameOperation(fs, @"C:\TestRoot\file1.txt", "file2.txt", isDirectory: false);
        var doResult = await op.DoAsync();

        Assert.False(doResult.Success);
        Assert.Contains("already exists", doResult.ErrorMessage);
        Assert.True(fs.FileExists(@"C:\TestRoot\file1.txt"));
    }

    [Fact]
    public async Task Rename_CollisionOnUndo_FailsGracefully()
    {
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory(Root);
        fs.CreateFile(@"C:\TestRoot\first.txt");

        var op = new RenameOperation(fs, @"C:\TestRoot\first.txt", "second.txt", isDirectory: false);
        var doResult = await op.DoAsync();
        Assert.True(doResult.Success);

        // Someone creates a new file at first.txt while second.txt exists
        fs.CreateFile(@"C:\TestRoot\first.txt");

        // Attempt undo
        var undoResult = await op.UndoAsync();
        Assert.False(undoResult.Success);
        Assert.Contains("already exists", undoResult.ErrorMessage);

        // second.txt is still intact
        Assert.True(fs.FileExists(@"C:\TestRoot\second.txt"));
    }

    [Fact]
    public async Task UndoStack_ManagesOperationsCorrectly()
    {
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory(Root);

        var stack = new UndoStack();
        Assert.False(stack.CanUndo);

        var op1 = new CreateFolderOperation(fs, Root, "Folder1");
        await op1.DoAsync();
        stack.Push(op1);

        var op2 = new CreateFolderOperation(fs, Root, "Folder2");
        await op2.DoAsync();
        stack.Push(op2);

        Assert.True(stack.CanUndo);
        Assert.Equal("Create folder 'Folder2'", stack.NextUndoDescription);

        var undo2 = await stack.UndoAsync();
        Assert.True(undo2?.Success);
        Assert.False(fs.DirectoryExists(@"C:\TestRoot\Folder2"));

        Assert.True(stack.CanUndo);
        Assert.Equal("Create folder 'Folder1'", stack.NextUndoDescription);

        var undo1 = await stack.UndoAsync();
        Assert.True(undo1?.Success);
        Assert.False(fs.DirectoryExists(@"C:\TestRoot\Folder1"));

        Assert.False(stack.CanUndo);
    }

    // =========================================================================
    // PhysicalFileSystem Integration Tests (Max 3 tests as per B1/C5 budget)
    // =========================================================================

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PhysicalFileSystem_NewFolderAndUndo_SucceedsOnDisk()
    {
        using var ws = new TestWorkspace("PhysNewFolder");
        var fs = new PhysicalFileSystem();

        var op = new CreateFolderOperation(fs, ws.RootPath, "TestDir");
        var result = await op.DoAsync();
        Assert.True(result.Success);
        Assert.True(Directory.Exists(Path.Combine(ws.RootPath, "TestDir")));

        var undo = await op.UndoAsync();
        Assert.True(undo.Success);
        Assert.False(Directory.Exists(Path.Combine(ws.RootPath, "TestDir")));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PhysicalFileSystem_RenameAndUndo_SucceedsOnDisk()
    {
        using var ws = new TestWorkspace("PhysRename");
        var fs = new PhysicalFileSystem();
        string file = ws.CreateTextFile("source.txt", "hello");

        var op = new RenameOperation(fs, file, "renamed.txt", isDirectory: false);
        var result = await op.DoAsync();
        Assert.True(result.Success);
        Assert.False(File.Exists(file));
        Assert.True(File.Exists(Path.Combine(ws.RootPath, "renamed.txt")));

        var undo = await op.UndoAsync();
        Assert.True(undo.Success);
        Assert.True(File.Exists(file));
        Assert.False(File.Exists(Path.Combine(ws.RootPath, "renamed.txt")));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PhysicalFileSystem_Collision_FailsGracefullyOnDisk()
    {
        using var ws = new TestWorkspace("PhysCollision");
        var fs = new PhysicalFileSystem();
        ws.CreateTextFile("existing.txt", "data");

        var op = new CreateFolderOperation(fs, ws.RootPath, "existing.txt");
        var result = await op.DoAsync();
        Assert.False(result.Success);
        Assert.Contains("already exists", result.ErrorMessage);
    }
}
