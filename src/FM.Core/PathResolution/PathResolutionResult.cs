namespace FM.Core.PathResolution;

/// <summary>
/// Encapsulates the outcome of resolving a raw user input path or address bar query.
/// </summary>
public sealed record PathResolutionResult
{
    public ResolvedPathType PathType { get; init; }
    public string ResolvedPath { get; init; }
    public string DisplayName { get; init; }
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }

    public PathResolutionResult(
        ResolvedPathType pathType,
        string resolvedPath,
        string displayName,
        bool isSuccess = true,
        string? errorMessage = null)
    {
        PathType = pathType;
        ResolvedPath = resolvedPath ?? string.Empty;
        DisplayName = displayName ?? string.Empty;
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
    }

    public static PathResolutionResult Success(ResolvedPathType type, string resolvedPath, string displayName) =>
        new(type, resolvedPath, displayName, isSuccess: true);

    public static PathResolutionResult Failure(ResolvedPathType type, string originalPath, string errorMessage) =>
        new(type, originalPath, originalPath, isSuccess: false, errorMessage: errorMessage);
}

