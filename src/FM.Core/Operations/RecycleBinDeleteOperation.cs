namespace FM.Core.Operations;

using FM.Core.RecycleBin;

/// <summary>
/// Reversible operation that deletes items by moving them to the Windows Recycle Bin.
/// Undoing this operation restores the items from the Recycle Bin via IRecycleBinService.
/// </summary>
public sealed class RecycleBinDeleteOperation : IFileOperation
{
    private readonly IRecycleBinService _recycleBinService;
    private readonly Func<IEnumerable<string>, bool> _recycleAction;
    public IReadOnlyList<string> DeletedPaths { get; }

    public string Description => $"Delete {DeletedPaths.Count} item(s) to Recycle Bin";

    public RecycleBinDeleteOperation(
        IRecycleBinService recycleBinService,
        Func<IEnumerable<string>, bool> recycleAction,
        IEnumerable<string> deletedPaths)
    {
        _recycleBinService = recycleBinService ?? throw new ArgumentNullException(nameof(recycleBinService));
        _recycleAction = recycleAction ?? throw new ArgumentNullException(nameof(recycleAction));
        DeletedPaths = deletedPaths.ToList();
    }

    public Task<OperationResult> DoAsync(CancellationToken ct = default)
    {
        bool ok = _recycleAction(DeletedPaths);
        return Task.FromResult(ok ? OperationResult.Ok() : OperationResult.Fail("Failed to recycle items."));
    }

    public async Task<OperationResult> UndoAsync(CancellationToken ct = default)
    {
        int restoredCount = 0;
        foreach (var path in DeletedPaths)
        {
            if (await _recycleBinService.RestoreItemAsync(path, ct).ConfigureAwait(false))
            {
                restoredCount++;
            }
        }

        if (restoredCount == 0 && DeletedPaths.Count > 0)
        {
            return OperationResult.Fail("Could not restore item(s) from Recycle Bin.");
        }

        return OperationResult.Ok();
    }
}

