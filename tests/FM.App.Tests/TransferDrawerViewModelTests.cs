using FM.App.Tests.Fakes;
using FM.App.ViewModels;
using FM.Core.FileSystem;
using FM.Core.Operations.Transfer;

namespace FM.App.Tests;

public class TransferDrawerViewModelTests
{
    private class BlockingEngine : ITransferEngine
    {
        public TaskCompletionSource Blocker { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<TransferResult> RunAsync(
            TransferRequest request,
            IProgress<TransferProgress>? progress,
            PauseGate? gate,
            CancellationToken ct)
        {
            await Blocker.Task;
            return new TransferResult(TransferState.Completed, Array.Empty<TransferItemResult>());
        }
    }

    private readonly InMemoryFileSystem _fs;
    private readonly TestTimeProvider _timeProvider;

    public TransferDrawerViewModelTests()
    {
        _fs = new InMemoryFileSystem();
        _timeProvider = new TestTimeProvider();
        _fs.CreateDirectory(@"C:\src");
        _fs.CreateDirectory(@"C:\dst");
        _fs.CreateVirtualFile(@"C:\src\f1.txt", 1000);
    }

    private TransferRequest CreateRequest() =>
        new(TransferKind.Copy, new[] { @"C:\src\f1.txt" }, @"C:\dst", new TransferOptions());

    [Fact]
    public void Drawer_StartsEmpty_AndExpandsOnFirstJob()
    {
        var engine = new BlockingEngine();
        var queue = new TransferQueue(engine, _fs, _timeProvider);
        var vm = new TransferDrawerViewModel(queue, _timeProvider, uiDispatcher: a => a());

        Assert.Empty(vm.Jobs);
        Assert.False(vm.HasJobs);
        Assert.False(vm.IsExpanded);

        // Enqueue job
        var job = queue.Enqueue(CreateRequest());

        Assert.Single(vm.Jobs);
        Assert.True(vm.HasJobs);
        Assert.True(vm.IsExpanded);
        Assert.Equal(job.Id, vm.Jobs[0].Id);
    }

    [Fact]
    public void ProgressChanged_CoalescesAndUpdatesJobViewModel()
    {
        var engine = new BlockingEngine();
        var queue = new TransferQueue(engine, _fs, _timeProvider);
        var vm = new TransferDrawerViewModel(queue, _timeProvider, uiDispatcher: a => a());

        var job = queue.Enqueue(CreateRequest());
        var jobVm = vm.Jobs[0];

        // Simulate progress update
        var progress = new TransferProgress(
            TransferPhase.Transferring,
            CurrentPath: @"C:\src\f1.txt",
            CurrentFileBytes: 500,
            CurrentFileTotal: 1000,
            OverallBytes: 500,
            OverallTotal: 1000,
            FilesDone: 1,
            FilesTotal: 2,
            BytesPerSecond: 1024 * 1024 * 5, // 5 MB/s
            Eta: TimeSpan.FromSeconds(10));

        jobVm.UpdateProgress(progress);

        Assert.Equal("f1.txt", jobVm.CurrentFile);
        Assert.Equal(50.0, jobVm.Percent);
        Assert.False(jobVm.IsIndeterminate);
        Assert.Contains("1 of 2", jobVm.ProgressText);
        Assert.Contains("50%", jobVm.ProgressText);
        Assert.Contains("5.0 MB/s", jobVm.ProgressText);
        Assert.Contains("00:10 left", jobVm.ProgressText);
    }

