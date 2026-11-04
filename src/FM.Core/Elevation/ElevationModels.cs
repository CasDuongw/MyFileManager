namespace FM.Core.Elevation;

public enum ElevationKind
{
    CreateFolder,
    CreateFile,
    Rename,
    Delete,
    Copy,
    Move
}

public record ElevationItem(
    ElevationKind Kind,
    string SourcePath,
    string? DestinationPath = null,
    string? ContentBase64 = null);

public record ElevationPayload(
    string OperationId,
    List<ElevationItem> Items);

public record ElevationItemResult(
    string SourcePath,
    bool Success,
    string? ErrorMessage = null);

public record ElevationResponse(
    string OperationId,
    bool AllSucceeded,
    List<ElevationItemResult> Results,
    string? GlobalErrorMessage = null);

public static class ElevationExitCodes
{
    public const int Success = 0;
    public const int InvalidArguments = 1;
    public const int SecurityViolation = 2;
    public const int HashMismatch = 10;
    public const int PayloadCorrupt = 11;
    public const int ExecutionFailed = 12;
}
