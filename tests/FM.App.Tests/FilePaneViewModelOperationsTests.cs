using System.IO;
using System.Runtime.CompilerServices;
using FM.App.Tests.Fakes;
using FM.App.ViewModels;
using FM.Core.FileSystem;
using FM.Core.Models;
using FM.Core.Operations;
using FM.Core.Services;

namespace FM.App.Tests;

public class FileSystemBackedDirectoryService : IDirectoryService
{
    private readonly IFileSystem _fs;

    public FileSystemBackedDirectoryService(IFileSystem fs)
    {
        _fs = fs;
    }

    public async IAsyncEnumerable<IReadOnlyList<FileEntry>> EnumerateBatchesAsync(
        string path,
        int batchSize = 200,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!_fs.DirectoryExists(path))
        {
            throw new DirectoryNotFoundException($"Directory not found: '{path}'");
        }

        var entries = _fs.EnumerateEntries(path)
            .Select(p =>
            {
                bool isDir = _fs.DirectoryExists(p);
                string name = Path.GetFileName(p);
                return new FileEntry(name, p, isDir, isDir ? 0 : 500, DateTime.UtcNow);
            }).ToList();

        yield return entries;
    }
}

public class FilePaneViewModelOperationsTests
{
    private const string MockRoot = @"C:\MockWorkspace";
    private readonly InMemoryFileSystem _fileSystem;
    private readonly FileSystemBackedDirectoryService _dirService;
    private readonly FakeIconService _iconService;
    private readonly FakeInputDialogService _inputDialog;
    private readonly FilePaneViewModel _viewModel;

    public FilePaneViewModelOperationsTests()
    {
        _fileSystem = new InMemoryFileSystem();
        _fileSystem.CreateDirectory(MockRoot);

        _dirService = new FileSystemBackedDirectoryService(_fileSystem);
        _iconService = new FakeIconService();
        _inputDialog = new FakeInputDialogService();

        _viewModel = new FilePaneViewModel(
            _dirService,
            _iconService,
            _fileSystem,
            _inputDialog);
    }

    [Fact]
    public async Task NewFolderCommand_PromptsAndCreatesFolder_RefreshesAndSelects()
    {
        await _viewModel.NavigateInternalAsync(MockRoot, NavigationPhaseType.New);
        Assert.Empty(_viewModel.Items);

        _inputDialog.NextResult = "MyNewFolder";
        await _viewModel.NewFolderCommand.ExecuteAsync(null);

        // Verify folder exists in filesystem
        Assert.True(_fileSystem.DirectoryExists(Path.Combine(MockRoot, "MyNewFolder")));

        // Verify pane refreshed and item is selected
        Assert.Single(_viewModel.Items);
        Assert.Equal("MyNewFolder", _viewModel.Items[0].Name);
        Assert.Equal(_viewModel.Items[0], _viewModel.SelectedItem);
        Assert.True(_viewModel.UndoCommand.CanExecute(null));

        // Test Undo
        await _viewModel.UndoCommand.ExecuteAsync(null);
        Assert.False(_fileSystem.DirectoryExists(Path.Combine(MockRoot, "MyNewFolder")));
        Assert.Empty(_viewModel.Items);
        Assert.False(_viewModel.UndoCommand.CanExecute(null));
    }

    [Fact]
    public async Task RenameCommand_File_PreSelectsNameWithoutExtension()
    {
        string filePath = Path.Combine(MockRoot, "report.pdf");
        _fileSystem.CreateFile(filePath);

        await _viewModel.NavigateInternalAsync(MockRoot, NavigationPhaseType.New);
        Assert.Single(_viewModel.Items);

        _viewModel.SelectedItem = _viewModel.Items[0];
        Assert.True(_viewModel.RenameCommand.CanExecute(null));

        // Set up input dialog response
        _inputDialog.NextResult = "summary.pdf";
        await _viewModel.RenameCommand.ExecuteAsync(null);

        // Verify prompt captured name and pre-selected without extension ("report" = 6 chars)
        Assert.Equal("report.pdf", _inputDialog.LastInitialValue);
        Assert.Equal(6, _inputDialog.LastSelectionLength);

        // Verify file was renamed in filesystem and UI
        Assert.False(_fileSystem.FileExists(filePath));
        Assert.True(_fileSystem.FileExists(Path.Combine(MockRoot, "summary.pdf")));
        Assert.Single(_viewModel.Items);
        Assert.Equal("summary.pdf", _viewModel.Items[0].Name);
        Assert.Equal(_viewModel.Items[0], _viewModel.SelectedItem);

        // Undo rename
        await _viewModel.UndoCommand.ExecuteAsync(null);
        Assert.True(_fileSystem.FileExists(filePath));
        Assert.False(_fileSystem.FileExists(Path.Combine(MockRoot, "summary.pdf")));
        Assert.Equal("report.pdf", _viewModel.Items[0].Name);
    }

    [Fact]
    public async Task UndoCommand_WhenFolderNoLongerEmpty_ShowsErrorMessage()
    {
        await _viewModel.NavigateInternalAsync(MockRoot, NavigationPhaseType.New);

        _inputDialog.NextResult = "FolderWithFiles";
        await _viewModel.NewFolderCommand.ExecuteAsync(null);

        string folderPath = Path.Combine(MockRoot, "FolderWithFiles");
        Assert.True(_fileSystem.DirectoryExists(folderPath));

        // Simulate user placing a file in the folder
        _fileSystem.CreateFile(Path.Combine(folderPath, "keep_me.txt"));

        // Attempt undo
        await _viewModel.UndoCommand.ExecuteAsync(null);

        // Verify error banner is populated and directory was not deleted
        Assert.NotNull(_viewModel.ErrorMessage);
        Assert.Contains("not empty", _viewModel.ErrorMessage);
        Assert.True(_fileSystem.DirectoryExists(folderPath));
    }
}