    [Fact]
    public async Task CanExecute_Matrix_MatchesSpecificationStrictly()
    {
        var engine = new BlockingEngine();
        var queue = new TransferQueue(engine, _fs, _timeProvider);
        var vm = new TransferDrawerViewModel(queue, _timeProvider, uiDispatcher: a => a());

        var job = queue.Enqueue(CreateRequest());
        var jobVm = vm.Jobs[0];

        // 1. Queued
        jobVm.UpdateState(TransferState.Queued);
        Assert.False(jobVm.PauseCommand.CanExecute(null));
        Assert.False(jobVm.ResumeCommand.CanExecute(null));
        Assert.True(jobVm.CancelCommand.CanExecute(null));

        // 2. Running
        jobVm.UpdateState(TransferState.Running);
        Assert.True(jobVm.PauseCommand.CanExecute(null));
        Assert.False(jobVm.ResumeCommand.CanExecute(null));
        Assert.True(jobVm.CancelCommand.CanExecute(null));

        // 3. Pausing
        jobVm.UpdateState(TransferState.Pausing);
        Assert.False(jobVm.PauseCommand.CanExecute(null));
        Assert.True(jobVm.ResumeCommand.CanExecute(null));
        Assert.True(jobVm.CancelCommand.CanExecute(null));

        // 4. Paused
        jobVm.UpdateState(TransferState.Paused);
        Assert.False(jobVm.PauseCommand.CanExecute(null));
        Assert.True(jobVm.ResumeCommand.CanExecute(null));
        Assert.True(jobVm.CancelCommand.CanExecute(null));

        // 5. Completed
        jobVm.UpdateState(TransferState.Completed);
        Assert.False(jobVm.PauseCommand.CanExecute(null));
        Assert.False(jobVm.ResumeCommand.CanExecute(null));
        Assert.False(jobVm.CancelCommand.CanExecute(null));

        // 6. Cancel command invocation disables itself
        var job2 = queue.Enqueue(CreateRequest());
        Assert.Equal(2, vm.Jobs.Count);
        var jobVm2 = vm.Jobs[1];
        jobVm2.UpdateState(TransferState.Running);
        Assert.True(jobVm2.CancelCommand.CanExecute(null));
        await jobVm2.CancelCommand.ExecuteAsync(null);
        Assert.False(jobVm2.CancelCommand.CanExecute(null));
        Assert.Equal("Cancelled", jobVm2.StateText);
    }

    [Fact]
    public void AutoRemove_TriggersViaQueueStateChange_After8Seconds()
    {
        var engine = new BlockingEngine();
        var queue = new TransferQueue(engine, _fs, _timeProvider);
        var vm = new TransferDrawerViewModel(queue, _timeProvider, uiDispatcher: a => a());

        var job = queue.Enqueue(CreateRequest());

        Assert.Single(vm.Jobs);

        // Notify state transition to Completed via event handler
        typeof(TransferDrawerViewModel)
            .GetMethod("OnQueueStateChanged", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(vm, new object[] { queue, new TransferStateChangedEventArgs(job, TransferState.Running, TransferState.Completed) });

        Assert.Single(vm.Jobs);
        Assert.Equal(TransferState.Completed, vm.Jobs[0].State);

        // Advance 7 seconds -> still present
        _timeProvider.Advance(TimeSpan.FromSeconds(7));
        Assert.Single(vm.Jobs);

        // Advance 2 more seconds (total 9s) -> removed!
        _timeProvider.Advance(TimeSpan.FromSeconds(2));
        Assert.Empty(vm.Jobs);
        Assert.False(vm.HasJobs);
        Assert.False(vm.IsExpanded);
    }

    [Fact]
    public void ClearFinishedCommand_RemovesOnlyFinishedJobs()
    {
        var engine = new BlockingEngine();
        var queue = new TransferQueue(engine, _fs, _timeProvider);
        var vm = new TransferDrawerViewModel(queue, _timeProvider, uiDispatcher: a => a());

        var job1 = queue.Enqueue(CreateRequest());
        var job2 = queue.Enqueue(CreateRequest());
        var job3 = queue.Enqueue(CreateRequest());
        var job4 = queue.Enqueue(CreateRequest());

        Assert.Equal(4, vm.Jobs.Count);

        vm.Jobs[0].UpdateState(TransferState.Running);
        vm.Jobs[1].UpdateState(TransferState.Completed);
        vm.Jobs[2].UpdateState(TransferState.Failed);
        vm.Jobs[3].UpdateState(TransferState.Cancelled);

        vm.ClearFinishedCommand.Execute(null);

        Assert.Single(vm.Jobs);
        Assert.Equal(job1.Id, vm.Jobs[0].Id);
        Assert.Equal(TransferState.Running, vm.Jobs[0].State);
    }
}
