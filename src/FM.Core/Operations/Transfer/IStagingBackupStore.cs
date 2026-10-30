namespace FM.Core.Operations.Transfer;

public record StagedBackup(
    string BackupPath,
    long Size,
    DateTime Modified);

public record StagingPolicy(
    TimeSpan MaxAge,
    long MaxTotalBytes)
{
    public static StagingPolicy Default => new(
        MaxAge: TimeSpan.FromDays(7),
        MaxTotalBytes: 2L * 1024 * 1024 * 1024); // 2 GiB
}

/// <summary>
/// Store for safely staging files before they are overwritten, enabling robust and reversible Undo.
/// </summary>
public interface IStagingBackupStore
{
    Task<StagedBackup> StageAsync(string sourcePath, Guid operationId, int itemIndex, CancellationToken ct = default);
    Task RestoreAsync(string backupPath, string targetPath, CancellationToken ct = default);
    Task DiscardAsync(Guid operationId, CancellationToken ct = default);
    Task PurgeAsync(StagingPolicy policy, IReadOnlySet<Guid>? liveOperationIds = null, CancellationToken ct = default);
    bool BackupExists(string backupPath);
}

