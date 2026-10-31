namespace FM.Core.Operations.Transfer;

public enum JournalState
{
    Pending,
    Committed,
    Undone,
    Abandoned
}

public record JournalItemEntry(
    TransferKind Kind,
    string SourcePath,
    string? DestinationPath,
    long Size,
    DateTime? Modified,
    ItemOutcome Outcome,
    bool IsDirectory = false,
    string? BackupPath = null);

public record JournalIntentEntry(
    string SourcePath,
    string? DestinationPath,
    string? PlannedBackupPath = null,
    string? TempPath = null);

public record PersistentUndoEntry(
    Guid OperationId,
    int SchemaVersion,
    JournalState State,
    DateTime Timestamp,
    TransferKind Kind,
    IReadOnlyList<JournalItemEntry> Items,
    string? OwnerPane = null,
    IReadOnlyList<JournalIntentEntry>? Intents = null);

/// <summary>
/// Persistent write-ahead undo journal contract enabling crash-resilient multi-session Undo.
/// </summary>
public interface IUndoJournalStore
{
    Task AppendPendingEntryAsync(PersistentUndoEntry entry, CancellationToken ct = default);
    Task AppendIntentAsync(Guid operationId, JournalIntentEntry intent, CancellationToken ct = default);
    Task AppendItemAsync(Guid operationId, JournalItemEntry item, CancellationToken ct = default);
    Task CommitEntryAsync(Guid operationId, CancellationToken ct = default);
    Task MarkUndoneAsync(Guid operationId, CancellationToken ct = default);
    Task AbandonEntryAsync(Guid operationId, CancellationToken ct = default);
    Task<IReadOnlyList<PersistentUndoEntry>> LoadEntriesAsync(CancellationToken ct = default);
    Task DeleteEntryAsync(Guid operationId, CancellationToken ct = default);
}
