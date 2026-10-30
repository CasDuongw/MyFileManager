using FM.App.Services;
using FM.App.Tests.Fakes;
using FM.App.ViewModels;
using FM.Core.FileSystem;
using FM.Core.Operations.Transfer;

namespace FM.App.Tests;

public class FakeSettingsService : ISettingsService
{
    public AppSettings LoadSettings() => new AppSettings();
    public void SaveSettings(AppSettings settings) { }
    public Task ValidatePathsAsync(AppSettings settings, TimeSpan? timeout = null) => Task.CompletedTask;
}

public class MainViewModelTransferTests
{
    private readonly InMemoryFileSystem _fs;
    private readonly FileSystemBackedDirectoryService _dirService;
    private readonly FakeIconService _iconService;
    private readonly FakeSettingsService _settingsService;
    private readonly MainViewModel _mainViewModel;

    public MainViewModelTransferTests()
    {
        _fs = new InMemoryFileSystem();
        _dirService = new FileSystemBackedDirectoryService(_fs);
        _iconService = new FakeIconService();
        _settingsService = new FakeSettingsService();

        _fs.CreateDirectory(@"C:\left");
        _fs.CreateDirectory(@"C:\right");
        _fs.CreateVirtualFile(@"C:\left\test.txt", 100);

        _mainViewModel = new MainViewModel(_dirService, _iconService, _settingsService, _fs);
    }

    [Fact]
    public async Task CopyToOtherPane_TransfersSelectedFiles_RefreshesPanes_PushesUndo()
    {
        await _mainViewModel.LeftPane.NavigateAsync(@"C:\left");
        await _mainViewModel.RightPane.NavigateAsync(@"C:\right");

        _mainViewModel.SetActivePane(_mainViewModel.LeftPane);
        var item = _mainViewModel.LeftPane.Items.First(i => i.Name == "test.txt");
        _mainViewModel.LeftPane.UpdateSelectedItems(new[] { item });

        Assert.True(_mainViewModel.CanTransferToOtherPane);

        await _mainViewModel.CopyToOtherPaneCommand.ExecuteAsync(null);

        Assert.True(_fs.FileExists(@"C:\left\test.txt"));
        Assert.True(_fs.FileExists(@"C:\right\test.txt"));
        Assert.True(_mainViewModel.LeftPane.UndoStack.CanUndo);

        // Undo
        var undoResult = await _mainViewModel.LeftPane.UndoStack.UndoAsync();
        Assert.NotNull(undoResult);
        Assert.True(undoResult.Success);
        Assert.False(_fs.FileExists(@"C:\right\test.txt"));
        Assert.True(_fs.FileExists(@"C:\left\test.txt"));
    }

    [Fact]
    public async Task MoveToOtherPane_TransfersSelectedFiles_RemovesFromSource_PushesUndo()
    {
        await _mainViewModel.LeftPane.NavigateAsync(@"C:\left");
        await _mainViewModel.RightPane.NavigateAsync(@"C:\right");

        _mainViewModel.SetActivePane(_mainViewModel.LeftPane);
        var item = _mainViewModel.LeftPane.Items.First(i => i.Name == "test.txt");
        _mainViewModel.LeftPane.UpdateSelectedItems(new[] { item });

        await _mainViewModel.MoveToOtherPaneCommand.ExecuteAsync(null);

        Assert.False(_fs.FileExists(@"C:\left\test.txt"));
        Assert.True(_fs.FileExists(@"C:\right\test.txt"));
        Assert.True(_mainViewModel.LeftPane.UndoStack.CanUndo);

        // Undo
        var undoResult = await _mainViewModel.LeftPane.UndoStack.UndoAsync();
        Assert.NotNull(undoResult);
        Assert.True(undoResult.Success);
        Assert.True(_fs.FileExists(@"C:\left\test.txt"));
        Assert.False(_fs.FileExists(@"C:\right\test.txt"));
    }

    [Fact]
    public void CanTransferToOtherPane_FalseWhenNoItemsSelected()
    {
        _mainViewModel.LeftPane.CurrentPath = @"C:\left";
        _mainViewModel.RightPane.CurrentPath = @"C:\right";
        _mainViewModel.SetActivePane(_mainViewModel.LeftPane);

        _mainViewModel.LeftPane.UpdateSelectedItems(Array.Empty<FileItemViewModel>());

        Assert.False(_mainViewModel.CanTransferToOtherPane);
    }

