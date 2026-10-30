namespace FM.Core.Operations;

/// <summary>
/// Maintains an undo history of reversible file operations.
/// </summary>
public interface IUndoStack
{
    bool CanUndo { get; }
    string? NextUndoDescription { get; }
    event Action? CanUndoChanged;

    void Push(IFileOperation operation);
    Task<OperationResult?> UndoAsync(CancellationToken ct = default);
    void Clear();
}

public sealed class UndoStack : IUndoStack
{
    private readonly Stack<IFileOperation> _operations = new();
    private readonly object _lock = new();

    public event Action? CanUndoChanged;

    public bool CanUndo
    {
        get
        {
            lock (_lock) return _operations.Count > 0;
        }
    }

    public string? NextUndoDescription
    {
        get
        {
            lock (_lock)
            {
                return _operations.Count > 0 ? _operations.Peek().Description : null;
            }
        }
    }

    public void Push(IFileOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        lock (_lock)
        {
            _operations.Push(operation);
        }
        CanUndoChanged?.Invoke();
    }

    public async Task<OperationResult?> UndoAsync(CancellationToken ct = default)
    {
        IFileOperation? op;
        lock (_lock)
        {
            if (_operations.Count == 0) return null;
            op = _operations.Pop();
        }

        CanUndoChanged?.Invoke();

        try
        {
            var result = await op.UndoAsync(ct);
            if (!result.Success)
            {
                // If undo failed, push it back onto stack so user can retry or understand status
                lock (_lock)
                {
                    _operations.Push(op);
                }
                CanUndoChanged?.Invoke();
            }
            return result;
        }
        catch (Exception ex)
        {
            lock (_lock)
            {
                _operations.Push(op);
            }
            CanUndoChanged?.Invoke();
            return OperationResult.Fail($"Unexpected error during undo: {ex.Message}");
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _operations.Clear();
        }
        CanUndoChanged?.Invoke();
    }
}

