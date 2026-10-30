using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FM.Core.Operations.Transfer;

namespace FM.App.ViewModels;

public sealed partial class TransferDrawerViewModel : ObservableObject
{
    private readonly ITransferQueue _queue;
    private readonly TimeProvider _timeProvider;
    private readonly Action<Action> _uiDispatcher;

    private readonly object _progressLock = new();
    private readonly Dictionary<Guid, TransferProgress> _pendingProgress = new();
    private bool _progressDispatchPending;
    private readonly Dictionary<Guid, ITimer> _autoRemoveTimers = new();

    public ObservableCollection<TransferJobViewModel> Jobs { get; } = new();

    [ObservableProperty]
    private bool _isExpanded;

    public bool HasJobs => Jobs.Count > 0;

    [ObservableProperty]
    private string _summaryText = "No transfers";

    public TransferDrawerViewModel(
        ITransferQueue queue,
        TimeProvider? timeProvider = null,
        Action<Action>? uiDispatcher = null)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _timeProvider = timeProvider ?? TimeProvider.System;

        _uiDispatcher = uiDispatcher ?? (action =>
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.InvokeAsync(action);
            }
            else
            {
                action();
            }
        });

        // Load existing jobs from queue snapshot
        var initialJobs = _queue.Snapshot();
        foreach (var job in initialJobs)
        {
            var vm = new TransferJobViewModel(job);
            Jobs.Add(vm);
        }

        if (Jobs.Count > 0)
        {
            _isExpanded = true;
            UpdateSummary();
        }

        _queue.ProgressChanged += OnQueueProgressChanged;
        _queue.StateChanged += OnQueueStateChanged;
    }

    [RelayCommand]
    public void ToggleExpanded()
    {
        IsExpanded = !IsExpanded;
    }

    [RelayCommand]
    public void ClearFinished()
    {
        var finished = Jobs.Where(j =>
            j.State == TransferState.Completed ||
            j.State == TransferState.CompletedWithErrors ||
            j.State == TransferState.Failed ||
            j.State == TransferState.Cancelled).ToList();

        foreach (var job in finished)
        {
            if (_autoRemoveTimers.TryGetValue(job.Id, out var timer))
            {
                timer.Dispose();
                _autoRemoveTimers.Remove(job.Id);
            }
            Jobs.Remove(job);
        }

        OnPropertyChanged(nameof(HasJobs));
        if (Jobs.Count == 0)
        {
            IsExpanded = false;
        }

        UpdateSummary();
    }

    private void OnQueueProgressChanged(object? sender, TransferProgressEventArgs e)
    {
        lock (_progressLock)
        {
            _pendingProgress[e.Job.Id] = e.Progress;
            if (!_progressDispatchPending)
            {
                _progressDispatchPending = true;
                _uiDispatcher(FlushPendingProgress);
            }
        }
    }

    private void FlushPendingProgress()
    {
        Dictionary<Guid, TransferProgress> snapshot;
        lock (_progressLock)
        {
            snapshot = new Dictionary<Guid, TransferProgress>(_pendingProgress);
            _pendingProgress.Clear();
            _progressDispatchPending = false;
        }

        foreach (var (jobId, progress) in snapshot)
        {
            var jobVm = Jobs.FirstOrDefault(j => j.Id == jobId);
            jobVm?.UpdateProgress(progress);
        }

        UpdateSummary();
    }

    private void OnQueueStateChanged(object? sender, TransferStateChangedEventArgs e)
    {
        _uiDispatcher(() =>
        {
            var jobVm = Jobs.FirstOrDefault(j => j.Id == e.Job.Id);
            if (jobVm == null)
            {
                jobVm = new TransferJobViewModel(e.Job);
                Jobs.Add(jobVm);
                OnPropertyChanged(nameof(HasJobs));
                if (Jobs.Count == 1)
                {
                    IsExpanded = true;
                }
            }

            jobVm.UpdateState(e.NewState);

            if (e.NewState == TransferState.Completed)
            {
                // Auto remove completed job after ~8 seconds
                if (_autoRemoveTimers.TryGetValue(e.Job.Id, out var existingTimer))
                {
                    existingTimer.Dispose();
                }

                var timer = _timeProvider.CreateTimer(
                    _ => OnAutoRemoveJob(e.Job.Id),
                    null,
                    TimeSpan.FromSeconds(8),
                    Timeout.InfiniteTimeSpan);

                _autoRemoveTimers[e.Job.Id] = timer;
            }

            UpdateSummary();
        });
    }

    private void OnAutoRemoveJob(Guid jobId)
    {
        _uiDispatcher(() =>
        {
            if (_autoRemoveTimers.TryGetValue(jobId, out var timer))
            {
                timer.Dispose();
                _autoRemoveTimers.Remove(jobId);
            }

            var jobVm = Jobs.FirstOrDefault(j => j.Id == jobId);
            if (jobVm != null && jobVm.State == TransferState.Completed)
            {
                Jobs.Remove(jobVm);
                OnPropertyChanged(nameof(HasJobs));
                if (Jobs.Count == 0)
                {
                    IsExpanded = false;
                }
                UpdateSummary();
            }
        });
    }

    public void UpdateSummary()
    {
        int running = Jobs.Count(j => j.State == TransferState.Running);
        int queued = Jobs.Count(j => j.State == TransferState.Queued);

        if (running > 0 || queued > 0)
        {
            var parts = new List<string>();
            if (running > 0) parts.Add($"{running} running");
            if (queued > 0) parts.Add($"{queued} queued");

            // Calculate aggregate percent & speed across running jobs
            long totalBytes = 0;
            long totalExpected = 0;
            double totalSpeed = 0;
            bool hasValidPercent = false;

            foreach (var job in Jobs.Where(j => j.State == TransferState.Running))
            {
                if (job.Job.LastProgress != null)
                {
                    var lp = job.Job.LastProgress;
                    if (lp.OverallTotal.HasValue && lp.OverallTotal.Value > 0)
                    {
                        totalBytes += lp.OverallBytes;
                        totalExpected += lp.OverallTotal.Value;
                        hasValidPercent = true;
                    }
                    if (lp.BytesPerSecond > 0)
                    {
                        totalSpeed += lp.BytesPerSecond;
                    }
                }
            }

            if (hasValidPercent && totalExpected > 0)
            {
                double aggPercent = Math.Clamp((double)totalBytes / totalExpected * 100.0, 0.0, 100.0);
                parts.Add($"{aggPercent:F0}%");
            }

            if (totalSpeed > 0)
            {
                parts.Add(TransferJobViewModel.FormatSpeed(totalSpeed));
            }

            SummaryText = string.Join(" · ", parts);
        }
        else if (Jobs.Count > 0)
        {
            SummaryText = $"{Jobs.Count} transfer(s) finished";
        }
        else
        {
            SummaryText = "No active transfers";
        }
    }
}

