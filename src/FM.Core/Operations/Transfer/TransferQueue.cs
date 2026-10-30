using FM.Core.FileSystem;

namespace FM.Core.Operations.Transfer;

public sealed class TransferJob : ITransferJob
{
    private readonly TaskCompletionSource<TransferResult> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _cts = new();
    private readonly PauseGate _gate = new();

    public Guid Id { get; }
    public TransferRequest Request { get; }
    public string DestinationVolumeId { get; }

    public TransferState State { get; internal set; } = TransferState.Queued;
    public TransferProgress? LastProgress { get; internal set; }
    public Task<TransferResult> Completion => _tcs.Task;

    internal PauseGate Gate => _gate;
    internal CancellationToken CancellationToken => _cts.Token;
    internal TaskCompletionSource<TransferResult> Tcs => _tcs;

    public TransferJob(TransferRequest request, string destinationVolumeId)
    {
        Request = request;
        DestinationVolumeId = destinationVolumeId;
        Id = request.OperationId;
    }

    public void Pause() => _gate.Pause();
    public void Resume() => _gate.Resume();

    public Task CancelAsync()
    {
        _cts.Cancel();
        return Task.CompletedTask;
    }
}

/// <summary>
/// Thread-safe transfer queue enforcing serial lanes per destination volume,
/// up to 2 parallel jobs across distinct volumes, and throttled progress reporting.
/// </summary>
public sealed class TransferQueue : ITransferQueue
{
    private readonly ITransferEngine _engine;
    private readonly IFileSystem _fs;
    private readonly TimeProvider _timeProvider;
    private readonly int _maxParallelJobs;

    private readonly object _lock = new();
    private readonly List<TransferJob> _jobs = new();
    private readonly HashSet<string> _activeVolumeIds = new(StringComparer.OrdinalIgnoreCase);
    private int _runningJobsCount;

    public event EventHandler<TransferProgressEventArgs>? ProgressChanged;
    public event EventHandler<TransferStateChangedEventArgs>? StateChanged;

    public TransferQueue(
        ITransferEngine engine,
        IFileSystem fs,
        TimeProvider? timeProvider = null,
        int maxParallelJobs = 2)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _maxParallelJobs = maxParallelJobs;
    }

    public TransferQueue(
        IFileSystem fs,
        TimeProvider? timeProvider = null,
        int maxParallelJobs = 2)
        : this(new TransferEngine(fs), fs, timeProvider, maxParallelJobs)
    {
    }

    public TransferQueue(
        IFileSystem fs,
        IStagingBackupStore? stager,
        IUndoJournalStore? journal,
        TimeProvider? timeProvider = null,
        int maxParallelJobs = 2)
        : this(new TransferEngine(fs, stager, journal, timeProvider), fs, timeProvider, maxParallelJobs)
    {
    }

    public ITransferJob Enqueue(TransferRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        string volumeId = _fs.GetVolumeId(request.DestinationDirectory);
        var job = new TransferJob(request, volumeId);

        // Gate state notifications
        job.Gate.SetStateNotifier(newState =>
        {
            TransferState oldState;
            lock (_lock)
            {
                oldState = job.State;
                job.State = newState;
            }
            StateChanged?.Invoke(this, new TransferStateChangedEventArgs(job, oldState, newState));
        });

        List<(TransferJob Job, TransferState OldState, TransferState NewState)> stateChanges = new();

        lock (_lock)
        {
            _jobs.Add(job);
            ProcessQueue_Locked(stateChanges);
        }

        // Fire initial state changes outside lock
        foreach (var change in stateChanges)
        {
            StateChanged?.Invoke(this, new TransferStateChangedEventArgs(change.Job, change.OldState, change.NewState));
        }

        return job;
    }

    public IReadOnlyList<ITransferJob> Snapshot()
    {
        lock (_lock)
        {
            return _jobs.Cast<ITransferJob>().ToList();
        }
    }

    private void ProcessQueue_Locked(List<(TransferJob Job, TransferState OldState, TransferState NewState)> stateChanges)
    {
        while (_runningJobsCount < _maxParallelJobs)
        {
            // Find next queued job whose destination volume is not currently active
            var eligibleJob = _jobs.FirstOrDefault(j =>
                j.State == TransferState.Queued && !_activeVolumeIds.Contains(j.DestinationVolumeId));

            if (eligibleJob == null) break;

            _activeVolumeIds.Add(eligibleJob.DestinationVolumeId);
            _runningJobsCount++;

            var oldState = eligibleJob.State;
            eligibleJob.State = TransferState.Running;
            stateChanges.Add((eligibleJob, oldState, TransferState.Running));

            _ = Task.Run(() => ExecuteJobAsync(eligibleJob));
        }
    }

    private async Task ExecuteJobAsync(TransferJob job)
    {
        long lastProgressTimestamp = 0;
        long minIntervalTicks = (long)(_timeProvider.TimestampFrequency * 0.1); // 100 ms = max 10 events/sec

        var progress = new Progress<TransferProgress>(p =>
        {
            bool isFinal = (p.Phase == TransferPhase.Finalizing) ||
                           (p.OverallTotal.HasValue && p.OverallBytes >= p.OverallTotal.Value);

            long now = _timeProvider.GetTimestamp();
            if (isFinal || (now - lastProgressTimestamp) >= minIntervalTicks)
            {
                lastProgressTimestamp = now;
                job.LastProgress = p;
                ProgressChanged?.Invoke(this, new TransferProgressEventArgs(job, p));
            }
        });

        TransferResult result;
        try
        {
            result = await _engine.RunAsync(job.Request, progress, job.Gate, job.CancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            result = new TransferResult(TransferState.Cancelled, Array.Empty<TransferItemResult>());
        }
        catch (Exception ex)
        {
            var errItem = new TransferItemResult(string.Empty, null, false, ItemOutcome.Failed, 0, null, ex.Message);
            result = new TransferResult(TransferState.Failed, new[] { errItem });
        }

        job.Tcs.TrySetResult(result);

        List<(TransferJob Job, TransferState OldState, TransferState NewState)> stateChanges = new();

        lock (_lock)
        {
            _activeVolumeIds.Remove(job.DestinationVolumeId);
            _runningJobsCount--;

            var oldState = job.State;
            job.State = result.FinalState;
            stateChanges.Add((job, oldState, result.FinalState));

            ProcessQueue_Locked(stateChanges);
        }

        foreach (var change in stateChanges)
        {
            StateChanged?.Invoke(this, new TransferStateChangedEventArgs(change.Job, change.OldState, change.NewState));
        }
    }
}
