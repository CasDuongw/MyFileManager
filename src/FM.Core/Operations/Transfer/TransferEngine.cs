using System.IO;
using System.Security.Cryptography;
using FM.Core.FileSystem;

namespace FM.Core.Operations.Transfer;

/// <summary>
/// Robust transfer engine executing Copy and Move operations.
/// Guarantees data safety via temporary files (.fmpart) + atomic rename,
/// post-copy hash verification before deleting sources, and cooperative pause/cancellation.
/// </summary>
public sealed class TransferEngine : ITransferEngine
{
    private readonly IFileSystem _fs;
    private readonly IStagingBackupStore? _stager;
    private readonly TimeProvider _timeProvider;

    public TransferEngine(IFileSystem fs, IStagingBackupStore? stager = null, TimeProvider? timeProvider = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _stager = stager;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<TransferResult> RunAsync(
        TransferRequest request,
        IProgress<TransferProgress>? progress = null,
        PauseGate? gate = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 1. Build plan
        TransferPlan plan;
        try
        {
            plan = TransferPlanner.CreatePlan(_fs, request, progress, ct);
        }
        catch (OperationCanceledException)
        {
            return new TransferResult(TransferState.Cancelled, Array.Empty<TransferItemResult>());
        }
        catch (Exception ex)
        {
            var failedItem = new TransferItemResult(
                request.Sources.FirstOrDefault() ?? string.Empty,
                request.DestinationDirectory,
                true,
                ItemOutcome.Failed,
                0,
                null,
                ex.Message);
            return new TransferResult(TransferState.Failed, new[] { failedItem });
        }

        // 2. Execute plan
        if (request.Kind == TransferKind.Move && !plan.IsCrossVolume)
        {
            return await ExecuteSameVolumeMoveAsync(plan, progress, gate, ct).ConfigureAwait(false);
        }

        return await ExecuteCopyOrCrossVolumeMoveAsync(plan, progress, gate, ct).ConfigureAwait(false);
    }

    // -------------------------------------------------------------------------
    // Same-Volume Move (Atomic Rename, Item Progress Only)
    // -------------------------------------------------------------------------

    private async Task<TransferResult> ExecuteSameVolumeMoveAsync(
        TransferPlan plan,
        IProgress<TransferProgress>? progress,
        PauseGate? gate,
        CancellationToken ct)
    {
        var results = new List<TransferItemResult>();
        ConflictDecision? rememberedConflict = null;
        int itemsDone = 0;
        int totalItems = plan.Request.Sources.Count;

        foreach (var source in plan.Request.Sources)
        {
            ct.ThrowIfCancellationRequested();
            if (gate != null)
            {
                await gate.WaitIfPausedAsync(ct).ConfigureAwait(false);
            }

            var info = _fs.GetInfo(source);
            if (info == null || !info.Exists) continue;

            string name = Path.GetFileName(source);
            string destinationPath = Path.Combine(plan.Request.DestinationDirectory, name);

            // Check conflict
            var conflictResult = await ResolveConflictAsync(
                source,
                destinationPath,
                info.Size,
                info.Modified,
                info.IsDirectory,
                plan.Request,
                rememberedConflict,
                ct).ConfigureAwait(false);

            rememberedConflict = conflictResult.RememberedDecision;

            if (conflictResult.Action == ConflictAction.Cancel)
            {
                return new TransferResult(TransferState.Cancelled, results);
            }

            if (conflictResult.Action == ConflictAction.Skip)
            {
                var skipOutcome = conflictResult.Outcome == ItemOutcome.Failed ? ItemOutcome.Failed : ItemOutcome.Skipped;
                string? skipErr = conflictResult.Outcome == ItemOutcome.Failed ? "Cannot replace a directory with a file or vice-versa." : null;
                results.Add(new TransferItemResult(source, destinationPath, info.IsDirectory, skipOutcome, info.Size, null, skipErr));
                itemsDone++;
                ReportItemProgress(progress, source, itemsDone, totalItems);
                continue;
            }

            string finalDst = conflictResult.FinalDestinationPath;
            ItemOutcome outcome = conflictResult.Outcome;

            string? backupPath = null;
            try
            {
                if (outcome == ItemOutcome.Replaced)
                {
                    if (_stager != null)
                    {
                        var staged = await _stager.StageAsync(finalDst, plan.Request.OperationId, itemsDone, ct).ConfigureAwait(false);
                        backupPath = staged.BackupPath;
                        try
                        {
                            _fs.Move(source, finalDst, overwrite: false);
                        }
                        catch
                        {
                            if (backupPath != null)
                            {
                                try { await _stager.RestoreAsync(backupPath, finalDst, ct).ConfigureAwait(false); } catch { }
                            }
                            throw;
                        }
                    }
                    else
                    {
                        _fs.Move(source, finalDst, overwrite: true);
                    }
                }
                else
                {
                    _fs.Move(source, finalDst, overwrite: false);
                }

                results.Add(new TransferItemResult(source, finalDst, info.IsDirectory, outcome, info.Size, info.Modified, null, backupPath));
            }
            catch (Exception ex)
            {
                results.Add(new TransferItemResult(source, finalDst, info.IsDirectory, ItemOutcome.Failed, info.Size, null, ex.Message));
            }

            itemsDone++;
            ReportItemProgress(progress, source, itemsDone, totalItems);
        }

        bool hasErrors = results.Any(r => r.Outcome == ItemOutcome.Failed);
        return new TransferResult(hasErrors ? TransferState.CompletedWithErrors : TransferState.Completed, results);
    }

    // -------------------------------------------------------------------------
    // Copy and Cross-Volume Move (Chunked, Verified, Atomic Rename)
    // -------------------------------------------------------------------------

    private async Task<TransferResult> ExecuteCopyOrCrossVolumeMoveAsync(
        TransferPlan plan,
        IProgress<TransferProgress>? progress,
        PauseGate? gate,
        CancellationToken ct)
    {
        var results = new List<TransferItemResult>();
        ConflictDecision? rememberedConflict = null;
        FailureDecision? rememberedFailure = null;

        long overallBytesDone = 0;
        int filesDone = 0;
        long startTimestamp = _timeProvider.GetTimestamp();

        // 1. Create directory structures first
        foreach (var item in plan.Items.Where(i => i.IsDirectory))
        {
            ct.ThrowIfCancellationRequested();
            string targetDir = Path.Combine(plan.Request.DestinationDirectory, item.RelativeDestinationPath);
            try
            {
                if (!_fs.DirectoryExists(targetDir))
                {
                    _fs.CreateDirectory(targetDir);
                }
                results.Add(new TransferItemResult(item.SourcePath, targetDir, true, ItemOutcome.Copied, 0, item.Modified, null));
            }
            catch (Exception ex)
            {
                results.Add(new TransferItemResult(item.SourcePath, targetDir, true, ItemOutcome.Failed, 0, null, ex.Message));
            }
        }

        // 2. Transfer files
        byte[] buffer = new byte[Math.Max(4096, plan.Request.Options.BufferSize)];

        foreach (var item in plan.Items.Where(i => !i.IsDirectory))
        {
            ct.ThrowIfCancellationRequested();

            string finalDestinationPath = Path.Combine(plan.Request.DestinationDirectory, item.RelativeDestinationPath);
            string destDir = Path.GetDirectoryName(finalDestinationPath) ?? plan.Request.DestinationDirectory;

            // Resolve conflicts
            var conflictResult = await ResolveConflictAsync(
                item.SourcePath,
                finalDestinationPath,
                item.Size,
                item.Modified,
                false,
                plan.Request,
                rememberedConflict,
                ct).ConfigureAwait(false);

            rememberedConflict = conflictResult.RememberedDecision;

            if (conflictResult.Action == ConflictAction.Cancel)
            {
                return new TransferResult(TransferState.Cancelled, results);
            }

            if (conflictResult.Action == ConflictAction.Skip)
            {
                var skipOutcome = conflictResult.Outcome == ItemOutcome.Failed ? ItemOutcome.Failed : ItemOutcome.Skipped;
                string? skipErr = conflictResult.Outcome == ItemOutcome.Failed ? "Cannot replace a directory with a file or vice-versa." : null;
                results.Add(new TransferItemResult(item.SourcePath, finalDestinationPath, false, skipOutcome, item.Size, null, skipErr));
                filesDone++;
                continue;
            }

            finalDestinationPath = conflictResult.FinalDestinationPath;
            ItemOutcome outcome = conflictResult.Outcome;

            // File transfer with retry loop
            bool transferSuccess = false;
            string? transferError = null;
            DateTime? modifiedAfter = null;
            string? backupPath = null;
            int attempt = 0;

            while (!transferSuccess)
            {
                attempt++;
                ct.ThrowIfCancellationRequested();

                string tempPath = Path.Combine(destDir, $"{Path.GetFileName(finalDestinationPath)}.fmpart");

                try
                {
                    (modifiedAfter, backupPath) = await CopySingleFileAsync(
                        item.SourcePath,
                        finalDestinationPath,
                        tempPath,
                        item.Size,
                        item.Modified,
                        outcome,
                        filesDone,
                        plan,
                        buffer,
                        overallBytesDone,
                        filesDone,
                        startTimestamp,
                        progress,
                        gate,
                        ct).ConfigureAwait(false);

                    transferSuccess = true;
                    overallBytesDone += item.Size;
                    filesDone++;
                }
                catch (OperationCanceledException)
                {
                    // Clean up temp file on cancel
                    CleanupTemp(tempPath);
                    return new TransferResult(TransferState.Cancelled, results);
                }
                catch (Exception ex)
                {
                    CleanupTemp(tempPath);
                    transferError = ex.Message;

                    if (attempt <= plan.Request.Options.MaxRetries)
                    {
                        continue;
                    }

                    // Max retries exceeded; consult interaction
                    if (rememberedFailure != null)
                    {
                        if (rememberedFailure.Action == FailureAction.Retry) continue;
                        if (rememberedFailure.Action == FailureAction.Abort)
                        {
                            results.Add(new TransferItemResult(item.SourcePath, finalDestinationPath, false, ItemOutcome.Failed, item.Size, null, transferError));
                            return new TransferResult(TransferState.Failed, results);
                        }
                        break; // Skip
                    }

                    if (plan.Request.Interaction != null)
                    {
                        var failureDecision = await plan.Request.Interaction.OnFailureAsync(
                            new FailureInfo(item.SourcePath, ex, attempt), ct).ConfigureAwait(false);

                        if (failureDecision.ApplyToAll)
                        {
                            rememberedFailure = failureDecision;
                        }

                        if (failureDecision.Action == FailureAction.Retry)
                        {
                            continue;
                        }
                        if (failureDecision.Action == FailureAction.Abort)
                        {
                            results.Add(new TransferItemResult(item.SourcePath, finalDestinationPath, false, ItemOutcome.Failed, item.Size, null, transferError));
                            return new TransferResult(TransferState.Failed, results);
                        }
                        break; // Skip
                    }

                    break; // Default: record failure and continue
                }
            }

            if (transferSuccess)
            {
                // Cross-volume Move: Delete source file ONLY after destination has been verified and renamed
                if (plan.Request.Kind == TransferKind.Move && plan.IsCrossVolume)
                {
                    try
                    {
                        _fs.Delete(item.SourcePath);
                        if (outcome != ItemOutcome.Replaced)
                        {
                            outcome = ItemOutcome.Moved;
                        }
                    }
                    catch (Exception ex)
                    {
                        // Source deletion failed: report error, keep both copies
                        transferError = $"Destination created successfully, but source could not be deleted: {ex.Message}";
                    }
                }

                results.Add(new TransferItemResult(
                    item.SourcePath,
                    finalDestinationPath,
                    false,
                    outcome,
                    item.Size,
                    modifiedAfter,
                    transferError,
                    backupPath));
            }
            else
            {
                results.Add(new TransferItemResult(
                    item.SourcePath,
                    finalDestinationPath,
                    false,
                    ItemOutcome.Failed,
                    item.Size,
                    null,
                    transferError));
            }
        }

        // 3. For cross-volume Move: clean up empty source directories in reverse order
        if (plan.Request.Kind == TransferKind.Move && plan.IsCrossVolume)
        {
            var sourceDirsBottomUp = plan.Items
                .Where(i => i.IsDirectory)
                .OrderByDescending(i => i.SourcePath.Length);

            foreach (var dir in sourceDirsBottomUp)
            {
                try
                {
                    if (_fs.IsDirectoryEmpty(dir.SourcePath))
                    {
                        _fs.Delete(dir.SourcePath);
                    }
                }
                catch
                {
                    // Best effort cleanup of empty directories
                }
            }
        }

        bool anyErrors = results.Any(r => r.Outcome == ItemOutcome.Failed);
        return new TransferResult(anyErrors ? TransferState.CompletedWithErrors : TransferState.Completed, results);
    }

    private async Task<(DateTime Modified, string? BackupPath)> CopySingleFileAsync(
        string sourcePath,
        string destinationPath,
        string tempPath,
        long fileSize,
        DateTime sourceModified,
        ItemOutcome outcome,
        int itemIndex,
        TransferPlan plan,
        byte[] buffer,
        long baseOverallBytes,
        int filesDone,
        long startTimestamp,
        IProgress<TransferProgress>? progress,
        PauseGate? gate,
        CancellationToken ct)
    {
        IncrementalHash? sourceHasher = plan.Request.Options.VerifyAfterCopy
            ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
            : null;

        long currentFileBytes = 0;

        using (var sourceStream = _fs.OpenRead(sourcePath))
        using (var destStream = _fs.CreateFileForWrite(tempPath))
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                if (gate != null)
                {
                    await gate.WaitIfPausedAsync(ct).ConfigureAwait(false);
                }

                int read = await sourceStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
                if (read == 0) break;

                sourceHasher?.AppendData(buffer, 0, read);
                await destStream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);

                currentFileBytes += read;

                // Speed and ETA calculation
                long now = _timeProvider.GetTimestamp();
                double elapsedSec = Math.Max(0.001, (now - startTimestamp) / (double)_timeProvider.TimestampFrequency);
                double bps = (baseOverallBytes + currentFileBytes) / elapsedSec;
                long remainingBytes = Math.Max(0, plan.OverallTotalBytes - (baseOverallBytes + currentFileBytes));
                TimeSpan? eta = bps > 0 ? TimeSpan.FromSeconds(remainingBytes / bps) : null;

                progress?.Report(new TransferProgress(
                    TransferPhase.Transferring,
                    sourcePath,
                    currentFileBytes,
                    fileSize,
                    baseOverallBytes + currentFileBytes,
                    plan.OverallTotalBytes,
                    filesDone,
                    plan.FilesTotal,
                    bps,
                    eta));
            }

