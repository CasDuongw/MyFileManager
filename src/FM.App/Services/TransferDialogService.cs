using System.Windows;
using FM.App.Views;
using FM.Core.Operations.Transfer;

namespace FM.App.Services;

/// <summary>
/// Production implementation of <see cref="ITransferDialogService"/> presenting WPF modal dialogs
/// dispatched onto the UI thread without blocking or deadlocking background engine tasks.
/// </summary>
public class TransferDialogService : ITransferDialogService
{
    private readonly IThumbnailService? _thumbnailService;

    public TransferDialogService(IThumbnailService? thumbnailService = null)
    {
        _thumbnailService = thumbnailService;
    }

    public async Task<ConflictDecision> ShowConflictDialogAsync(ConflictInfo info, CancellationToken ct = default)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted)
        {
            return new ConflictDecision(ConflictAction.KeepBoth);
        }

        if (ct.IsCancellationRequested)
        {
            return new ConflictDecision(ConflictAction.Cancel);
        }

        var tcs = new TaskCompletionSource<ConflictDecision>(TaskCreationOptions.RunContinuationsAsynchronously);

        await dispatcher.InvokeAsync(() =>
        {
            if (ct.IsCancellationRequested)
            {
                tcs.TrySetResult(new ConflictDecision(ConflictAction.Cancel));
                return;
            }

            var dialog = new ConflictDialog(info, _thumbnailService);
            var mainWindow = Application.Current?.MainWindow;
            if (mainWindow != null && mainWindow.IsVisible)
            {
                dialog.Owner = mainWindow;
            }

            CancellationTokenRegistration ctr = default;
            if (ct.CanBeCanceled)
            {
                ctr = ct.Register(() =>
                {
                    dispatcher.InvokeAsync(() =>
                    {
                        if (dialog.IsLoaded)
                        {
                            dialog.Close();
                        }
                    });
                });
            }

            dialog.Closed += (s, e) =>
            {
                ctr.Dispose();
                if (ct.IsCancellationRequested)
                {
                    tcs.TrySetResult(new ConflictDecision(ConflictAction.Cancel));
                }
                else
                {
                    tcs.TrySetResult(dialog.Decision);
                }
            };

            try
            {
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                ctr.Dispose();
                tcs.TrySetException(ex);
            }
        });

        return await tcs.Task;
    }

    public async Task<FailureDecision> ShowFailureDialogAsync(FailureInfo info, CancellationToken ct = default)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted)
        {
            return new FailureDecision(FailureAction.Skip);
        }

        if (ct.IsCancellationRequested)
        {
            return new FailureDecision(FailureAction.Abort);
        }

        var tcs = new TaskCompletionSource<FailureDecision>(TaskCreationOptions.RunContinuationsAsynchronously);

        await dispatcher.InvokeAsync(() =>
        {
            if (ct.IsCancellationRequested)
            {
                tcs.TrySetResult(new FailureDecision(FailureAction.Abort));
                return;
            }

            var dialog = new FailureDialog(info);
            var mainWindow = Application.Current?.MainWindow;
            if (mainWindow != null && mainWindow.IsVisible)
            {
                dialog.Owner = mainWindow;
            }

            CancellationTokenRegistration ctr = default;
            if (ct.CanBeCanceled)
            {
                ctr = ct.Register(() =>
                {
                    dispatcher.InvokeAsync(() =>
                    {
                        if (dialog.IsLoaded)
                        {
                            dialog.Close();
                        }
                    });
                });
            }

            dialog.Closed += (s, e) =>
            {
                ctr.Dispose();
                if (ct.IsCancellationRequested)
                {
                    tcs.TrySetResult(new FailureDecision(FailureAction.Abort));
                }
                else
                {
                    tcs.TrySetResult(dialog.Decision);
                }
            };

            try
            {
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                ctr.Dispose();
                tcs.TrySetException(ex);
            }
        });

        return await tcs.Task;
    }
}

