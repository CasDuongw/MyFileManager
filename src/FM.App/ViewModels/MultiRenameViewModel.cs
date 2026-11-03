using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FM.Core.FileSystem;
using FM.Core.Operations;
using FM.Core.Operations.MultiRename;

namespace FM.App.ViewModels;

public sealed class MultiRenameRowViewModel : ObservableObject
{
    public string OriginalName { get; }
    public string NewName { get; }
    public MultiRenameStatus Status { get; }
    public string? ErrorMessage { get; }
    public bool IsDirectory { get; }

    public string StatusText => Status switch
    {
        MultiRenameStatus.Valid => "OK",
        MultiRenameStatus.Unchanged => "Unchanged",
        MultiRenameStatus.DiskCollision => "Disk Conflict",
        MultiRenameStatus.BatchCollision => "Duplicate Name",
        MultiRenameStatus.InvalidName => "Invalid Name",
        _ => Status.ToString()
    };

    public string StatusColor => Status switch
    {
        MultiRenameStatus.Valid => "#059669",      // Green
        MultiRenameStatus.Unchanged => "#6B7280",  // Gray
        _ => "#DC2626"                            // Red
    };

    public MultiRenameRowViewModel(MultiRenamePlanItem item)
    {
        OriginalName = item.OriginalName;
        NewName = item.NewName;
        Status = item.Status;
        ErrorMessage = item.ErrorMessage;
        IsDirectory = item.IsDirectory;
    }
}

public sealed partial class MultiRenameViewModel : ObservableObject
{
    private readonly IReadOnlyList<MultiRenameItemInput> _inputs;
    private readonly IFileSystem _fileSystem;
    private readonly Func<MultiRenamePlan, Task<OperationResult>> _executeCallback;
    private CancellationTokenSource? _previewCts;

    public event Action<bool>? RequestClose;

    public string TargetDirectory { get; }
    public int TotalItemCount => _inputs.Count;

    [ObservableProperty]
    private string _pattern = "[N].[E]";

    [ObservableProperty]
    private ExtensionCase _extCase = ExtensionCase.Keep;

    [ObservableProperty]
    private int _counterStart = 1;

    [ObservableProperty]
    private int _counterStep = 1;

    [ObservableProperty]
    private int _counterPadding = 1;

    [ObservableProperty]
    private string? _findText;

    [ObservableProperty]
    private string? _replaceText;

    [ObservableProperty]
    private bool _matchCase;

    [ObservableProperty]
    private bool _useRegex;

    [ObservableProperty]
    private string _statusSummary = "Ready";

    [ObservableProperty]
    private bool _canExecute;

    [ObservableProperty]
    private bool _isCalculating;

    public ObservableCollection<MultiRenameRowViewModel> Rows { get; } = new();

    public MultiRenamePlan? CurrentPlan { get; private set; }

    public MultiRenameViewModel(
        IReadOnlyList<MultiRenameItemInput> inputs,
        IFileSystem fileSystem,
        Func<MultiRenamePlan, Task<OperationResult>> executeCallback)
    {
        _inputs = inputs ?? throw new ArgumentNullException(nameof(inputs));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _executeCallback = executeCallback ?? throw new ArgumentNullException(nameof(executeCallback));

        TargetDirectory = inputs.Count > 0 ? (Path.GetDirectoryName(inputs[0].FullPath) ?? "") : "";

        // Trigger initial preview computation
        SchedulePreviewCalculation();
    }

    partial void OnPatternChanged(string value) => SchedulePreviewCalculation();
    partial void OnExtCaseChanged(ExtensionCase value) => SchedulePreviewCalculation();
    partial void OnCounterStartChanged(int value) => SchedulePreviewCalculation();
    partial void OnCounterStepChanged(int value) => SchedulePreviewCalculation();
    partial void OnCounterPaddingChanged(int value) => SchedulePreviewCalculation();
    partial void OnFindTextChanged(string? value) => SchedulePreviewCalculation();
    partial void OnReplaceTextChanged(string? value) => SchedulePreviewCalculation();
    partial void OnMatchCaseChanged(bool value) => SchedulePreviewCalculation();
    partial void OnUseRegexChanged(bool value) => SchedulePreviewCalculation();

    [RelayCommand]
    public void InsertToken(string token)
    {
        if (string.IsNullOrEmpty(token)) return;
        Pattern = string.IsNullOrEmpty(Pattern) ? token : Pattern + token;
    }

    [RelayCommand]
    public async Task RenameAsync()
    {
        if (CurrentPlan == null || !CanExecute) return;

        IsCalculating = true;
        StatusSummary = "Renaming items...";

        try
        {
            var result = await _executeCallback(CurrentPlan).ConfigureAwait(true);
            if (result.Success)
            {
                RequestClose?.Invoke(true);
            }
            else
            {
                StatusSummary = result.ErrorMessage ?? "Rename failed.";
                CanExecute = false;
            }
        }
        catch (Exception ex)
        {
            StatusSummary = $"Error: {ex.Message}";
            CanExecute = false;
        }
        finally
        {
            IsCalculating = false;
        }
    }

    [RelayCommand]
    public void Cancel()
    {
        RequestClose?.Invoke(false);
    }

    public Action<Action>? CustomDispatcher { get; set; }

    private void RunOnUi(Action action)
    {
        if (CustomDispatcher != null)
        {
            CustomDispatcher(action);
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.HasShutdownStarted && !dispatcher.CheckAccess())
        {
            try
            {
                dispatcher.Invoke(action);
                return;
            }
            catch
            {
                // Fallback to direct execution
            }
        }

        action();
    }

    public async Task CalculatePreviewAsync(CancellationToken ct = default)
    {
        var options = new MultiRenameOptions(
            Pattern: Pattern,
            ExtCase: ExtCase,
            CounterStart: CounterStart,
            CounterStep: CounterStep,
            CounterPadding: CounterPadding,
            FindText: FindText,
            ReplaceText: ReplaceText,
            MatchCase: MatchCase,
            UseRegex: UseRegex);

        var plan = await Task.Run(() => MultiRenamePlanner.Plan(_inputs, options, _fileSystem), ct).ConfigureAwait(false);
        var rowList = plan.Items.Select(item => new MultiRenameRowViewModel(item)).ToList();

        RunOnUi(() =>
        {
            if (ct.IsCancellationRequested) return;

            CurrentPlan = plan;
            Rows.Clear();
            foreach (var r in rowList)
            {
                Rows.Add(r);
            }

            if (plan.HasErrors)
            {
                StatusSummary = $"{plan.ErrorCount} conflict(s) or invalid name(s) detected. Fix errors to proceed.";
                CanExecute = false;
            }
            else if (plan.TotalChanged == 0)
            {
                StatusSummary = "No files will be modified (all names unchanged).";
                CanExecute = false;
            }
            else
            {
                StatusSummary = $"{plan.TotalChanged} of {plan.Items.Count} item(s) will be renamed.";
                CanExecute = true;
            }

            IsCalculating = false;
        });
    }

    public void SchedulePreviewCalculation()
    {
        _previewCts?.Cancel();
        _previewCts?.Dispose();
        _previewCts = new CancellationTokenSource();
        var ct = _previewCts.Token;

        IsCalculating = true;

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(30, ct).ConfigureAwait(false);
                await CalculatePreviewAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected when new calculation is scheduled
            }
            catch (Exception ex)
            {
                RunOnUi(() =>
                {
                    StatusSummary = $"Preview error: {ex.Message}";
                    CanExecute = false;
                    IsCalculating = false;
                });
            }
        }, ct);
    }
}
