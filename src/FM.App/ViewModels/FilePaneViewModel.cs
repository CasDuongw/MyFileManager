using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FM.App.Collections;
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
    private void GoBack()
    {
        string? target = _history.PeekBack();
        if (target != null)
        {
            _ = NavigateInternalAsync(target, NavigationPhaseType.Back);
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void GoForward()
    {
        string? target = _history.PeekForward();
        if (target != null)
        {
            _ = NavigateInternalAsync(target, NavigationPhaseType.Forward);
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoUp))]
    public void GoUp()
    {
        string? parent = _history.GetParentPath();
        if (!string.IsNullOrEmpty(parent))
        {
            Navigate(parent);
        }
    }

    [RelayCommand]
    public void Refresh()
    {
        if (!string.IsNullOrEmpty(CurrentPath))
        {
            _ = NavigateInternalAsync(CurrentPath, NavigationPhaseType.Refresh);
        }
    }

    [RelayCommand]
    public void Navigate(string? path)
    {
        string target = string.IsNullOrWhiteSpace(path) ? AddressText : path;
        if (string.IsNullOrWhiteSpace(target)) return;

        _ = NavigateInternalAsync(target, NavigationPhaseType.New);
    }

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

        await ApplySortAsync();
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

            if (!Directory.Exists(resolvedPath))
            {
                if (!IsCurrent()) return;
                ErrorMessage = $"Directory not found: '{targetPath}'";
                IsLoading = false;
                UpdateHistoryCommandStates();
                return;
            }

            if (!IsCurrent()) return;

            // Commit history now that directory existence is confirmed
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

            string? previousSelection = SelectedItem?.FullPath;

            // Clear view items immediately
            Items.ReplaceAll(Array.Empty<FileItemViewModel>());

            await foreach (var batch in _directoryService.EnumerateBatchesAsync(resolvedPath, 150, ct))
            {
                if (!IsCurrent()) return;
                var batchVms = batch.Select(e => new FileItemViewModel(e, _iconService, () => _loadCts?.Token ?? CancellationToken.None)).ToList();
                Items.AddRange(batchVms);
            }

            if (!IsCurrent()) return;

            // Apply folders-first sort order once enumeration completes
            await ApplySortAsync();

            if (previousSelection != null && SelectedItem == null)
            {
                SelectedItem = Items.FirstOrDefault(i => string.Equals(i.FullPath, previousSelection, StringComparison.OrdinalIgnoreCase));
            }
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
            }
        }
    }

    public async Task ApplySortAsync()
    {
        if (Items.Count <= 1) return;

        string? selectedPath = SelectedItem?.FullPath;

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
            });
        }
        else
        {
            var sortedEntries = entries.OrderEntries(sortCol, isAsc);
            sortedVms = sortedEntries.Select(e => map[e.FullPath]).ToList();
        }

        // Avoid rebuilding collection if the order did not change
        if (sortedVms.SequenceEqual(Items))
        {
            return;
        }

        Items.ReplaceAll(sortedVms);

        // Restore selection by FullPath
        if (selectedPath != null)
        {
            SelectedItem = Items.FirstOrDefault(i => string.Equals(i.FullPath, selectedPath, StringComparison.OrdinalIgnoreCase));
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