    [Fact]
    public async Task InitializeSettingsAsync_RestoresUndoStackFromJournal()
    {
        var journal = new FileUndoJournalStore(_fs, @"C:\journal\undo.jsonl");
        var opId = Guid.NewGuid();
        _fs.CreateVirtualFile(@"C:\right\test.txt", 100);

        await journal.AppendPendingEntryAsync(new PersistentUndoEntry(
            opId,
            1,
            JournalState.Pending,
            DateTime.UtcNow,
            TransferKind.Copy,
            Array.Empty<JournalItemEntry>(),
            OwnerPane: "Left"));

        await journal.AppendItemAsync(opId, new JournalItemEntry(
            TransferKind.Copy,
            @"C:\left\test.txt",
            @"C:\right\test.txt",
            100,
            DateTime.UtcNow,
            ItemOutcome.Copied));

        await journal.CommitEntryAsync(opId);

        var vm = new MainViewModel(
            _dirService,
            _iconService,
            _settingsService,
            _fs,
            journalStore: journal);

        await vm.InitializeSettingsAsync(new AppSettings());

        Assert.True(vm.LeftPane.UndoStack.CanUndo);
        Assert.Equal("Copy of 1 files", vm.LeftPane.UndoStack.NextUndoDescription);
    }

    [Fact]
    public async Task EnqueueMultipleTransfers_Concurrently_WithoutBlocking()
    {
        _fs.CreateVirtualFile(@"C:\left\doc1.txt", 100);
        _fs.CreateVirtualFile(@"C:\left\doc2.txt", 100);
        await _mainViewModel.LeftPane.NavigateAsync(@"C:\left");
        await _mainViewModel.RightPane.NavigateAsync(@"C:\right");

        _mainViewModel.SetActivePane(_mainViewModel.LeftPane);

        // Enqueue transfer 1 via EnqueueTransfer
        var job1 = _mainViewModel.EnqueueTransfer(
            TransferKind.Copy,
            new[] { @"C:\left\doc1.txt" },
            @"C:\right",
            _mainViewModel.LeftPane);

        // Enqueue transfer 2 immediately without waiting for job1
        var job2 = _mainViewModel.EnqueueTransfer(
            TransferKind.Copy,
            new[] { @"C:\left\doc2.txt" },
            @"C:\right",
            _mainViewModel.LeftPane);

        Assert.NotNull(job1);
        Assert.NotNull(job2);

        await Task.WhenAll(job1.Completion, job2.Completion);

        Assert.True(_fs.FileExists(@"C:\right\doc1.txt"));
        Assert.True(_fs.FileExists(@"C:\right\doc2.txt"));
    }

    [Fact]
    public async Task DropTransfer_FromOtherPane_AssignsUndoToSourcePane()
    {
        _fs.CreateVirtualFile(@"C:\left\dragged.txt", 100);
        await _mainViewModel.LeftPane.NavigateAsync(@"C:\left");
        await _mainViewModel.RightPane.NavigateAsync(@"C:\right");

        // Simulating drop originating from LeftPane onto RightPane
        var job = _mainViewModel.EnqueueTransfer(
            TransferKind.Move,
            new[] { @"C:\left\dragged.txt" },
            @"C:\right",
            _mainViewModel.LeftPane); // Source pane owns Undo

        Assert.NotNull(job);
        await _mainViewModel.WaitForJobCompletionAsync(job);

        Assert.True(_fs.FileExists(@"C:\right\dragged.txt"));
        Assert.False(_fs.FileExists(@"C:\left\dragged.txt"));
        Assert.True(_mainViewModel.LeftPane.UndoStack.CanUndo);
        Assert.False(_mainViewModel.RightPane.UndoStack.CanUndo);
    }

    [Fact]
    public async Task DropTransfer_FromExplorer_AssignsUndoToTargetPane()
    {
        _fs.CreateVirtualFile(@"D:\external\photo.png", 500);
        await _mainViewModel.LeftPane.NavigateAsync(@"C:\left");
        await _mainViewModel.RightPane.NavigateAsync(@"C:\right");

        // Simulating drop originating from external Explorer onto RightPane
        var job = _mainViewModel.EnqueueTransfer(
            TransferKind.Copy,
            new[] { @"D:\external\photo.png" },
            @"C:\right",
            _mainViewModel.RightPane); // Target pane owns Undo

        Assert.NotNull(job);
        await _mainViewModel.WaitForJobCompletionAsync(job);

        Assert.True(_fs.FileExists(@"C:\right\photo.png"));
        Assert.True(_mainViewModel.RightPane.UndoStack.CanUndo);
    }
}
