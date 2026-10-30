using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FM.Core.Operations.Transfer;

namespace FM.App.ViewModels;

public sealed partial class TransferJobViewModel : ObservableObject
{
    private readonly ITransferJob _job;
    private bool _isCancelling;

    public ITransferJob Job => _job;
    public Guid Id => _job.Id;
    public TransferKind Kind => _job.Request.Kind;

    public string Title
    {
        get
        {
            string verb = Kind == TransferKind.Copy ? "Copying" : "Moving";
            int count = _job.Request.Sources.Count;
            string countText = count == 1 ? "1 item" : $"{count} items";
            return $"{verb} {countText} → {_job.Request.DestinationDirectory}";
        }
    }

    [ObservableProperty]
    private string _currentFile = string.Empty;

    [ObservableProperty]
    private double _percent;

    [ObservableProperty]
    private bool _isIndeterminate;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private TransferState _state;

    [ObservableProperty]
    private string _stateText = string.Empty;

    public TransferJobViewModel(ITransferJob job)
    {
        _job = job ?? throw new ArgumentNullException(nameof(job));
        _state = job.State;
        UpdateState(job.State);
    }

    [RelayCommand(CanExecute = nameof(CanPause))]
    public void Pause()
    {
        _job.Pause();
    }

    private bool CanPause() => State == TransferState.Running;

    [RelayCommand(CanExecute = nameof(CanResume))]
    public void Resume()
    {
        _job.Resume();
    }

    private bool CanResume() => State == TransferState.Paused || State == TransferState.Pausing;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    public async Task CancelAsync()
    {
        _isCancelling = true;
        StateText = "Cancelling...";
        ProgressText = "Cancelling...";
        CancelCommand.NotifyCanExecuteChanged();
        await _job.CancelAsync().ConfigureAwait(false);
    }

    private bool CanCancel() =>
        !_isCancelling &&
        (State == TransferState.Queued ||
         State == TransferState.Running ||
         State == TransferState.Pausing ||
         State == TransferState.Paused);

    public void UpdateProgress(TransferProgress p)
    {
        CurrentFile = string.IsNullOrWhiteSpace(p.CurrentPath)
            ? string.Empty
            : System.IO.Path.GetFileName(p.CurrentPath) ?? p.CurrentPath;

        if (p.Phase == TransferPhase.Scanning)
        {
            IsIndeterminate = true;
            Percent = 0;
            ProgressText = "Scanning files...";
            return;
        }

        if (p.OverallTotal.HasValue && p.OverallTotal.Value > 0)
        {
            IsIndeterminate = false;
            Percent = Math.Clamp((double)p.OverallBytes / p.OverallTotal.Value * 100.0, 0.0, 100.0);
        }
        else
        {
            IsIndeterminate = State == TransferState.Running;
            Percent = 0;
        }

        var parts = new List<string>();
        if (p.FilesTotal.HasValue && p.FilesTotal.Value > 0)
        {
            parts.Add($"{p.FilesDone} of {p.FilesTotal.Value}");
        }
        else if (p.FilesDone > 0)
        {
            parts.Add($"{p.FilesDone} items");
        }

        if (p.OverallTotal.HasValue && p.OverallTotal.Value > 0)
        {
            parts.Add($"{Percent:F0}%");
        }

        if (p.BytesPerSecond > 0)
        {
            parts.Add(FormatSpeed(p.BytesPerSecond));
        }

        if (p.Eta.HasValue && p.Eta.Value > TimeSpan.Zero)
        {
            parts.Add($"{FormatTime(p.Eta.Value)} left");
        }

        ProgressText = parts.Count > 0 ? string.Join(" · ", parts) : "In progress...";
    }

    public void UpdateState(TransferState newState)
    {
        State = newState;
        StateText = FormatStateText(newState);

        switch (newState)
        {
            case TransferState.Queued:
                ProgressText = $"Waiting for drive {_job.DestinationVolumeId}...";
                IsIndeterminate = false;
                Percent = 0;
                break;
            case TransferState.Paused:
                ProgressText = "Paused";
                IsIndeterminate = false;
                break;
            case TransferState.Pausing:
                ProgressText = "Pausing...";
                break;
            case TransferState.Cancelled:
                ProgressText = "Cancelled";
                IsIndeterminate = false;
                break;
            case TransferState.Completed:
                ProgressText = "Completed";
                Percent = 100;
                IsIndeterminate = false;
                break;
            case TransferState.CompletedWithErrors:
                ProgressText = "Completed with errors";
                IsIndeterminate = false;
                break;
            case TransferState.Failed:
                ProgressText = "Failed";
                IsIndeterminate = false;
                break;
        }

        PauseCommand.NotifyCanExecuteChanged();
        ResumeCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    private static string FormatStateText(TransferState state) => state switch
    {
        TransferState.Queued => "Queued",
        TransferState.Running => "Running",
        TransferState.Pausing => "Pausing",
        TransferState.Paused => "Paused",
        TransferState.Completed => "Completed",
        TransferState.CompletedWithErrors => "Completed with warnings",
        TransferState.Failed => "Failed",
        TransferState.Cancelled => "Cancelled",
        _ => state.ToString()
    };

    public static string FormatSpeed(double bytesPerSec)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (bytesPerSec >= 1024 * 1024 * 1024)
            return $"{(bytesPerSec / (1024 * 1024 * 1024)).ToString("0.0", inv)} GB/s";
        if (bytesPerSec >= 1024 * 1024)
            return $"{(bytesPerSec / (1024 * 1024)).ToString("0.0", inv)} MB/s";
        if (bytesPerSec >= 1024)
            return $"{(bytesPerSec / 1024).ToString("0", inv)} KB/s";
        return $"{((int)bytesPerSec).ToString(inv)} B/s";
    }

    public static string FormatTime(TimeSpan span)
    {
        if (span.TotalHours >= 1)
            return $"{span.Hours}:{span.Minutes:D2}:{span.Seconds:D2}";
        return $"{span.Minutes:D2}:{span.Seconds:D2}";
    }
}
