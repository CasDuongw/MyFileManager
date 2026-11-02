namespace FM.Core.ShellIntegration;

public record SendToTarget(
    string DisplayName,
    string Path,
    string? ResolvedDirectory = null,
    bool IsDesktopShortcut = false,
    bool IsZipCompressor = false);

public interface ISendToService
{
    IReadOnlyList<SendToTarget> GetTargets();
    Task<bool> ExecuteSendToAsync(
        SendToTarget target,
        IReadOnlyList<string> sourcePaths,
        Func<IReadOnlyList<string>, string, Task> copyToFolderAction,
        Func<IReadOnlyList<string>, Task> zipAction,
        CancellationToken ct = default);
}

