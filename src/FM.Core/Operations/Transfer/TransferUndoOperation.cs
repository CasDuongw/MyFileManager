using FM.Core.FileSystem;

namespace FM.Core.Operations.Transfer;

/// <summary>
/// Reversible operation wrapping a completed transfer for the UndoStack.
/// Handles undo for Copy and Move while guaranteeing data integrity.
/// Operations that replaced/overwrote existing files are rejected from undo with a clear message.
/// </summary>
public sealed class TransferUndoOperation : IFileOperation
{
    private readonly IFileSystem _fs;
    public TransferKind Kind { get; }
    public TransferResult Result { get; }
    public bool CanUndo { get; }
    public string? NonUndoableReason { get; }

    public string Description =>
        Kind == TransferKind.Copy
            ? $"Copy of {Result.Items.Count(i => !i.IsDirectory && (i.Outcome == ItemOutcome.Copied || i.Outcome == ItemOutcome.KeptBoth))} files"
            : $"Move of {Result.Items.Count(i => !i.IsDirectory && i.Outcome == ItemOutcome.Moved)} files";

    public TransferUndoOperation(IFileSystem fs, TransferKind kind, TransferResult result)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        Kind = kind;
        Result = result ?? throw new ArgumentNullException(nameof(result));

        if (result.Items.Any(i => i.Outcome == ItemOutcome.Replaced))
        {
            CanUndo = false;
            NonUndoableReason = "Overwritten files cannot be restored without backup staging (planned for v0.4).";
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

    public Task<OperationResult> UndoAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (!CanUndo)
        {
            return Task.FromResult(OperationResult.Fail(NonUndoableReason ?? "Operation is not undoable."));
        }

        if (Kind == TransferKind.Copy)
        {
            return Task.FromResult(UndoCopy());
        }

        return Task.FromResult(UndoMove());
    }

    private OperationResult UndoCopy()
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

        // 2. Remove created directories bottom-up only if empty
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

    private OperationResult UndoMove()
    {
        var messages = new List<string>();

        // Move files and directories back to source
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

        if (messages.Count > 0)
        {
            return OperationResult.Fail(string.Join("; ", messages));
        }

        return OperationResult.Ok();
    }
}
