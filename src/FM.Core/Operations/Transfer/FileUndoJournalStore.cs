using System.IO;
using System.Text;
using FM.Core.FileSystem;

namespace FM.Core.Operations.Transfer;

/// <summary>
/// Production implementation of <see cref="IUndoJournalStore"/> utilizing an append-only JSONL write-ahead log.
/// Guarantees thread-safe writes across concurrent jobs, atomic recovery of pending operations, and compaction.
/// Degrades gracefully to in-memory tracking if file locking or I/O failure occurs without disrupting transfers.
/// </summary>
public sealed class FileUndoJournalStore : IUndoJournalStore
{
    private readonly IFileSystem _fs;
    private readonly string _journalPath;
    private readonly TimeProvider _timeProvider;
    private readonly object _lock = new();

    private bool _isDegraded;
    private bool _hasWarnedDegraded;
    private readonly Dictionary<Guid, (PersistentUndoEntry Entry, List<JournalItemEntry> Items)> _inMemoryEntries = new();

    public Action<string>? WarningHandler { get; set; }

    public FileUndoJournalStore(IFileSystem fs, string journalPath, TimeProvider? timeProvider = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _journalPath = string.IsNullOrWhiteSpace(journalPath)
            ? throw new ArgumentException("Journal path cannot be empty.", nameof(journalPath))
            : journalPath;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task AppendPendingEntryAsync(PersistentUndoEntry entry, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var record = new JournalRecordDto(
            Version: JournalCodec.CurrentSchemaVersion,
            Type: "begin",
            OperationId: entry.OperationId,
            Kind: entry.Kind,
            Timestamp: entry.Timestamp,
            OwnerPane: entry.OwnerPane);

        lock (_lock)
        {
            _inMemoryEntries[entry.OperationId] = (entry, new List<JournalItemEntry>());
            WriteRecord_Locked(record, flush: true);
        }

        return Task.CompletedTask;
    }

    public Task AppendIntentAsync(Guid operationId, JournalIntentEntry intent, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var intentDto = new JournalIntentDto(
            intent.SourcePath,
            intent.DestinationPath,
            intent.PlannedBackupPath,
            intent.TempPath);

        var record = new JournalRecordDto(
            Version: JournalCodec.CurrentSchemaVersion,
            Type: "intent",
            OperationId: operationId,
            Intent: intentDto);

        lock (_lock)
        {
            // True write-ahead intent must flush immediately
            WriteRecord_Locked(record, flush: true);
        }

        return Task.CompletedTask;
    }

    public Task AppendItemAsync(Guid operationId, JournalItemEntry item, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var itemDto = new JournalItemDto(
            item.Kind,
            item.SourcePath,
            item.DestinationPath,
            item.Size,
            item.Modified,
            item.Outcome,
            item.IsDirectory,
            item.BackupPath);

        var record = new JournalRecordDto(
            Version: JournalCodec.CurrentSchemaVersion,
            Type: "item",
            OperationId: operationId,
            Item: itemDto);

        // Destructive operations (overwrites, staging backup, or moves) must flush immediately
        bool isDestructive = (item.Outcome == ItemOutcome.Replaced) || (item.Kind == TransferKind.Move);

        lock (_lock)
        {
            if (_inMemoryEntries.TryGetValue(operationId, out var existing))
            {
                existing.Items.Add(item);
            }

            WriteRecord_Locked(record, flush: isDestructive);
        }

        return Task.CompletedTask;
    }

    public Task CommitEntryAsync(Guid operationId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var record = new JournalRecordDto(
            Version: JournalCodec.CurrentSchemaVersion,
            Type: "commit",
            OperationId: operationId);

        lock (_lock)
        {
            if (_inMemoryEntries.TryGetValue(operationId, out var existing))
            {
                _inMemoryEntries[operationId] = (existing.Entry with { State = JournalState.Committed }, existing.Items);
            }

            WriteRecord_Locked(record, flush: true);
        }

        return Task.CompletedTask;
    }

    public Task MarkUndoneAsync(Guid operationId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var record = new JournalRecordDto(
            Version: JournalCodec.CurrentSchemaVersion,
            Type: "undone",
            OperationId: operationId);

        lock (_lock)
        {
            if (_inMemoryEntries.TryGetValue(operationId, out var existing))
            {
                _inMemoryEntries[operationId] = (existing.Entry with { State = JournalState.Undone }, existing.Items);
            }

            WriteRecord_Locked(record, flush: true);
        }

        return Task.CompletedTask;
    }

    public Task AbandonEntryAsync(Guid operationId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var record = new JournalRecordDto(
            Version: JournalCodec.CurrentSchemaVersion,
            Type: "abandon",
            OperationId: operationId);

        lock (_lock)
        {
            if (_inMemoryEntries.TryGetValue(operationId, out var existing))
            {
                _inMemoryEntries[operationId] = (existing.Entry with { State = JournalState.Abandoned }, existing.Items);
            }

            WriteRecord_Locked(record, flush: true);
        }

        return Task.CompletedTask;
    }

    public Task DeleteEntryAsync(Guid operationId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var record = new JournalRecordDto(
            Version: JournalCodec.CurrentSchemaVersion,
            Type: "delete",
            OperationId: operationId);

        lock (_lock)
        {
            _inMemoryEntries.Remove(operationId);
            WriteRecord_Locked(record, flush: true);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PersistentUndoEntry>> LoadEntriesAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        lock (_lock)
        {
            if (_isDegraded || !_fs.FileExists(_journalPath))
            {
                var memoryList = _inMemoryEntries.Values
                    .Where(v => v.Entry.State == JournalState.Committed)
                    .Select(v => v.Entry with { Items = v.Items })
                    .ToList();
                return Task.FromResult<IReadOnlyList<PersistentUndoEntry>>(memoryList);
            }

            try
            {
                using var stream = _fs.OpenRead(_journalPath);
                using var reader = new StreamReader(stream, Encoding.UTF8);

                var replayed = JournalCodec.Replay(reader, out var corruptedLines);

                if (corruptedLines.Count > 0)
                {
                    HandleCorruptedLines_Locked(corruptedLines);
                }

                var committedEntries = replayed.Values
                    .Where(e => e.State == JournalState.Committed)
                    .OrderBy(e => e.Timestamp)
                    .ToList();

                return Task.FromResult<IReadOnlyList<PersistentUndoEntry>>(committedEntries);
            }
            catch (Exception ex)
            {
                Degrade_Locked($"Failed to read journal file: {ex.Message}");
                var memoryList = _inMemoryEntries.Values
                    .Where(v => v.Entry.State == JournalState.Committed)
                    .Select(v => v.Entry with { Items = v.Items })
                    .ToList();
                return Task.FromResult<IReadOnlyList<PersistentUndoEntry>>(memoryList);
            }
        }
    }

    public async Task<int> RecoverPendingEntriesAsync(
        IFileSystem fs,
        IStagingBackupStore? stager,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        Dictionary<Guid, PersistentUndoEntry> entries;
        lock (_lock)
        {
            if (!_fs.FileExists(_journalPath)) return 0;

            try
            {
                using var stream = _fs.OpenRead(_journalPath);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                entries = JournalCodec.Replay(reader, out _);
            }
            catch
            {
                return 0;
            }
        }

        var pendingEntries = entries.Values.Where(e => e.State == JournalState.Pending).ToList();
        int recoveredCount = 0;

        foreach (var entry in pendingEntries)
        {
            ct.ThrowIfCancellationRequested();

            // 1. Recover unfulfilled InProgress / Intent records
            if (entry.Intents != null)
            {
                foreach (var intent in entry.Intents)
                {
                    // Clean up leftover .fmpart temp file
                    if (!string.IsNullOrEmpty(intent.TempPath))
                    {
                        try
                        {
                            if (fs.FileExists(intent.TempPath))
                            {
                                fs.Delete(intent.TempPath);
                            }
                        }
                        catch { }
                    }

                    // Restore backup if destination missing
                    if (!string.IsNullOrEmpty(intent.PlannedBackupPath) && fs.FileExists(intent.PlannedBackupPath))
                    {
                        if (!string.IsNullOrEmpty(intent.DestinationPath) && !fs.Exists(intent.DestinationPath))
                        {
                            if (stager != null)
                            {
                                try
                                {
                                    await stager.RestoreAsync(intent.PlannedBackupPath, intent.DestinationPath, ct).ConfigureAwait(false);
                                }
                                catch { }
                            }
                        }
                    }
                }
            }

            bool hasRecoverableItem = false;

            // 2. Recover completed items with backups where destination is missing
            foreach (var item in entry.Items)
            {
                if (item.BackupPath != null && fs.FileExists(item.BackupPath))
                {
                    // 1. Dest does not exist -> Rollback backup to dest
                    if (!string.IsNullOrEmpty(item.DestinationPath) && !fs.Exists(item.DestinationPath))
                    {
                        if (stager != null)
                        {
                            try
                            {
                                await stager.RestoreAsync(item.BackupPath, item.DestinationPath, ct).ConfigureAwait(false);
                            }
                            catch
                            {
                                // Best effort restore
                            }
                        }
                    }
                    // 2. Dest exists -> File successfully landed, keep backup for future Undo
                }

                if (item.Outcome == ItemOutcome.Copied ||
                    item.Outcome == ItemOutcome.Moved ||
                    item.Outcome == ItemOutcome.Replaced ||
                    item.Outcome == ItemOutcome.KeptBoth)
                {
                    hasRecoverableItem = true;
                }
            }

            // 3. If has undoable item, mark Committed; else Abandoned
            if (hasRecoverableItem)
            {
                await CommitEntryAsync(entry.OperationId, ct).ConfigureAwait(false);
                recoveredCount++;
            }
            else
            {
                await AbandonEntryAsync(entry.OperationId, ct).ConfigureAwait(false);
            }
        }

        return recoveredCount;
    }

    public Task CompactAsync(TimeSpan maxAge, int maxEntries = 50, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        lock (_lock)
        {
            if (_isDegraded || !_fs.FileExists(_journalPath))
            {
                return Task.CompletedTask;
            }

            try
            {
                Dictionary<Guid, PersistentUndoEntry> replayed;
                using (var stream = _fs.OpenRead(_journalPath))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    replayed = JournalCodec.Replay(reader, out _);
                }

                DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
                var filtered = JournalCodec.FilterForCompaction(replayed.Values, maxAge, maxEntries, now);

                string tmpPath = _journalPath + ".tmp";
                using (var writeStream = _fs.CreateFileForWrite(tmpPath))
                using (var writer = new StreamWriter(writeStream, Encoding.UTF8))
                {
                    foreach (var entry in filtered)
                    {
                        // Write begin
                        JournalCodec.WriteRecord(writer, new JournalRecordDto(
                            Version: JournalCodec.CurrentSchemaVersion,
                            Type: "begin",
                            OperationId: entry.OperationId,
                            Kind: entry.Kind,
                            Timestamp: entry.Timestamp,
                            OwnerPane: entry.OwnerPane));

                        // Write items
                        foreach (var item in entry.Items)
                        {
                            JournalCodec.WriteRecord(writer, new JournalRecordDto(
                                Version: JournalCodec.CurrentSchemaVersion,
                                Type: "item",
                                OperationId: entry.OperationId,
                                Item: new JournalItemDto(
                                    item.Kind,
                                    item.SourcePath,
                                    item.DestinationPath,
                                    item.Size,
                                    item.Modified,
                                    item.Outcome,
                                    item.IsDirectory,
                                    item.BackupPath)));
                        }

                        // Write commit
                        JournalCodec.WriteRecord(writer, new JournalRecordDto(
                            Version: JournalCodec.CurrentSchemaVersion,
                            Type: "commit",
                            OperationId: entry.OperationId));
                    }
                    writer.Flush();
                }

                // Atomic replacement of journal file
                _fs.Move(tmpPath, _journalPath, overwrite: true);
            }
            catch (Exception ex)
            {
                Degrade_Locked($"Journal compaction failed: {ex.Message}");
            }
        }

        return Task.CompletedTask;
    }

    private void WriteRecord_Locked(JournalRecordDto record, bool flush)
    {
        if (_isDegraded) return;

        try
        {
            string? dir = Path.GetDirectoryName(_journalPath);
            if (!string.IsNullOrEmpty(dir) && !_fs.DirectoryExists(dir))
            {
                _fs.CreateDirectory(dir);
            }

            using var stream = _fs.OpenAppend(_journalPath);
            using var writer = new StreamWriter(stream, Encoding.UTF8);
            JournalCodec.WriteRecord(writer, record);
            if (flush)
            {
                writer.Flush();
            }
        }
        catch (Exception ex)
        {
            Degrade_Locked($"Failed to write to journal '{_journalPath}': {ex.Message}");
        }
    }

    private void HandleCorruptedLines_Locked(List<string> corruptedLines)
    {
        try
        {
            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            string? dir = Path.GetDirectoryName(_journalPath);
            string corruptPath = Path.Combine(dir ?? string.Empty, $"undo-journal.corrupt-{timestamp}.jsonl");

            using var stream = _fs.CreateFileForWrite(corruptPath);
            using var writer = new StreamWriter(stream, Encoding.UTF8);
            foreach (var line in corruptedLines)
            {
                writer.WriteLine(line);
            }
            writer.Flush();
        }
        catch
        {
            // Best effort corrupt log writing
        }
    }

    private void Degrade_Locked(string reason)
    {
        _isDegraded = true;
        if (!_hasWarnedDegraded)
        {
            _hasWarnedDegraded = true;
            WarningHandler?.Invoke($"Undo history won't survive a restart: {reason}");
        }
    }
}

