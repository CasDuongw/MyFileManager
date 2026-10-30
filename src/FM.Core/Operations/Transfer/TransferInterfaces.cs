namespace FM.Core.Operations.Transfer;

public class TransferProgressEventArgs : EventArgs
{
    public ITransferJob Job { get; }
    public TransferProgress Progress { get; }

    public TransferProgressEventArgs(ITransferJob job, TransferProgress progress)
    {
        Job = job;
        Progress = progress;
    }
}

public class TransferStateChangedEventArgs : EventArgs
{
    public ITransferJob Job { get; }
    public TransferState OldState { get; }
    public TransferState NewState { get; }

    public TransferStateChangedEventArgs(ITransferJob job, TransferState oldState, TransferState newState)
    {
        Job = job;
        OldState = oldState;
        NewState = newState;
    }
}

public interface ITransferJob
{
    Guid Id { get; }
    TransferRequest Request { get; }
    TransferState State { get; }
    TransferProgress? LastProgress { get; }
    Task<TransferResult> Completion { get; }

    void Pause();
    void Resume();
    Task CancelAsync();
}

public interface ITransferQueue
{
    ITransferJob Enqueue(TransferRequest request);
    IReadOnlyList<ITransferJob> Snapshot();

    event EventHandler<TransferProgressEventArgs>? ProgressChanged;
    event EventHandler<TransferStateChangedEventArgs>? StateChanged;
}

public interface ITransferEngine
{
    Task<TransferResult> RunAsync(
        TransferRequest request,
        IProgress<TransferProgress>? progress,
        PauseGate? gate,
        CancellationToken ct);
}
