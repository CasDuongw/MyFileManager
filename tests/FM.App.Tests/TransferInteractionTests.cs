using System.IO;
using FM.App.Services;
using FM.App.Tests.Fakes;
using FM.App.ViewModels;
using FM.Core.FileSystem;
using FM.Core.Operations.Transfer;

namespace FM.App.Tests;

public class TransferInteractionTests
{
    private readonly InMemoryFileSystem _fs;
    private readonly FileSystemBackedDirectoryService _dirService;
    private readonly FakeIconService _iconService;
    private readonly FakeSettingsService _settingsService;
    private readonly FakeTransferDialogService _dialogService;
    private readonly MainViewModel _mainViewModel;

    public TransferInteractionTests()
    {
        _fs = new InMemoryFileSystem();
        _dirService = new FileSystemBackedDirectoryService(_fs);
        _iconService = new FakeIconService();
        _settingsService = new FakeSettingsService();
        _dialogService = new FakeTransferDialogService();

        _fs.CreateDirectory(@"C:\left");
        _fs.CreateDirectory(@"C:\right");

        _mainViewModel = new MainViewModel(
            _dirService,
            _iconService,
            _settingsService,
            _fs,
            transferDialogService: _dialogService);
    }

    [Fact]
    public async Task Conflict_KeepBoth_CreatesRenamedFile()
    {
        _fs.CreateVirtualFile(@"C:\left\doc.txt", 100);
        _fs.CreateVirtualFile(@"C:\right\doc.txt", 200);

        await _mainViewModel.LeftPane.NavigateAsync(@"C:\left");
        await _mainViewModel.RightPane.NavigateAsync(@"C:\right");

        _mainViewModel.SetActivePane(_mainViewModel.LeftPane);
        var item = _mainViewModel.LeftPane.Items.First(i => i.Name == "doc.txt");
        _mainViewModel.LeftPane.UpdateSelectedItems(new[] { item });

        _dialogService.ConflictHandler = (info, ct) =>
        {
            Assert.Equal(@"C:\left\doc.txt", info.SourcePath);
            Assert.Equal(@"C:\right\doc.txt", info.ExistingPath);
            return Task.FromResult(new ConflictDecision(ConflictAction.KeepBoth));
        };

        await _mainViewModel.CopyToOtherPaneCommand.ExecuteAsync(null);

        Assert.Single(_dialogService.ConflictCalls);
        Assert.True(_fs.FileExists(@"C:\right\doc.txt"));
        Assert.True(_fs.FileExists(@"C:\right\doc (2).txt"));
        Assert.True(_mainViewModel.LeftPane.UndoStack.CanUndo);
    }

    [Fact]
    public async Task Conflict_Overwrite_ReplacesFile_DoesNotPushUndo()
    {
        _fs.CreateVirtualFile(@"C:\left\doc.txt", 100);
        _fs.CreateVirtualFile(@"C:\right\doc.txt", 200);

        await _mainViewModel.LeftPane.NavigateAsync(@"C:\left");
        await _mainViewModel.RightPane.NavigateAsync(@"C:\right");

        _mainViewModel.SetActivePane(_mainViewModel.LeftPane);
        var item = _mainViewModel.LeftPane.Items.First(i => i.Name == "doc.txt");
        _mainViewModel.LeftPane.UpdateSelectedItems(new[] { item });

        _dialogService.ConflictHandler = (info, ct) =>
            Task.FromResult(new ConflictDecision(ConflictAction.Overwrite));

        await _mainViewModel.CopyToOtherPaneCommand.ExecuteAsync(null);

        Assert.Single(_dialogService.ConflictCalls);
        Assert.True(_fs.FileExists(@"C:\right\doc.txt"));
        Assert.Equal(100, _fs.GetInfo(@"C:\right\doc.txt")?.Size);

        // Crucial requirement: When items are replaced, CanUndo must be false (no undo pushed)
        Assert.False(_mainViewModel.LeftPane.UndoStack.CanUndo);
    }

