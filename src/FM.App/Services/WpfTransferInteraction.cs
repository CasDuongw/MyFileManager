using FM.Core.Operations.Transfer;

namespace FM.App.Services;

/// <summary>
/// WPF adapter for <see cref="ITransferInteraction"/> that routes conflict and failure decisions
/// to <see cref="ITransferDialogService"/> and triggers waiting notifications.
/// </summary>
public class WpfTransferInteraction : ITransferInteraction
{
    private readonly ITransferDialogService _dialogService;
    private readonly Action? _onWaiting;

    public WpfTransferInteraction(ITransferDialogService dialogService, Action? onWaiting = null)
    {
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _onWaiting = onWaiting;
    }

    public async ValueTask<ConflictDecision> OnConflictAsync(ConflictInfo info, CancellationToken ct)
    {
        _onWaiting?.Invoke();
        return await _dialogService.ShowConflictDialogAsync(info, ct).ConfigureAwait(false);
    }

    public async ValueTask<FailureDecision> OnFailureAsync(FailureInfo info, CancellationToken ct)
    {
        _onWaiting?.Invoke();
        return await _dialogService.ShowFailureDialogAsync(info, ct).ConfigureAwait(false);
    }
}
