using FM.Core.FileSystem;

namespace FM.Core.Operations.Transfer;

/// <summary>
/// Reversible operation wrapping a completed transfer for the UndoStack.
/// Handles undo for Copy and Move while guaranteeing data integrity.
/// Replaced files are undoable when backed up to staging.
/// </summary>
public sealed class TransferUndoOperation : IFileOperation
{
    private readonly IFileSystem _fs;
    private readonly IStagingBackupStore? _stager;
    public Guid OperationId { get; }
    public TransferKind Kind { get; }
    public TransferResult Result { get; }
    public bool CanUndo { get; }
    public string? NonUndoableReason { get; }

    public string Description =>
        Kind == TransferKind.Copy
            ? $"Copy of {Result.Items.Count(i => !i.IsDirectory && (i.Outcome == ItemOutcome.Copied || i.Outcome == ItemOutcome.KeptBoth || i.Outcome == ItemOutcome.Replaced))} files"
            : $"Move of {Result.Items.Count(i => !i.IsDirectory && (i.Outcome == ItemOutcome.Moved || i.Outcome == ItemOutcome.Replaced))} files";

    public TransferUndoOperation(
        IFileSystem fs,
        TransferKind kind,
        TransferResult result,
        IStagingBackupStore? stager = null,
        Guid operationId = default)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _stager = stager;
        OperationId = operationId;
        Kind = kind;
        Result = result ?? throw new ArgumentNullException(nameof(result));

