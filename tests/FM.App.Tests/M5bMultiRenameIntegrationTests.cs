using System.IO;
using FM.App.Services;
using FM.App.Tests.Fakes;
using FM.App.ViewModels;
using FM.Core.FileSystem;
using FM.Core.Operations;
using FM.Core.Operations.MultiRename;
using Xunit;

namespace FM.App.Tests;

public class M5bMultiRenameIntegrationTests
{
    private const string MockRoot = @"C:\MockWorkspace";
    private readonly InMemoryFileSystem _fileSystem;
    private readonly FileSystemBackedDirectoryService _dirService;
    private readonly FakeIconService _iconService;
    private readonly FakeInputDialogService _inputDialog;
    private readonly FakeMultiRenameDialogService _fakeDialogService;
    private readonly FilePaneViewModel _viewModel;

    public M5bMultiRenameIntegrationTests()
    {
        _fileSystem = new InMemoryFileSystem();
        _fileSystem.CreateDirectory(MockRoot);

        _dirService = new FileSystemBackedDirectoryService(_fileSystem);
        _iconService = new FakeIconService();
        _inputDialog = new FakeInputDialogService();
        _fakeDialogService = new FakeMultiRenameDialogService();

        _viewModel = new FilePaneViewModel(
            _dirService,
            _iconService,
            _fileSystem,
            _inputDialog,
            multiRenameDialogService: _fakeDialogService);
    }

    [Fact]
    public async Task MultiRename_WhenMultipleItemsSelected_ExecutesBatchRenameAndUndoes()
    {
        string f1 = Path.Combine(MockRoot, "docA.txt");
        string f2 = Path.Combine(MockRoot, "docB.txt");
        string f3 = Path.Combine(MockRoot, "docC.txt");

        using (_fileSystem.CreateFileForWrite(f1)) { }
        using (_fileSystem.CreateFileForWrite(f2)) { }
        using (_fileSystem.CreateFileForWrite(f3)) { }

        await _viewModel.NavigateInternalAsync(MockRoot, NavigationPhaseType.New);
        Assert.Equal(3, _viewModel.Items.Count);

        _viewModel.UpdateSelectedItems(_viewModel.Items);

        _fakeDialogService.PlanGenerator = (inputs, fs) =>
        {
            var opts = new MultiRenameOptions("File_[C:01].[E]");
            return MultiRenamePlanner.Plan(inputs, opts, fs);
        };

        // Execute batch rename
        await _viewModel.MultiRenameCommand.ExecuteAsync(null);

        // Verify files renamed on disk
        Assert.Single(_fakeDialogService.ShownBatches);
        Assert.Equal(3, _fakeDialogService.ShownBatches[0].Count);

        string target1 = Path.Combine(MockRoot, "File_01.txt");
        string target2 = Path.Combine(MockRoot, "File_02.txt");
        string target3 = Path.Combine(MockRoot, "File_03.txt");

        Assert.True(_fileSystem.FileExists(target1));
        Assert.True(_fileSystem.FileExists(target2));
        Assert.True(_fileSystem.FileExists(target3));
        Assert.False(_fileSystem.FileExists(f1));
        Assert.False(_fileSystem.FileExists(f2));
        Assert.False(_fileSystem.FileExists(f3));

        // Compound Undo: single Undo restores all 3 files
        Assert.True(_viewModel.UndoStack.CanUndo);
        await _viewModel.UndoCommand.ExecuteAsync(null);

        Assert.True(_fileSystem.FileExists(f1));
        Assert.True(_fileSystem.FileExists(f2));
        Assert.True(_fileSystem.FileExists(f3));
        Assert.False(_fileSystem.FileExists(target1));
        Assert.False(_fileSystem.FileExists(target2));
        Assert.False(_fileSystem.FileExists(target3));
    }

