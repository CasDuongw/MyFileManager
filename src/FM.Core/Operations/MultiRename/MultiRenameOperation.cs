using System.IO;
using FM.Core.FileSystem;

namespace FM.Core.Operations.MultiRename;

/// <summary>
/// Reversible operation that executes a batch of file/folder renames using a robust two-phase
/// strategy (Original -> Temp -> Target). Handles cycles, swaps, and case-only renames safely
/// with automated full rollback upon failure and compound single-step undo.
/// </summary>
public sealed class MultiRenameOperation : IFileOperation
{
    private readonly IFileSystem _fs;
    public MultiRenamePlan Plan { get; }
    public IReadOnlyList<MultiRenamePlanItem> ChangedItems { get; }

    public string Description => $"Batch rename {ChangedItems.Count} item(s)";

    public MultiRenameOperation(IFileSystem fs, MultiRenamePlan plan)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        Plan = plan ?? throw new ArgumentNullException(nameof(plan));
        ChangedItems = plan.Items.Where(i => i.IsChanged && i.IsValid).ToList();
    }

    public Task<OperationResult> DoAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (Plan.HasErrors)
        {
            return Task.FromResult(OperationResult.Fail($"Cannot execute batch rename: {Plan.ErrorCount} conflict(s) or invalid name(s) present."));
        }

        if (ChangedItems.Count == 0)
        {
            return Task.FromResult(OperationResult.Ok());
        }

        return Task.FromResult(ExecuteTwoPhaseRename(
            forward: true,
            items: ChangedItems,
            ct));
    }

    public Task<OperationResult> UndoAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (ChangedItems.Count == 0)
        {
            return Task.FromResult(OperationResult.Ok());
        }

        return Task.FromResult(ExecuteTwoPhaseRename(
            forward: false,
            items: ChangedItems,
            ct));
    }

    private OperationResult ExecuteTwoPhaseRename(
        bool forward,
        IReadOnlyList<MultiRenamePlanItem> items,
        CancellationToken ct)
    {
        // Each entry: (From, To, IsDir)
        var executedMoves = new Stack<(string From, string To, bool IsDir)>();

        // Phase 1 mapping: From -> Temp
        var phase1Steps = new List<(string From, string Temp, bool IsDir)>(items.Count);
        // Phase 2 mapping: Temp -> Final
        var phase2Steps = new List<(string Temp, string Final, bool IsDir)>(items.Count);

        foreach (var item in items)
        {
            string from = forward ? item.OriginalPath : item.NewPath;
            string final = forward ? item.NewPath : item.OriginalPath;
            string dir = Path.GetDirectoryName(from) ?? "";
            string tempName = $"_fmtmp_{Guid.NewGuid():N}.tmp";
            string tempPath = !string.IsNullOrEmpty(dir) ? Path.Combine(dir, tempName) : tempName;

            phase1Steps.Add((from, tempPath, item.IsDirectory));
            phase2Steps.Add((tempPath, final, item.IsDirectory));
        }

        try
        {
            // --- Phase 1: Move all to Temp ---
            foreach (var step in phase1Steps)
            {
                ct.ThrowIfCancellationRequested();
                MoveItem(step.From, step.Temp, step.IsDir);
                executedMoves.Push((step.From, step.Temp, step.IsDir));
            }

            // --- Phase 2: Move all Temp to Final ---
            foreach (var step in phase2Steps)
            {
                ct.ThrowIfCancellationRequested();
                MoveItem(step.Temp, step.Final, step.IsDir);
                executedMoves.Push((step.Temp, step.Final, step.IsDir));
            }

            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            // Rollback everything executed in reverse order
            string rollbackError = null!;
            while (executedMoves.Count > 0)
            {
                var (from, to, isDir) = executedMoves.Pop();
                try
                {
                    MoveItem(to, from, isDir);
                }
                catch (Exception rbEx)
                {
                    rollbackError = rbEx.Message;
                }
            }

            string failMsg = rollbackError != null
                ? $"Batch rename failed: {ex.Message}. Rollback also encountered an error: {rollbackError}"
                : $"Batch rename failed: {ex.Message}. All changes were safely rolled back.";

            return OperationResult.Fail(failMsg);
        }
    }

    private void MoveItem(string source, string destination, bool isDirectory)
    {
        if (isDirectory)
        {
            _fs.MoveDirectory(source, destination);
        }
        else
        {
            _fs.MoveFile(source, destination);
        }
    }
}

