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
    ItemOutcome Outcome);

public record PersistentUndoEntry(
    Guid OperationId,
    int SchemaVersion,
    JournalState State,
    DateTime Timestamp,
    TransferKind Kind,
    IReadOnlyList<JournalItemEntry> Items);

/// <summary>
/// Redesigned interface-only contract for persistent write-ahead undo journaling (planned for v0.4).
/// </summary>
public interface IUndoJournalStore
{
    Task AppendPendingEntryAsync(PersistentUndoEntry entry, CancellationToken ct = default);
    Task CommitEntryAsync(Guid operationId, CancellationToken ct = default);
    Task MarkUndoneAsync(Guid operationId, CancellationToken ct = default);
    Task<IReadOnlyList<PersistentUndoEntry>> LoadEntriesAsync(CancellationToken ct = default);
    Task DeleteEntryAsync(Guid operationId, CancellationToken ct = default);
}

