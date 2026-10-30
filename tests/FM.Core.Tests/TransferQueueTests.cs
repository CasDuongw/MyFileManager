using FM.Core.FileSystem;
using FM.Core.Operations.Transfer;

namespace FM.Core.Tests;

public class TransferQueueTests
{
    private class FakeEngine : ITransferEngine
    {
        public Func<TransferRequest, Task<TransferResult>>? OnRun { get; set; }

        public Task<TransferResult> RunAsync(
            TransferRequest request,
            IProgress<TransferProgress>? progress,
            PauseGate? gate,
            CancellationToken ct)
        {
            if (OnRun != null) return OnRun(request);
            return Task.FromResult(new TransferResult(TransferState.Completed, Array.Empty<TransferItemResult>()));
        }
    }

    [Fact]
    public async Task SameVolume_IsSerialized_AndDistinctVolumes_RunInParallel()
    {
        var fs = new InMemoryFileSystem();
        var engine = new FakeEngine();
        var queue = new TransferQueue(engine, fs, maxParallelJobs: 2);

        var c1Started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var c1Blocker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var d1Started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var d1Blocker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var c2Started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var c2Blocker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        engine.OnRun = async req =>
        {
            if (req.DestinationDirectory.StartsWith(@"C:\Dest1"))
            {
                c1Started.TrySetResult();
                await c1Blocker.Task;
            }
            else if (req.DestinationDirectory.StartsWith(@"D:\Dest1"))
            {
                d1Started.TrySetResult();
                await d1Blocker.Task;
            }
            else if (req.DestinationDirectory.StartsWith(@"C:\Dest2"))
            {
                c2Started.TrySetResult();
                await c2Blocker.Task;
            }

            return new TransferResult(TransferState.Completed, Array.Empty<TransferItemResult>());
        };

        // Enqueue C1 and D1 (different volumes -> run in parallel!)
        var jobC1 = queue.Enqueue(new TransferRequest(TransferKind.Copy, new[] { @"C:\a.txt" }, @"C:\Dest1", new TransferOptions()));
        var jobD1 = queue.Enqueue(new TransferRequest(TransferKind.Copy, new[] { @"C:\b.txt" }, @"D:\Dest1", new TransferOptions()));

        // Enqueue C2 (same volume as C1 -> must wait for C1 to complete!)
        var jobC2 = queue.Enqueue(new TransferRequest(TransferKind.Copy, new[] { @"C:\c.txt" }, @"C:\Dest2", new TransferOptions()));

        // Both C1 and D1 start concurrently
        await Task.WhenAll(c1Started.Task, d1Started.Task);
        Assert.Equal(TransferState.Running, jobC1.State);
        Assert.Equal(TransferState.Running, jobD1.State);

        // C2 is queued and NOT started yet
        Assert.Equal(TransferState.Queued, jobC2.State);
        Assert.False(c2Started.Task.IsCompleted);

        // Snapshot is thread safe and accurate
        var snapshot = queue.Snapshot();
        Assert.Equal(3, snapshot.Count);

        // Complete C1 -> unlocks volume C lane -> C2 starts!
        c1Blocker.TrySetResult();
        await c2Started.Task;
        Assert.Equal(TransferState.Running, jobC2.State);

        // Complete D1 and C2
        d1Blocker.TrySetResult();
        c2Blocker.TrySetResult();

        await Task.WhenAll(jobC1.Completion, jobD1.Completion, jobC2.Completion);

        Assert.Equal(TransferState.Completed, jobC1.State);
        Assert.Equal(TransferState.Completed, jobD1.State);
        Assert.Equal(TransferState.Completed, jobC2.State);
    }
}
