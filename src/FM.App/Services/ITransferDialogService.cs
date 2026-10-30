using FM.Core.Operations.Transfer;

namespace FM.App.Services;

/// <summary>
/// Service abstraction for presenting transfer conflict and failure dialogs to the user.
/// Decouples UI presentation from transfer coordination for unit testability.
/// </summary>
public interface ITransferDialogService
{
    Task<ConflictDecision> ShowConflictDialogAsync(ConflictInfo info, CancellationToken ct = default);
    Task<FailureDecision> ShowFailureDialogAsync(FailureInfo info, CancellationToken ct = default);
}
