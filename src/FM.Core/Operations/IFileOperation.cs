namespace FM.Core.Operations;

public record OperationResult(bool Success, string? ErrorMessage = null, string? TargetPath = null)
{
    public static OperationResult Ok(string? targetPath = null) => new(true, null, targetPath);
    public static OperationResult Fail(string errorMessage) => new(false, errorMessage, null);
}

/// <summary>
/// Represents a reversible file system operation.
/// </summary>
public interface IFileOperation
{
    string Description { get; }
    Task<OperationResult> DoAsync(CancellationToken ct = default);
    Task<OperationResult> UndoAsync(CancellationToken ct = default);
}
