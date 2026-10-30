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
    private FileSortColumn _currentSortColumn = FileSortColumn.Name;

    [ObservableProperty]
    private bool _isSortAscending = true;

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
        string? path = _history.GoBack();
        if (path != null)
        {
            _ = NavigateInternalAsync(path, recordHistory: false);
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void GoForward()
    {
        string? path = _history.GoForward();
        if (path != null)
        {
            _ = NavigateInternalAsync(path, recordHistory: false);
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
            _ = NavigateInternalAsync(CurrentPath, recordHistory: false);
        }
    }

    [RelayCommand]
    public void Navigate(string? path)
    {
        string target = string.IsNullOrWhiteSpace(path) ? AddressText : path;
        if (string.IsNullOrWhiteSpace(target)) return;

        _ = NavigateInternalAsync(target, recordHistory: true);
    }

    [RelayCommand]
    public void Sort(string columnStr)
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

        ApplySort();
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

    public async Task NavigateInternalAsync(string targetPath, bool recordHistory)
    {
        // Cancel previous loading task so fast navigation never displays stale results
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        ErrorMessage = null;
        IsLoading = true;

        try
        {
            string resolvedPath = Path.GetFullPath(targetPath);

            if (!Directory.Exists(resolvedPath))
            {
                ErrorMessage = $"Directory not found: '{targetPath}'";
                IsLoading = false;
                return;
            }

            if (recordHistory)
            {
                _history.Navigate(resolvedPath);
            }

            CurrentPath = resolvedPath;
            AddressText = resolvedPath;
            UpdateHistoryCommandStates();

            // Clear view items immediately
            Items.ReplaceAll(Array.Empty<FileItemViewModel>());

            var buffer = new List<FileItemViewModel>(150);

            await foreach (var entry in _directoryService.EnumerateAsync(resolvedPath, ct))
            {
                var itemVm = new FileItemViewModel(entry);
                buffer.Add(itemVm);

                // Batch dispatch to UI collection to eliminate per-item layout invalidation
                if (buffer.Count >= 150)
                {
                    var batch = buffer.ToList();
                    buffer.Clear();
                    Items.AddRange(batch);
                    _ = LoadIconsForBatchAsync(batch, ct);
                }
            }

            if (buffer.Count > 0)
            {
                var batch = buffer.ToList();
                buffer.Clear();
                Items.AddRange(batch);
                _ = LoadIconsForBatchAsync(batch, ct);
            }

            // Apply folders-first sort order once enumeration completes
            ApplySort();
        }
        catch (OperationCanceledException)
        {
            // Expected when user quickly navigates to another folder
        }
        catch (UnauthorizedAccessException)
        {
            ErrorMessage = $"Access denied: '{targetPath}'";
            Items.ReplaceAll(Array.Empty<FileItemViewModel>());
        }
        catch (DirectoryNotFoundException)
        {
            ErrorMessage = $"Directory not found: '{targetPath}'";
            Items.ReplaceAll(Array.Empty<FileItemViewModel>());
        }
        catch (IOException ex)
        {
            ErrorMessage = $"I/O Error: {ex.Message}";
            Items.ReplaceAll(Array.Empty<FileItemViewModel>());
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error: {ex.Message}";
            Items.ReplaceAll(Array.Empty<FileItemViewModel>());
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                IsLoading = false;
            }
        }
    }

    private void ApplySort()
    {
        if (Items.Count == 0) return;

        // Group by full path to preserve previously resolved icon references
        var map = Items.ToDictionary(i => i.FullPath, StringComparer.OrdinalIgnoreCase);
        var sortedEntries = Items.Select(i => i.Entry).OrderEntries(CurrentSortColumn, IsSortAscending);
        var sortedVms = sortedEntries.Select(e => map[e.FullPath]).ToList();

        Items.ReplaceAll(sortedVms);
    }

    private async Task LoadIconsForBatchAsync(List<FileItemViewModel> batch, CancellationToken ct)
    {
        foreach (var item in batch)
        {
            if (ct.IsCancellationRequested) break;
            await item.LoadIconAsync(_iconService, ct);
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
