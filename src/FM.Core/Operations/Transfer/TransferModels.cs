namespace FM.Core.Operations.Transfer;

public enum TransferKind
{
    Copy,
    Move
}

public enum TransferPhase
{
    Validating,
    Scanning,
    Transferring,
    Verifying,
    Finalizing
}

public enum TransferState
{
    Queued,
    Running,
    Pausing,
    Paused,
    Completed,
    CompletedWithErrors,
    Failed,
    Cancelled
}

public enum ConflictAction
{
    Overwrite,
    OverwriteIfNewer,
    KeepBoth,
    Skip,
    Cancel
}

public enum FailureAction
{
    Retry,
    Skip,
    Abort
}

public record TransferOptions(
    ConflictAction DefaultConflict = ConflictAction.KeepBoth,
    bool VerifyAfterCopy = true,
    bool PreserveTimestamps = true,
    int BufferSize = 1024 * 1024,
    int MaxRetries = 2,
    long? MaxBytesPerSecond = null);

public record TransferRequest(
    TransferKind Kind,
    IReadOnlyList<string> Sources,
    string DestinationDirectory,
    TransferOptions Options,
    ITransferInteraction? Interaction = null);

public record ConflictInfo(
    string SourcePath,
    long SourceSize,
    DateTime SourceModified,
    string ExistingPath,
    long ExistingSize,
    DateTime ExistingModified);

public record ConflictDecision(
    ConflictAction Action,
    bool ApplyToAll = false);

public record FailureInfo(
    string Path,
    Exception Error,
    int Attempt);

public record FailureDecision(
    FailureAction Action,
    bool ApplyToAll = false);

public interface ITransferInteraction
{
    ValueTask<ConflictDecision> OnConflictAsync(ConflictInfo info, CancellationToken ct);
    ValueTask<FailureDecision> OnFailureAsync(FailureInfo info, CancellationToken ct);
}

public record TransferProgress(
    TransferPhase Phase,
    string? CurrentPath,
    long CurrentFileBytes,
    long CurrentFileTotal,
    long OverallBytes,
    long? OverallTotal,
    int FilesDone,
    int? FilesTotal,
    double BytesPerSecond,
    TimeSpan? Eta);

public enum ItemOutcome
{
    Copied,
    Moved,
    Replaced,
    KeptBoth,
    Skipped,
    Failed
}

public record TransferItemResult(
    string SourcePath,
    string? DestinationPath,
    bool IsDirectory,
    ItemOutcome Outcome,
    long Size,
    DateTime? ModifiedAfter,
    string? Error);

public record TransferResult(
    TransferState FinalState,
    IReadOnlyList<TransferItemResult> Items);

