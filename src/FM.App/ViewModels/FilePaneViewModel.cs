using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FM.App.Collections;
using FM.App.Helpers;
using FM.App.Services;
using FM.Core.Helpers;
using FM.Core.Models;
using FM.Core.Navigation;
using FM.Core.Services;

namespace FM.App.ViewModels;

public enum NavigationPhaseType
{
    New,
    Back,
    Forward,
    Refresh
}

/// <summary>
/// ViewModel managing the state, navigation, and file listing of a single file pane.
/// </summary>
public partial class FilePaneViewModel : ObservableObject
{
    private readonly IDirectoryService _directoryService;
    private readonly IIconService _iconService;
    private readonly NavigationHistory _history = new();
    private CancellationTokenSource? _loadCts;

    public BulkObservableCollection<FileItemViewModel> Items { get; } = new();

    [ObservableProperty]
    private string _currentPath = string.Empty;

    [ObservableProperty]
    private string _addressText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private FileItemViewModel? _selectedItem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectionStatusText))]
    private IReadOnlyList<FileItemViewModel> _selectedItems = Array.Empty<FileItemViewModel>();

    public string SelectionStatusText
    {
        get
        {
            int selectedCount = SelectedItems.Count;
            int totalCount = Items.Count;
            if (selectedCount == 0)
            {
                return $"{totalCount} items";
            }

            long totalSelectedBytes = SelectedItems.Where(i => !i.IsDirectory).Sum(i => i.Size);
            string formattedBytes = FormatHelper.FormatFileSize(totalSelectedBytes, isDirectory: false);

            return $"{selectedCount} of {totalCount} selected ({formattedBytes})";
        }
    }

    public void UpdateSelectedItems(IEnumerable<FileItemViewModel> selected)
    {
        SelectedItems = selected.ToList();
        SelectedItem = SelectedItems.Count > 0 ? SelectedItems[0] : null;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortStatusText))]
    private FileSortColumn _currentSortColumn = FileSortColumn.Name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortStatusText))]
    private bool _isSortAscending = true;

    public string SortStatusText => $"Sorted by {CurrentSortColumn} ({(IsSortAscending ? "▲ Asc" : "▼ Desc")})";

    public bool CanGoBack => _history.CanGoBack;
    public bool CanGoForward => _history.CanGoForward;
    public bool CanGoUp => !string.IsNullOrEmpty(_history.GetParentPath());

    public event Action<FilePaneViewModel>? Activated;

    public FilePaneViewModel(IDirectoryService directoryService, IIconService iconService)
    {
        _directoryService = directoryService;
        _iconService = iconService;
    }

    public void NotifyActivated()
    {
        Activated?.Invoke(this);
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    public async Task GoBackAsync()
    {
        string? target = _history.PeekBack();
        if (target != null)
        {
            await NavigateInternalAsync(target, NavigationPhaseType.Back);
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    public async Task GoForwardAsync()
    {
        string? target = _history.PeekForward();
        if (target != null)
        {
            await NavigateInternalAsync(target, NavigationPhaseType.Forward);
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoUp))]
    public async Task GoUpAsync()
    {
        string? parent = _history.GetParentPath();
        if (!string.IsNullOrEmpty(parent))
        {
            await NavigateAsync(parent);
        }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (!string.IsNullOrEmpty(CurrentPath))
        {
            await NavigateInternalAsync(CurrentPath, NavigationPhaseType.Refresh);
        }
    }

    [RelayCommand]
    public async Task NavigateAsync(string? path)
    {
        string target = string.IsNullOrWhiteSpace(path) ? AddressText : path;
        if (string.IsNullOrWhiteSpace(target)) return;

        await NavigateInternalAsync(target, NavigationPhaseType.New);
    }

    public void Navigate(string? path) => _ = NavigateAsync(path);
    public void Refresh() => _ = RefreshAsync();
    public void GoBack() => _ = GoBackAsync();
    public void GoForward() => _ = GoForwardAsync();
    public void GoUp() => _ = GoUpAsync();

    [RelayCommand]
    public async Task Sort(string columnStr)
    {
        if (!Enum.TryParse<FileSortColumn>(columnStr, ignoreCase: true, out var column)) return;

        if (CurrentSortColumn == column)
        {
            IsSortAscending = !IsSortAscending;
        }
        else
        {
            CurrentSortColumn = column;
            IsSortAscending = true;
        }

        await ApplySortAsync(Interlocked.Read(ref _navigationVersion), _loadCts?.Token ?? CancellationToken.None);
    }

    [RelayCommand]
    public void OpenItem(FileItemViewModel? item)
    {
        var target = item ?? SelectedItem;
        if (target == null) return;

        if (target.IsDirectory)
        {
            Navigate(target.FullPath);
        }
        else
        {
            try
            {
                // Launch file with registered default Windows application
                var psi = new ProcessStartInfo(target.FullPath)
                {
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Cannot open file: {ex.Message}";
            }
        }
    }

    [RelayCommand]
    private void DismissError()
    {
        ErrorMessage = null;
    }

    private long _navigationVersion;

    public async Task NavigateInternalAsync(string targetPath, NavigationPhaseType phaseType)
    {
        long currentVersion = Interlocked.Increment(ref _navigationVersion);

        // Cancel previous loading task so fast navigation never displays stale results
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        bool IsCurrent() => currentVersion == Interlocked.Read(ref _navigationVersion) && !ct.IsCancellationRequested;

        ErrorMessage = null;
        IsLoading = true;

        try
        {
            string resolvedPath = Path.GetFullPath(targetPath);
            if (!IsCurrent()) return;

            bool historyCommitted = false;
            void EnsureHistoryCommitted()
            {
                if (historyCommitted) return;
                historyCommitted = true;

                switch (phaseType)
                {
                    case NavigationPhaseType.New:
                        _history.CommitNavigate(resolvedPath);
                        break;
                    case NavigationPhaseType.Back:
                        _history.CommitBack();
                        break;
                    case NavigationPhaseType.Forward:
                        _history.CommitForward();
                        break;
                    case NavigationPhaseType.Refresh:
                        break;
                }

                CurrentPath = resolvedPath;
                AddressText = resolvedPath;
                UpdateHistoryCommandStates();
            }

            var previousSelectedPaths = phaseType == NavigationPhaseType.Refresh
                ? new HashSet<string>(SelectedItems.Select(i => i.FullPath), StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Clear view items immediately
            Items.ReplaceAll(Array.Empty<FileItemViewModel>());
            UpdateSelectedItems(Array.Empty<FileItemViewModel>());
            OnPropertyChanged(nameof(SelectionStatusText));

            var statusUpdateSw = Stopwatch.StartNew();
            var batchSw = Stopwatch.StartNew();
            int currentBatchSize = 50;
            await foreach (var batch in _directoryService.EnumerateBatchesAsync(resolvedPath, currentBatchSize, ct))
            {
                if (!IsCurrent()) return;
                EnsureHistoryCommitted();
                var batchVms = batch.Select(e => new FileItemViewModel(e, _iconService, ct)).ToList();
                Items.AddRange(batchVms);

                if (statusUpdateSw.ElapsedMilliseconds >= 100)
                {
                    OnPropertyChanged(nameof(SelectionStatusText));
                    statusUpdateSw.Restart();
                }

                if (System.Windows.Application.Current != null)
                {
                    await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                }
                else
                {
                    await Task.Yield();
                }
            }
            batchSw.Stop();

            if (!IsCurrent()) return;
            EnsureHistoryCommitted();

            // Apply folders-first sort order once enumeration completes
            await ApplySortAsync(currentVersion, ct);

            if (!IsCurrent()) return;

            // Restore all selected items by FullPath on Refresh
            if (previousSelectedPaths.Count > 0)
            {
                var restored = Items.Where(i => previousSelectedPaths.Contains(i.FullPath)).ToList();
                UpdateSelectedItems(restored);
            }

            OnPropertyChanged(nameof(SelectionStatusText));
        }
        catch (OperationCanceledException)
        {
            // Expected when user quickly navigates to another folder
        }
        catch (UnauthorizedAccessException)
        {
            if (IsCurrent())
            {
                ErrorMessage = $"Access denied: '{targetPath}'";
                Items.ReplaceAll(Array.Empty<FileItemViewModel>());
            }
        }
        catch (DirectoryNotFoundException)
        {
            if (IsCurrent())
            {
                ErrorMessage = $"Directory not found: '{targetPath}'";
                Items.ReplaceAll(Array.Empty<FileItemViewModel>());
            }
        }
        catch (IOException ex)
        {
            if (IsCurrent())
            {
                ErrorMessage = $"I/O Error: {ex.Message}";
                Items.ReplaceAll(Array.Empty<FileItemViewModel>());
            }
        }
        catch (Exception ex)
        {
            if (IsCurrent())
            {
                ErrorMessage = $"Error: {ex.Message}";
                Items.ReplaceAll(Array.Empty<FileItemViewModel>());
            }
        }
        finally
        {
            if (IsCurrent())
            {
                IsLoading = false;
                OnPropertyChanged(nameof(SelectionStatusText));
            }
        }
    }

    internal Func<Task>? SortGate { get; set; }

    public async Task ApplySortAsync(long? expectedVersion = null, CancellationToken cancellationToken = default)
    {
        if (Items.Count <= 1) return;

        long version = expectedVersion ?? Interlocked.Read(ref _navigationVersion);
        bool IsCurrent() => version == Interlocked.Read(ref _navigationVersion)
                         && !cancellationToken.IsCancellationRequested
                         && (_loadCts == null || !_loadCts.IsCancellationRequested);

        if (!IsCurrent()) return;

        var selectedPaths = new HashSet<string>(SelectedItems.Select(i => i.FullPath), StringComparer.OrdinalIgnoreCase);

        var currentItems = Items.ToList();
        var map = currentItems.ToDictionary(i => i.FullPath, StringComparer.OrdinalIgnoreCase);
        var entries = currentItems.Select(i => i.Entry).ToList();

        var sortCol = CurrentSortColumn;
        var isAsc = IsSortAscending;

        // Perform sorting off UI thread when list is large
        List<FileItemViewModel> sortedVms;
        if (currentItems.Count > 500)
        {
            sortedVms = await Task.Run(() =>
            {
                var sortedEntries = entries.OrderEntries(sortCol, isAsc);
                return sortedEntries.Select(e => map[e.FullPath]).ToList();
            }, cancellationToken);
        }
        else
        {
            var sortedEntries = entries.OrderEntries(sortCol, isAsc);
            sortedVms = sortedEntries.Select(e => map[e.FullPath]).ToList();
        }

        if (SortGate != null)
        {
            await SortGate();
        }

        if (!IsCurrent()) return;

        // Avoid rebuilding collection if the order did not change
        if (sortedVms.SequenceEqual(Items))
        {
            return;
        }

        Items.ReplaceAll(sortedVms);

        // Restore ALL selections by FullPath
        if (selectedPaths.Count > 0)
        {
            var restored = Items.Where(i => selectedPaths.Contains(i.FullPath)).ToList();
            UpdateSelectedItems(restored);
        }
    }

    private void UpdateHistoryCommandStates()
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(CanGoUp));

        GoBackCommand.NotifyCanExecuteChanged();
        GoForwardCommand.NotifyCanExecuteChanged();
        GoUpCommand.NotifyCanExecuteChanged();
    }
}
