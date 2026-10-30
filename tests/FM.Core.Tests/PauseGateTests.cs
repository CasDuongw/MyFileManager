using FM.Core.FileSystem;
using FM.Core.Operations.Transfer;

namespace FM.Core.Tests;

public class SyncProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;
    public SyncProgress(Action<T> handler) => _handler = handler;
    public void Report(T value) => _handler(value);
}

public class PauseGateTests
{
    [Fact]
    public async Task PauseAndResume_ContinuesFromSameByteOffset_WithoutRestart()
    {
        var fs = new InMemoryFileSystem();
        long fileSize = 20 * 1024 * 1024; // 20 MB
        fs.CreateVirtualFile(@"C:\source\movie.mp4", fileSize);
        fs.CreateDirectory(@"D:\dest");

        var engine = new TransferEngine(fs);
        var gate = new PauseGate();

        var pausedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stateChanges = new List<TransferState>();

        gate.SetStateNotifier(state =>
        {
            stateChanges.Add(state);
            if (state == TransferState.Paused)
            {
                pausedTcs.TrySetResult();
            }
        });

        long bytesAtPause = 0;
        bool hasPaused = false;
        var progressList = new List<TransferProgress>();

        var progress = new SyncProgress<TransferProgress>(p =>
        {
            progressList.Add(p);
            if (!hasPaused && p.CurrentFileBytes >= 5 * 1024 * 1024)
            {
                hasPaused = true;
                bytesAtPause = p.CurrentFileBytes;
                gate.Pause();
            }
        });

        var request = new TransferRequest(
            TransferKind.Copy,
            new[] { @"C:\source\movie.mp4" },
            @"D:\dest",
            new TransferOptions(BufferSize: 1024 * 1024, VerifyAfterCopy: false));

        var copyTask = engine.RunAsync(request, progress, gate, CancellationToken.None);

        // Await worker entering Paused state
        await pausedTcs.Task;

        Assert.True(gate.IsPaused);
        Assert.Contains(TransferState.Pausing, stateChanges);
        Assert.Contains(TransferState.Paused, stateChanges);

        // Capture progress count while paused
        int countWhilePaused = progressList.Count;

        // Resume execution
        gate.Resume();
        Assert.False(gate.IsPaused);

        var result = await copyTask;

        Assert.Equal(TransferState.Completed, result.FinalState);
        Assert.True(fs.FileExists(@"D:\dest\movie.mp4"));

        // Verify progress continued monotonically from bytesAtPause without restarting from 0
        var postResumeReports = progressList.Skip(countWhilePaused).ToList();
        Assert.NotEmpty(postResumeReports);
        Assert.True(postResumeReports[0].CurrentFileBytes >= bytesAtPause);
    }

    [Fact]
    public async Task CancelWhilePaused_WorksImmediatelyAndDeletesTempFile()
    {
        var fs = new InMemoryFileSystem();
        long fileSize = 20 * 1024 * 1024;
        fs.CreateVirtualFile(@"C:\source\data.bin", fileSize);
        fs.CreateDirectory(@"D:\dest");

        var engine = new TransferEngine(fs);
        var gate = new PauseGate();
        using var cts = new CancellationTokenSource();

        var pausedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        gate.SetStateNotifier(state =>
        {
            if (state == TransferState.Paused)
            {
                pausedTcs.TrySetResult();
            }
        });

        bool hasPaused = false;
        var progress = new SyncProgress<TransferProgress>(p =>
        {
            if (!hasPaused && p.CurrentFileBytes >= 2 * 1024 * 1024)
            {
                hasPaused = true;
                gate.Pause();
            }
        });

        var request = new TransferRequest(
            TransferKind.Copy,
            new[] { @"C:\source\data.bin" },
            @"D:\dest",
            new TransferOptions(BufferSize: 1024 * 1024));

        var copyTask = engine.RunAsync(request, progress, gate, cts.Token);

        // Wait until paused
        await pausedTcs.Task;
        Assert.True(gate.IsPaused);

        // Cancel while paused
        cts.Cancel();

        var result = await copyTask;

        Assert.Equal(TransferState.Cancelled, result.FinalState);
        // Temp file is cleaned up
        Assert.False(fs.FileExists(@"D:\dest\data.bin.fmpart"));
        Assert.False(fs.FileExists(@"D:\dest\data.bin"));
    }
}