        var replacedItems = result.Items.Where(i => i.Outcome == ItemOutcome.Replaced).ToList();
        if (replacedItems.Count > 0)
        {
            if (_stager != null && replacedItems.All(i => !string.IsNullOrEmpty(i.BackupPath)))
            {
                CanUndo = true;
            }
            else
            {
                CanUndo = false;
                NonUndoableReason = "Overwritten files cannot be restored without backup staging.";
            }
        }
        else if (result.Items.Count == 0 || result.Items.All(i => i.Outcome == ItemOutcome.Skipped || i.Outcome == ItemOutcome.Failed))
        {
            CanUndo = false;
            NonUndoableReason = "No items were transferred.";
        }
        else
        {
            CanUndo = true;
        }
    }

    public Task<OperationResult> DoAsync(CancellationToken ct = default)
    {
        throw new InvalidOperationException("TransferUndoOperation is created post-execution and cannot be executed with DoAsync.");
    }

    public async Task<OperationResult> UndoAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (!CanUndo)
        {
            return OperationResult.Fail(NonUndoableReason ?? "Operation is not undoable.");
        }

        // Verify all required backups still exist before proceeding
        if (_stager != null)
        {
            foreach (var item in Result.Items.Where(i => i.Outcome == ItemOutcome.Replaced && i.BackupPath != null))
            {
                if (!_stager.BackupExists(item.BackupPath!))
                {
                    return OperationResult.Fail($"Cannot undo: Staged backup file is missing or was purged: '{item.BackupPath}'.");
                }
            }
        }

        if (Kind == TransferKind.Copy)
        {
            return await UndoCopyAsync(ct).ConfigureAwait(false);
        }

        return await UndoMoveAsync(ct).ConfigureAwait(false);
    }

    private async Task<OperationResult> UndoCopyAsync(CancellationToken ct)
    {
        var messages = new List<string>();

        // 1. Delete created files whose size and timestamp still match
        var createdFiles = Result.Items
            .Where(i => !i.IsDirectory && (i.Outcome == ItemOutcome.Copied || i.Outcome == ItemOutcome.KeptBoth) && i.DestinationPath != null);

        foreach (var item in createdFiles)
        {
            string dst = item.DestinationPath!;
            var info = _fs.GetInfo(dst);

            if (info != null && info.Exists)
            {
                bool sizeMatches = (info.Size == item.Size);
                bool timeMatches = item.ModifiedAfter.HasValue &&
                                   Math.Abs((info.Modified - item.ModifiedAfter.Value).TotalSeconds) < 2.0;

                if (sizeMatches && timeMatches)
                {
                    try
                    {
                        _fs.Delete(dst);
                    }
                    catch (Exception ex)
                    {
                        messages.Add($"Failed to delete '{dst}': {ex.Message}");
                    }
                }
                else
                {
                    messages.Add($"Skipped '{dst}' because it was modified after copy.");
                }
            }
        }

        // 2. Restore replaced files from staging
        var replacedFiles = Result.Items
            .Where(i => !i.IsDirectory && i.Outcome == ItemOutcome.Replaced && i.DestinationPath != null && i.BackupPath != null);

        foreach (var item in replacedFiles)
        {
            string dst = item.DestinationPath!;
            var info = _fs.GetInfo(dst);

            if (info != null && info.Exists)
            {
                bool sizeMatches = (info.Size == item.Size);
                bool timeMatches = item.ModifiedAfter.HasValue &&
                                   Math.Abs((info.Modified - item.ModifiedAfter.Value).TotalSeconds) < 2.0;

                if (sizeMatches && timeMatches)
                {
                    try
                    {
                        _fs.Delete(dst);
                        if (_stager != null)
                        {
                            await _stager.RestoreAsync(item.BackupPath!, dst, ct).ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        messages.Add($"Failed to restore backup for '{dst}': {ex.Message}");
                    }
                }
                else
                {
                    messages.Add($"Skipped restoring '{dst}' because it was modified after copy.");
                }
            }
            else
            {
                // Destination file was deleted or moved; restore backup to destination
                try
                {
                    if (_stager != null)
                    {
                        await _stager.RestoreAsync(item.BackupPath!, dst, ct).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    messages.Add($"Failed to restore backup for missing '{dst}': {ex.Message}");
                }
            }
        }

        // 3. Remove created directories bottom-up only if empty
        var createdDirs = Result.Items
            .Where(i => i.IsDirectory && i.Outcome == ItemOutcome.Copied && i.DestinationPath != null)
            .OrderByDescending(i => i.DestinationPath!.Length);

        foreach (var dir in createdDirs)
        {
            try
            {
                if (_fs.IsDirectoryEmpty(dir.DestinationPath!))
                {
                    _fs.Delete(dir.DestinationPath!);
                }
            }
            catch
            {
                // Best effort directory cleanup
            }
        }

        if (messages.Count > 0)
        {
            return OperationResult.Fail(string.Join("; ", messages));
        }

        return OperationResult.Ok();
    }

    private async Task<OperationResult> UndoMoveAsync(CancellationToken ct)
    {
        var messages = new List<string>();

        // 1. Move ordinary moved items back to source
        var movedItems = Result.Items
            .Where(i => (i.Outcome == ItemOutcome.Moved || i.Outcome == ItemOutcome.KeptBoth) && i.DestinationPath != null);

        foreach (var item in movedItems)
        {
            string dst = item.DestinationPath!;
            string src = item.SourcePath;

            // Check if original location is free
            if (_fs.Exists(src))
            {
                messages.Add($"Cannot undo: Original location '{src}' is already occupied.");
                continue;
            }

            var info = _fs.GetInfo(dst);
            if (info == null || !info.Exists)
            {
                messages.Add($"Cannot undo: Moved item '{dst}' no longer exists.");
                continue;
            }

            // Verify item has not been modified
            if (!item.IsDirectory && item.ModifiedAfter.HasValue)
            {
                bool sizeMatches = (info.Size == item.Size);
                bool timeMatches = Math.Abs((info.Modified - item.ModifiedAfter.Value).TotalSeconds) < 2.0;
                if (!sizeMatches || !timeMatches)
                {
                    messages.Add($"Cannot undo: Item '{dst}' was modified after move.");
                    continue;
                }
            }

            try
            {
                _fs.Move(dst, src, overwrite: false);
            }
            catch (Exception ex)
            {
                messages.Add($"Failed to move '{dst}' back to '{src}': {ex.Message}");
            }
        }

        // 2. Restore replaced moved items: move new file back to source, restore backup to destination
        var replacedItems = Result.Items
            .Where(i => i.Outcome == ItemOutcome.Replaced && i.DestinationPath != null && i.BackupPath != null);

        foreach (var item in replacedItems)
        {
            string dst = item.DestinationPath!;
            string src = item.SourcePath;

            if (_fs.Exists(src))
            {
                messages.Add($"Cannot undo: Original source location '{src}' is already occupied.");
                continue;
            }

            var info = _fs.GetInfo(dst);
            if (info != null && info.Exists)
            {
                if (!item.IsDirectory && item.ModifiedAfter.HasValue)
                {
                    bool sizeMatches = (info.Size == item.Size);
                    bool timeMatches = Math.Abs((info.Modified - item.ModifiedAfter.Value).TotalSeconds) < 2.0;
                    if (!sizeMatches || !timeMatches)
                    {
                        messages.Add($"Cannot undo: Item '{dst}' was modified after move.");
                        continue;
                    }
                }

                try
                {
                    _fs.Move(dst, src, overwrite: false);
                }
                catch (Exception ex)
                {
                    messages.Add($"Failed to return '{dst}' to source '{src}': {ex.Message}");
                    continue;
                }
            }

            // Now restore the backup back to dst
            try
            {
                if (_stager != null)
                {
                    await _stager.RestoreAsync(item.BackupPath!, dst, ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                messages.Add($"Failed to restore backup to '{dst}': {ex.Message}");
            }
        }

        if (messages.Count > 0)
        {
            return OperationResult.Fail(string.Join("; ", messages));
        }

        return OperationResult.Ok();
    }
}