    [Fact]
    public async Task RenameCommand_WhenMultipleItemsSelected_DelegatesToMultiRename()
    {
        string f1 = Path.Combine(MockRoot, "alpha.txt");
        string f2 = Path.Combine(MockRoot, "beta.txt");

        using (_fileSystem.CreateFileForWrite(f1)) { }
        using (_fileSystem.CreateFileForWrite(f2)) { }

        await _viewModel.NavigateInternalAsync(MockRoot, NavigationPhaseType.New);
        _viewModel.UpdateSelectedItems(_viewModel.Items);

        // RenameCommand on multiple items delegates to MultiRename
        await _viewModel.RenameCommand.ExecuteAsync(null);

        Assert.Single(_fakeDialogService.ShownBatches);
        Assert.Equal(2, _fakeDialogService.ShownBatches[0].Count);
    }

    [Fact]
    public async Task RenameCommand_WhenSingleItemSelected_PromptsForSingleRename()
    {
        string f1 = Path.Combine(MockRoot, "original.txt");
        using (_fileSystem.CreateFileForWrite(f1)) { }

        await _viewModel.NavigateInternalAsync(MockRoot, NavigationPhaseType.New);
        _viewModel.UpdateSelectedItems(new[] { _viewModel.Items[0] });

        _inputDialog.NextResult = "renamed_single.txt";
        await _viewModel.RenameCommand.ExecuteAsync(null);

        // Single rename prompt was invoked
        Assert.NotNull(_inputDialog.LastMessage);
        Assert.True(_fileSystem.FileExists(Path.Combine(MockRoot, "renamed_single.txt")));
        Assert.False(_fileSystem.FileExists(f1));

        // Fake multi-rename dialog was NOT invoked
        Assert.Empty(_fakeDialogService.ShownBatches);
    }

    [Fact]
    public async Task MultiRename_WhenCanceled_DoesNotModifyFiles()
    {
        string f1 = Path.Combine(MockRoot, "keep1.txt");
        string f2 = Path.Combine(MockRoot, "keep2.txt");

        using (_fileSystem.CreateFileForWrite(f1)) { }
        using (_fileSystem.CreateFileForWrite(f2)) { }

        await _viewModel.NavigateInternalAsync(MockRoot, NavigationPhaseType.New);
        _viewModel.UpdateSelectedItems(_viewModel.Items);

        _fakeDialogService.DialogResult = false;
        await _viewModel.MultiRenameCommand.ExecuteAsync(null);

        Assert.True(_fileSystem.FileExists(f1));
        Assert.True(_fileSystem.FileExists(f2));
        Assert.False(_viewModel.UndoStack.CanUndo);
    }

    [Fact]
    public async Task MultiRenameViewModel_LivePreview_ComputesPlanOffUiThread()
    {
        var inputs = new[]
        {
            new MultiRenameItemInput(Path.Combine(MockRoot, "item1.txt"), "item1.txt", false, DateTime.UtcNow),
            new MultiRenameItemInput(Path.Combine(MockRoot, "item2.txt"), "item2.txt", false, DateTime.UtcNow)
        };

        var vm = new MultiRenameViewModel(inputs, _fileSystem, plan => Task.FromResult(OperationResult.Ok()));
        await vm.CalculatePreviewAsync();

        Assert.Equal(2, vm.Rows.Count);
        Assert.False(vm.IsCalculating);
        Assert.False(vm.CanExecute); // Because default pattern [N].[E] leaves all names unchanged

        // Change pattern to something that renames
        vm.Pattern = "Item_[C:01].[E]";
        await vm.CalculatePreviewAsync();

        Assert.True(vm.CanExecute);
        Assert.Equal("Item_01.txt", vm.Rows[0].NewName);
        Assert.Equal("Item_02.txt", vm.Rows[1].NewName);

        // Change pattern to invalid Win32 name
        vm.Pattern = "Item*Invalid?.txt";
        await vm.CalculatePreviewAsync();

        Assert.False(vm.CanExecute);
        Assert.Contains("conflict", vm.StatusSummary.ToLowerInvariant());
    }
}