            await destStream.FlushAsync(ct).ConfigureAwait(false);
        }

        // Verification phase
        if (plan.Request.Options.VerifyAfterCopy && sourceHasher != null)
        {
            progress?.Report(new TransferProgress(
                TransferPhase.Verifying,
                sourcePath,
                currentFileBytes,
                fileSize,
                baseOverallBytes + currentFileBytes,
                plan.OverallTotalBytes,
                filesDone,
                plan.FilesTotal,
                0,
                null));

            byte[] sourceHash = sourceHasher.GetHashAndReset();
            sourceHasher.Dispose();

            byte[] destHash;
            using (var verifyStream = _fs.OpenRead(tempPath))
            using (var destHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                int verifyRead;
                while ((verifyRead = await verifyStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
                {
                    destHasher.AppendData(buffer, 0, verifyRead);
                }
                destHash = destHasher.GetHashAndReset();
            }

            if (!sourceHash.AsSpan().SequenceEqual(destHash))
            {
                throw new IOException($"Verification failed: SHA-256 mismatch for file '{sourcePath}'.");
            }
        }

        // Preserve timestamps
        DateTime modifiedUtc = sourceModified.Kind == DateTimeKind.Utc ? sourceModified : sourceModified.ToUniversalTime();
        if (plan.Request.Options.PreserveTimestamps)
        {
            _fs.SetModified(tempPath, modifiedUtc);
        }

        string? backupPath = null;
        if (outcome == ItemOutcome.Replaced)
        {
            if (_stager != null)
            {
                var staged = await _stager.StageAsync(destinationPath, plan.Request.OperationId, itemIndex, ct).ConfigureAwait(false);
                backupPath = staged.BackupPath;

                try
                {
                    _fs.Move(tempPath, destinationPath, overwrite: false);
                }
                catch (Exception moveEx)
                {
                    try
                    {
                        await _stager.RestoreAsync(backupPath, destinationPath, ct).ConfigureAwait(false);
                    }
                    catch
                    {
                        // Best effort rollback
                    }
                    CleanupTemp(tempPath);
                    throw new IOException($"Failed to place new file after staging backup: {moveEx.Message}", moveEx);
                }
            }
            else
            {
                _fs.Move(tempPath, destinationPath, overwrite: true);
            }
        }
        else
        {
            _fs.Move(tempPath, destinationPath, overwrite: false);
        }

        if (plan.Request.Options.PreserveTimestamps)
        {
            _fs.SetModified(destinationPath, modifiedUtc);
        }

        return (modifiedUtc, backupPath);
    }

    private void CleanupTemp(string tempPath)
    {
        try
        {
            if (_fs.FileExists(tempPath))
            {
                _fs.Delete(tempPath);
            }
        }
        catch
        {
            // Best effort temp cleanup
        }
    }

    private record ConflictResolutionResult(
        ConflictAction Action,
        string FinalDestinationPath,
        ItemOutcome Outcome,
        ConflictDecision? RememberedDecision);

    private async ValueTask<ConflictResolutionResult> ResolveConflictAsync(
        string sourcePath,
        string destinationPath,
        long sourceSize,
        DateTime sourceModified,
        bool isDirectory,
        TransferRequest request,
        ConflictDecision? rememberedConflict,
        CancellationToken ct)
    {
        ItemOutcome defaultOutcome = request.Kind == TransferKind.Move ? ItemOutcome.Moved : ItemOutcome.Copied;

        var existingInfo = _fs.GetInfo(destinationPath);
        if (existingInfo == null || !existingInfo.Exists)
        {
            return new ConflictResolutionResult(ConflictAction.Overwrite, destinationPath, defaultOutcome, rememberedConflict);
        }

        // Self-overwrite prevention (e.g. source == destination path)
        if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase))
        {
            return new ConflictResolutionResult(ConflictAction.Skip, destinationPath, ItemOutcome.Skipped, rememberedConflict);
        }

        // Directory vs Directory: automatically merges
        if (isDirectory && existingInfo.IsDirectory)
        {
            return new ConflictResolutionResult(ConflictAction.Overwrite, destinationPath, defaultOutcome, rememberedConflict);
        }

        // File vs Directory or Directory vs File mismatch: cannot overwrite/replace
        if (isDirectory != existingInfo.IsDirectory)
        {
            return new ConflictResolutionResult(ConflictAction.Skip, destinationPath, ItemOutcome.Failed, rememberedConflict);
        }

        // File vs File conflict
        ConflictDecision decision;
        if (rememberedConflict != null)
        {
            decision = rememberedConflict;
        }
        else if (request.Interaction != null)
        {
            var conflictInfo = new ConflictInfo(
                sourcePath,
                sourceSize,
                sourceModified,
                destinationPath,
                existingInfo.Size,
                existingInfo.Modified,
                OverwriteIsUndoable: (_stager != null));

            decision = await request.Interaction.OnConflictAsync(conflictInfo, ct).ConfigureAwait(false);
            if (decision.ApplyToAll)
            {
                rememberedConflict = decision;
            }
        }
        else
        {
            decision = new ConflictDecision(request.Options.DefaultConflict);
        }

        switch (decision.Action)
        {
            case ConflictAction.Cancel:
                return new ConflictResolutionResult(ConflictAction.Cancel, destinationPath, ItemOutcome.Skipped, rememberedConflict);

            case ConflictAction.Skip:
                return new ConflictResolutionResult(ConflictAction.Skip, destinationPath, ItemOutcome.Skipped, rememberedConflict);

            case ConflictAction.KeepBoth:
                string dir = Path.GetDirectoryName(destinationPath) ?? request.DestinationDirectory;
                string newName = TransferPlanner.GetKeepBothName(_fs, dir, Path.GetFileName(destinationPath), isDirectory);
                string newPath = Path.Combine(dir, newName);
                return new ConflictResolutionResult(ConflictAction.KeepBoth, newPath, ItemOutcome.KeptBoth, rememberedConflict);

            case ConflictAction.OverwriteIfNewer:
                if (sourceModified > existingInfo.Modified)
                {
                    return new ConflictResolutionResult(ConflictAction.Overwrite, destinationPath, ItemOutcome.Replaced, rememberedConflict);
                }
                return new ConflictResolutionResult(ConflictAction.Skip, destinationPath, ItemOutcome.Skipped, rememberedConflict);

            case ConflictAction.Overwrite:
            default:
                return new ConflictResolutionResult(ConflictAction.Overwrite, destinationPath, ItemOutcome.Replaced, rememberedConflict);
        }
    }

    private static void ReportItemProgress(
        IProgress<TransferProgress>? progress,
        string currentPath,
        int filesDone,
        int filesTotal)
    {
        progress?.Report(new TransferProgress(
            TransferPhase.Transferring,
            currentPath,
            0,
            0,
            0,
            0,
            filesDone,
            filesTotal,
            0,
            null));
    }
}
