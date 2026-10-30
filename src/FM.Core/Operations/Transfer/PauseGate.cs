namespace FM.Core.Operations.Transfer;

/// <summary>
/// Cooperative gate checked between chunks to allow pausing and resuming transfers without restarting files.
/// </summary>
public sealed class PauseGate
{
    private readonly object _lock = new();
    private TaskCompletionSource _resumeTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _isPaused;
    private bool _isPausing;
    private Action<TransferState>? _stateNotifier;

    public bool IsPaused
    {
        get { lock (_lock) return _isPaused; }
    }

    public bool IsPausing
    {
        get { lock (_lock) return _isPausing; }
    }

    public void SetStateNotifier(Action<TransferState> notifier)
    {
        lock (_lock)
        {
            _stateNotifier = notifier;
        }
    }

    public void Pause()
    {
        Action<TransferState>? notifier;
        lock (_lock)
        {
            if (_isPaused || _isPausing) return;
            _isPausing = true;
            _resumeTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            notifier = _stateNotifier;
        }
        notifier?.Invoke(TransferState.Pausing);
    }

    public void Resume()
    {
        Action<TransferState>? notifier;
        lock (_lock)
        {
            if (!_isPaused && !_isPausing) return;
            _isPaused = false;
            _isPausing = false;
            _resumeTcs.TrySetResult();
            notifier = _stateNotifier;
        }
        notifier?.Invoke(TransferState.Running);
    }

    public async Task WaitIfPausedAsync(CancellationToken ct = default)
    {
        Task waitTask;
        Action<TransferState>? notifyPaused = null;

        lock (_lock)
        {
            if (!_isPausing && !_isPaused) return;

            if (_isPausing)
            {
                _isPausing = false;
                _isPaused = true;
                notifyPaused = _stateNotifier;
            }

            waitTask = _resumeTcs.Task;
        }

        notifyPaused?.Invoke(TransferState.Paused);

        // Await resume or cancellation while paused
        await waitTask.WaitAsync(ct).ConfigureAwait(false);
    }
}