    [Fact]
    public async Task Conflict_OverwriteIfNewer_OverwritesWhenSourceIsNewer()
    {
        var now = DateTime.UtcNow;
        _fs.CreateVirtualFile(@"C:\left\doc.txt", 100);
        _fs.SetModified(@"C:\left\doc.txt", now);

        _fs.CreateVirtualFile(@"C:\right\doc.txt", 200);
        _fs.SetModified(@"C:\right\doc.txt", now.AddHours(-2));

        await _mainViewModel.LeftPane.NavigateAsync(@"C:\left");
        await _mainViewModel.RightPane.NavigateAsync(@"C:\right");

        _mainViewModel.SetActivePane(_mainViewModel.LeftPane);
        var item = _mainViewModel.LeftPane.Items.First(i => i.Name == "doc.txt");
        _mainViewModel.LeftPane.UpdateSelectedItems(new[] { item });

        _dialogService.ConflictHandler = (info, ct) =>
            Task.FromResult(new ConflictDecision(ConflictAction.OverwriteIfNewer));

        await _mainViewModel.CopyToOtherPaneCommand.ExecuteAsync(null);

        Assert.Single(_dialogService.ConflictCalls);
        Assert.Equal(100, _fs.GetInfo(@"C:\right\doc.txt")?.Size);
    }

    [Fact]
    public async Task Conflict_OverwriteIfNewer_SkipsWhenSourceIsOlder()
    {
        var now = DateTime.UtcNow;
        _fs.CreateVirtualFile(@"C:\left\doc.txt", 100);
        _fs.SetModified(@"C:\left\doc.txt", now.AddHours(-2)); // older

        _fs.CreateVirtualFile(@"C:\right\doc.txt", 200);
        _fs.SetModified(@"C:\right\doc.txt", now); // newer

        await _mainViewModel.LeftPane.NavigateAsync(@"C:\left");
        await _mainViewModel.RightPane.NavigateAsync(@"C:\right");

        _mainViewModel.SetActivePane(_mainViewModel.LeftPane);
        var item = _mainViewModel.LeftPane.Items.First(i => i.Name == "doc.txt");
        _mainViewModel.LeftPane.UpdateSelectedItems(new[] { item });

        _dialogService.ConflictHandler = (info, ct) =>
            Task.FromResult(new ConflictDecision(ConflictAction.OverwriteIfNewer));

        await _mainViewModel.CopyToOtherPaneCommand.ExecuteAsync(null);

        Assert.Single(_dialogService.ConflictCalls);
        // Destination preserved
        Assert.Equal(200, _fs.GetInfo(@"C:\right\doc.txt")?.Size);
    }

    [Fact]
    public async Task Conflict_Skip_SkipsFile()
    {
        _fs.CreateVirtualFile(@"C:\left\doc.txt", 100);
        _fs.CreateVirtualFile(@"C:\right\doc.txt", 200);

        await _mainViewModel.LeftPane.NavigateAsync(@"C:\left");
        await _mainViewModel.RightPane.NavigateAsync(@"C:\right");

        _mainViewModel.SetActivePane(_mainViewModel.LeftPane);
        var item = _mainViewModel.LeftPane.Items.First(i => i.Name == "doc.txt");
        _mainViewModel.LeftPane.UpdateSelectedItems(new[] { item });

        _dialogService.ConflictHandler = (info, ct) =>
            Task.FromResult(new ConflictDecision(ConflictAction.Skip));

        await _mainViewModel.CopyToOtherPaneCommand.ExecuteAsync(null);

        Assert.Single(_dialogService.ConflictCalls);
        Assert.Equal(200, _fs.GetInfo(@"C:\right\doc.txt")?.Size);
    }

    [Fact]
    public async Task Conflict_Cancel_CancelsEntireTransfer()
    {
        _fs.CreateVirtualFile(@"C:\left\doc.txt", 100);
        _fs.CreateVirtualFile(@"C:\right\doc.txt", 200);

        await _mainViewModel.LeftPane.NavigateAsync(@"C:\left");
        await _mainViewModel.RightPane.NavigateAsync(@"C:\right");

        _mainViewModel.SetActivePane(_mainViewModel.LeftPane);
        var item = _mainViewModel.LeftPane.Items.First(i => i.Name == "doc.txt");
        _mainViewModel.LeftPane.UpdateSelectedItems(new[] { item });

        _dialogService.ConflictHandler = (info, ct) =>
            Task.FromResult(new ConflictDecision(ConflictAction.Cancel));

        await _mainViewModel.CopyToOtherPaneCommand.ExecuteAsync(null);

        Assert.Single(_dialogService.ConflictCalls);
        Assert.Contains("cancelled", _mainViewModel.LeftPane.SelectionStatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Conflict_ApplyToAll_AsksOnlyOnceForMultipleConflicts()
    {
        _fs.CreateVirtualFile(@"C:\left\f1.txt", 10);
        _fs.CreateVirtualFile(@"C:\left\f2.txt", 20);
        _fs.CreateVirtualFile(@"C:\left\f3.txt", 30);

        _fs.CreateVirtualFile(@"C:\right\f1.txt", 100);
        _fs.CreateVirtualFile(@"C:\right\f2.txt", 200);
        _fs.CreateVirtualFile(@"C:\right\f3.txt", 300);

        await _mainViewModel.LeftPane.NavigateAsync(@"C:\left");
        await _mainViewModel.RightPane.NavigateAsync(@"C:\right");

        _mainViewModel.SetActivePane(_mainViewModel.LeftPane);
        _mainViewModel.LeftPane.UpdateSelectedItems(_mainViewModel.LeftPane.Items.ToList());

        _dialogService.ConflictHandler = (info, ct) =>
            Task.FromResult(new ConflictDecision(ConflictAction.KeepBoth, ApplyToAll: true));

        await _mainViewModel.CopyToOtherPaneCommand.ExecuteAsync(null);

        // Dialog should be queried exactly once because ApplyToAll is true
        Assert.Single(_dialogService.ConflictCalls);
        Assert.True(_fs.FileExists(@"C:\right\f1 (2).txt"));
        Assert.True(_fs.FileExists(@"C:\right\f2 (2).txt"));
        Assert.True(_fs.FileExists(@"C:\right\f3 (2).txt"));
    }

    [Fact]
    public async Task StatusText_ShowsWaitingForDecision_WhileDialogIsOpen()
    {
        _fs.CreateVirtualFile(@"C:\left\doc.txt", 100);
        _fs.CreateVirtualFile(@"C:\right\doc.txt", 200);

        await _mainViewModel.LeftPane.NavigateAsync(@"C:\left");
        await _mainViewModel.RightPane.NavigateAsync(@"C:\right");

        _mainViewModel.SetActivePane(_mainViewModel.LeftPane);
        var item = _mainViewModel.LeftPane.Items.First(i => i.Name == "doc.txt");
        _mainViewModel.LeftPane.UpdateSelectedItems(new[] { item });

        string? statusDuringDialog = null;

        _dialogService.ConflictHandler = (info, ct) =>
        {
            statusDuringDialog = _mainViewModel.ActivePane.SelectionStatusText;
            return Task.FromResult(new ConflictDecision(ConflictAction.KeepBoth));
        };

        await _mainViewModel.CopyToOtherPaneCommand.ExecuteAsync(null);

        Assert.Equal("Waiting for your decision...", statusDuringDialog);
    }

    [Fact]
    public async Task Failure_Retry_RetriesAndSucceeds()
    {
        _fs.CreateVirtualFile(@"C:\left\doc.txt", 100);

        int attempts = 0;
        _fs.InjectFailure(@"C:\left\doc.txt", 0, new IOException("Simulated disk error"), count: 10);

        await _mainViewModel.LeftPane.NavigateAsync(@"C:\left");
        await _mainViewModel.RightPane.NavigateAsync(@"C:\right");

        _mainViewModel.SetActivePane(_mainViewModel.LeftPane);
        var item = _mainViewModel.LeftPane.Items.First(i => i.Name == "doc.txt");
        _mainViewModel.LeftPane.UpdateSelectedItems(new[] { item });

        _dialogService.FailureHandler = (info, ct) =>
        {
            attempts++;
            // Clear injected failure on retry
            _fs.ClearFailures();
            return Task.FromResult(new FailureDecision(FailureAction.Retry));
        };

        await _mainViewModel.CopyToOtherPaneCommand.ExecuteAsync(null);

        Assert.Equal(1, attempts);
        Assert.True(_fs.FileExists(@"C:\right\doc.txt"));
    }

    [Fact]
    public async Task Failure_Skip_SkipsFailedFile()
    {
        _fs.CreateVirtualFile(@"C:\left\doc.txt", 100);
        _fs.InjectFailure(@"C:\left\doc.txt", 0, new IOException("Simulated disk error"), count: 10);

        await _mainViewModel.LeftPane.NavigateAsync(@"C:\left");
        await _mainViewModel.RightPane.NavigateAsync(@"C:\right");

        _mainViewModel.SetActivePane(_mainViewModel.LeftPane);
        var item = _mainViewModel.LeftPane.Items.First(i => i.Name == "doc.txt");
        _mainViewModel.LeftPane.UpdateSelectedItems(new[] { item });

        _dialogService.FailureHandler = (info, ct) =>
            Task.FromResult(new FailureDecision(FailureAction.Skip));

        await _mainViewModel.CopyToOtherPaneCommand.ExecuteAsync(null);

        Assert.Single(_dialogService.FailureCalls);
        Assert.Contains("Completed with warnings", _mainViewModel.ActivePane.SelectionStatusText);
    }

    [Fact]
    public async Task Failure_Abort_AbortsTransfer()
    {
        _fs.CreateVirtualFile(@"C:\left\doc.txt", 100);
        _fs.InjectFailure(@"C:\left\doc.txt", 0, new IOException("Simulated disk error"), count: 10);

        await _mainViewModel.LeftPane.NavigateAsync(@"C:\left");
        await _mainViewModel.RightPane.NavigateAsync(@"C:\right");

        _mainViewModel.SetActivePane(_mainViewModel.LeftPane);
        var item = _mainViewModel.LeftPane.Items.First(i => i.Name == "doc.txt");
        _mainViewModel.LeftPane.UpdateSelectedItems(new[] { item });

        _dialogService.FailureHandler = (info, ct) =>
            Task.FromResult(new FailureDecision(FailureAction.Abort));

        await _mainViewModel.CopyToOtherPaneCommand.ExecuteAsync(null);

        Assert.Single(_dialogService.FailureCalls);
    }

    [Fact]
    public async Task CancellationWhileWaitingForDialog_TerminatesWithoutHanging()
    {
        _fs.CreateVirtualFile(@"C:\left\doc.txt", 100);
        _fs.CreateVirtualFile(@"C:\right\doc.txt", 200);

        var queue = new TransferQueue(_fs);
        var cts = new CancellationTokenSource();

        var dialogEnteredTcs = new TaskCompletionSource<bool>();

        _dialogService.ConflictHandler = async (info, ct) =>
        {
            dialogEnteredTcs.TrySetResult(true);
            // Simulate dialog staying open until cancelled
            var tcs = new TaskCompletionSource<ConflictDecision>();
            using (ct.Register(() => tcs.TrySetResult(new ConflictDecision(ConflictAction.Cancel))))
            {
                return await tcs.Task;
            }
        };

        var request = new TransferRequest(
            TransferKind.Copy,
            new[] { @"C:\left\doc.txt" },
            @"C:\right",
            new TransferOptions(DefaultConflict: ConflictAction.KeepBoth),
            new WpfTransferInteraction(_dialogService));

        var job = queue.Enqueue(request);

        // Wait until dialog was entered
        await dialogEnteredTcs.Task;

        // Cancel job while dialog is open
        await job.CancelAsync();

        // Complete within timeout without hanging
        var completedTask = await Task.WhenAny(job.Completion, Task.Delay(2000));
        Assert.Same(job.Completion, completedTask);

        var result = await job.Completion;
        Assert.Equal(TransferState.Cancelled, result.FinalState);
    }
}
