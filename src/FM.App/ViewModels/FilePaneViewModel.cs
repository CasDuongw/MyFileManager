using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FM.App.Collections;
using FM.App.Helpers;
using FM.App.Models;
using FM.App.Services;
using FM.Core.FileSystem;
using FM.Core.Helpers;
using FM.Core.Models;
using FM.Core.Navigation;
using FM.Core.Operations;
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
    private readonly IFileSystem _fileSystem;
    private readonly IInputDialogService _inputDialogService;
    private readonly IShellLauncher _shellLauncher;
    private readonly NavigationHistory _history = new();
    private CancellationTokenSource? _loadCts;

    [ObservableProperty]
    private bool _showHiddenFiles;

    public IUndoStack UndoStack { get; }
    public IFileSystem FileSystem => _fileSystem;

    private readonly object _itemsLock = new();
    public BulkObservableCollection<FileItemViewModel> Items { get; } = new();

    [ObservableProperty]
    private string _currentPath = string.Empty;

    partial void OnCurrentPathChanged(string value)
    {
        NewFolderCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CurrentFolderName));
        OnPropertyChanged(nameof(Breadcrumbs));
    }

    public string CurrentFolderName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(CurrentPath)) return "Home";
            try
            {
                string trimmed = CurrentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string name = Path.GetFileName(trimmed);
                return string.IsNullOrEmpty(name) ? CurrentPath : name;
            }
            catch
            {
                return CurrentPath;
            }
        }
    }

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs => GenerateBreadcrumbs(CurrentPath);

    [ObservableProperty]
    private FilePaneViewMode _viewMode = FilePaneViewMode.Details;

    [RelayCommand]
    public void SetViewMode(string modeStr)
    {
        if (Enum.TryParse<FilePaneViewMode>(modeStr, ignoreCase: true, out var mode))
        {
            ViewMode = mode;
        }
    }

    [ObservableProperty]
    private bool _isEditingAddress;

    [RelayCommand]
    public void StartEditingAddress()
    {
        IsEditingAddress = true;
    }

    [RelayCommand]
    public void StopEditingAddress()
    {
        IsEditingAddress = false;
    }

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _addressText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    partial void OnIsLoadingChanged(bool value)
    {
        NewFolderCommand.NotifyCanExecuteChanged();
        RenameCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private FileItemViewModel? _selectedItem;

    partial void OnSelectedItemChanged(FileItemViewModel? value)
    {
        RenameCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(SelectedItemName));
        OnPropertyChanged(nameof(SelectedItemType));
        OnPropertyChanged(nameof(SelectedItemSize));
        OnPropertyChanged(nameof(SelectedItemModified));
        OnPropertyChanged(nameof(SelectedItemPath));
        OnPropertyChanged(nameof(SelectedItemIcon));
        OnPropertyChanged(nameof(HasSelectedItem));
    }

    public string SelectedItemName => SelectedItem?.Name ?? "No item selected";
    public string SelectedItemType => SelectedItem?.DisplayType ?? "";
    public string SelectedItemSize => SelectedItem?.FormattedSize ?? "";
    public string SelectedItemModified => SelectedItem?.FormattedModified ?? "";
    public string SelectedItemPath => SelectedItem?.FullPath ?? "";
    public ImageSource? SelectedItemIcon => SelectedItem?.Icon;
    public bool HasSelectedItem => SelectedItem != null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectionStatusText))]
    private IReadOnlyList<FileItemViewModel> _selectedItems = Array.Empty<FileItemViewModel>();

    private string? _toastMessage;
    private System.Threading.Timer? _toastTimer;

    public void SetStatusText(string? message, TimeSpan? autoDismissAfter = null)
    {
        _toastMessage = message;
        RunOnUi(() => OnPropertyChanged(nameof(SelectionStatusText)));

        _toastTimer?.Dispose();
        if (autoDismissAfter.HasValue)
        {
            _toastTimer = new System.Threading.Timer(_ =>
            {
                _toastMessage = null;
                RunOnUi(() => OnPropertyChanged(nameof(SelectionStatusText)));
            }, null, autoDismissAfter.Value, Timeout.InfiniteTimeSpan);
        }
    }

    private void ShowToast(string message) => SetStatusText(message, TimeSpan.FromSeconds(4));

    public string SelectionStatusText
    {
        get
        {
            if (!string.IsNullOrEmpty(_toastMessage))
            {
                return _toastMessage;
            }

            int selectedCount = SelectedItems.Count;
            int totalCount;
            lock (_itemsLock)
            {
                totalCount = Items.Count;
            }
            if (selectedCount == 0)
            {
                return $"{totalCount} items";
            }

            long totalSelectedBytes = SelectedItems.Where(i => !i.IsDirectory).Sum(i => i.Size);
            string formattedBytes = FormatHelper.FormatFileSize(totalSelectedBytes, isDirectory: false);

            return $"{selectedCount} of {totalCount} selected ({formattedBytes})";
        }
    }

    public event Action? SelectionChanged;

    public void UpdateSelectedItems(IEnumerable<FileItemViewModel> selected)
    {
        RunOnUi(() =>
        {
            SelectedItems = selected.ToList();
            SelectedItem = SelectedItems.Count > 0 ? SelectedItems[0] : null;
            SelectionChanged?.Invoke();
        });
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

    public FilePaneViewModel(
        IDirectoryService directoryService,
        IIconService iconService,
        IFileSystem? fileSystem = null,
        IInputDialogService? inputDialogService = null,
        IUndoStack? undoStack = null,
        IShellLauncher? shellLauncher = null)
    {
        _directoryService = directoryService;
        _iconService = iconService;
        _fileSystem = fileSystem ?? new PhysicalFileSystem();
        _inputDialogService = inputDialogService ?? new InputDialogService();
        UndoStack = undoStack ?? new UndoStack();
        _shellLauncher = shellLauncher ?? new ShellLauncher();

        UndoStack.CanUndoChanged += () =>
        {
            RunOnUi(() => UndoCommand.NotifyCanExecuteChanged());
        };

        if (System.Windows.Application.Current != null)
        {
            System.Windows.Data.BindingOperations.EnableCollectionSynchronization(Items, _itemsLock);
        }
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
    public async Task OpenItemAsync(FileItemViewModel? item)
    {
        var target = item ?? SelectedItem;
        if (target == null) return;

        if (target.IsDirectory)
        {
            Navigate(target.FullPath);
        }
        else
        {
            if (!_fileSystem.FileExists(target.FullPath))
            {
                await RefreshAsync();
                ErrorMessage = "File no longer exists";
                return;
            }

            try
            {
                _shellLauncher.Launch(target.FullPath);
            }
            catch (Win32Exception ex)
            {
                switch (ex.NativeErrorCode)
                {
                    case 2: // ERROR_FILE_NOT_FOUND
                    case 3: // ERROR_PATH_NOT_FOUND
                        ErrorMessage = "File not found";
                        break;
                    case 5: // ERROR_ACCESS_DENIED
                        ErrorMessage = "Access denied";
                        break;
                    case 1155: // ERROR_NO_ASSOCIATION
                        ErrorMessage = "No application is associated with this file";
                        break;
                    case 1223: // ERROR_CANCELLED
                        // User cancelled the prompt (e.g. UAC elevation or Open With dialog); silently ignore
                        break;
                    default:
                        ErrorMessage = $"{ex.Message} (Error code: {ex.NativeErrorCode})";
                        break;
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Cannot open file: {ex.Message}";
            }
        }
    }

    public void OpenItem(FileItemViewModel? item) => _ = OpenItemAsync(item);

    [RelayCommand]
    private void DismissError()
    {
        ErrorMessage = null;
    }

    public bool CanCreateNewFolder => !string.IsNullOrEmpty(CurrentPath) && !IsLoading;

    [RelayCommand(CanExecute = nameof(CanCreateNewFolder))]
    public async Task NewFolderAsync()
    {
        if (string.IsNullOrEmpty(CurrentPath)) return;

        string defaultName = CreateFolderOperation.GetDefaultUniqueFolderName(_fileSystem, CurrentPath);
        string? inputName = _inputDialogService.Prompt("New Folder", "Enter folder name:", defaultName);

        if (string.IsNullOrWhiteSpace(inputName)) return;

        var op = new CreateFolderOperation(_fileSystem, CurrentPath, inputName.Trim());
        var result = await op.DoAsync();

        if (!result.Success)
        {
            ErrorMessage = result.ErrorMessage;
            return;
        }

        UndoStack.Push(op);
        ShowToast($"Created folder '{inputName.Trim()}'");

        await RefreshAsync();

        if (result.TargetPath != null)
        {
            var match = Items.FirstOrDefault(i => string.Equals(i.FullPath, result.TargetPath, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                UpdateSelectedItems(new[] { match });
            }
        }
    }

    public bool CanRename => SelectedItem != null && !IsLoading;

    [RelayCommand(CanExecute = nameof(CanRename))]
    public async Task RenameAsync()
    {
        if (SelectedItem == null) return;

        var target = SelectedItem;
        string currentName = target.Name;
        int? selLen = !target.IsDirectory ? Path.GetFileNameWithoutExtension(currentName).Length : null;

        string? newName = _inputDialogService.Prompt("Rename", "Enter new name:", currentName, selLen);

        if (string.IsNullOrWhiteSpace(newName) || string.Equals(newName, currentName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var op = new RenameOperation(_fileSystem, target.FullPath, newName.Trim(), target.IsDirectory);
        var result = await op.DoAsync();

        if (!result.Success)
        {
            ErrorMessage = result.ErrorMessage;
            return;
        }

        UndoStack.Push(op);
        ShowToast($"Renamed to '{newName.Trim()}'");

        await RefreshAsync();

        if (result.TargetPath != null)
        {
            var match = Items.FirstOrDefault(i => string.Equals(i.FullPath, result.TargetPath, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                UpdateSelectedItems(new[] { match });
            }
        }
    }

    public bool CanUndo => UndoStack.CanUndo && !IsLoading;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    public async Task UndoAsync()
    {
        if (!UndoStack.CanUndo) return;

        string? desc = UndoStack.NextUndoDescription;
        var result = await UndoStack.UndoAsync();

        if (result == null) return;

        if (!result.Success)
        {
            ErrorMessage = result.ErrorMessage;
            return;
        }

        ShowToast($"Undone: {desc}");

        await RefreshAsync();

        if (result.TargetPath != null)
        {
            var match = Items.FirstOrDefault(i => string.Equals(i.FullPath, result.TargetPath, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                UpdateSelectedItems(new[] { match });
            }
        }
    }

    private long _navigationVersion;

    public async Task NavigateInternalAsync(string targetPath, NavigationPhaseType phaseType)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.HasShutdownStarted && !dispatcher.CheckAccess())
        {
            await dispatcher.InvokeAsync(() => NavigateInternalAsync(targetPath, phaseType)).Task.Unwrap().ConfigureAwait(false);
            return;
        }

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

                RunOnUi(() =>
                {
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
                });
            }

            var previousSelectedPaths = phaseType == NavigationPhaseType.Refresh
                ? new HashSet<string>(SelectedItems.Select(i => i.FullPath), StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Clear view items immediately
            SafeReplaceItems(Array.Empty<FileItemViewModel>());
            UpdateSelectedItems(Array.Empty<FileItemViewModel>());
            OnPropertyChanged(nameof(SelectionStatusText));

            var statusUpdateSw = Stopwatch.StartNew();
            var batchSw = Stopwatch.StartNew();
            int currentBatchSize = 50;
            await foreach (var batch in _directoryService.EnumerateBatchesAsync(resolvedPath, currentBatchSize, ct))
            {
                if (!IsCurrent()) return;
                EnsureHistoryCommitted();
                var filteredBatch = ShowHiddenFiles
                    ? batch
                    : batch.Where(e => !e.IsHidden && !e.IsSystem);
                var batchVms = filteredBatch.Select(e => new FileItemViewModel(e, _iconService, ct)).ToList();
                if (batchVms.Count > 0)
                {
                    SafeAddItems(batchVms);
                }

                if (statusUpdateSw.ElapsedMilliseconds >= 100)
                {
                    RunOnUi(() => OnPropertyChanged(nameof(SelectionStatusText)));
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

            // Switch to UI thread for sorting, selection restoration and final status update
            await RunOnUiAsync(async () =>
            {
                if (!IsCurrent()) return;

                // Apply folders-first sort order once enumeration completes
                await ApplySortAsync(currentVersion, ct);

                if (!IsCurrent()) return;

                // Restore all selected items by FullPath on Refresh
                if (previousSelectedPaths.Count > 0)
                {
                    List<FileItemViewModel> restored;
                    lock (_itemsLock)
                    {
                        restored = Items.Where(i => previousSelectedPaths.Contains(i.FullPath)).ToList();
                    }
                    UpdateSelectedItems(restored);
                }

                OnPropertyChanged(nameof(SelectionStatusText));
            });
        }
        catch (OperationCanceledException)
        {
            // Expected when user quickly navigates to another folder
        }
        catch (UnauthorizedAccessException)
        {
            if (IsCurrent())
            {
                RunOnUi(() =>
                {
                    ErrorMessage = $"Access denied: '{targetPath}'";
                    SafeReplaceItems(Array.Empty<FileItemViewModel>());
                });
            }
        }
        catch (DirectoryNotFoundException)
        {
            if (IsCurrent())
            {
                RunOnUi(() =>
                {
                    ErrorMessage = $"Directory not found: '{targetPath}'";
                    SafeReplaceItems(Array.Empty<FileItemViewModel>());
                });
            }
        }
        catch (IOException ex)
        {
            if (IsCurrent())
            {
                RunOnUi(() =>
                {
                    ErrorMessage = $"I/O Error: {ex.Message}";
                    SafeReplaceItems(Array.Empty<FileItemViewModel>());
                });
            }
        }
        catch (Exception ex)
        {
            if (IsCurrent())
            {
                RunOnUi(() =>
                {
                    ErrorMessage = $"Error: {ex.Message}";
                    SafeReplaceItems(Array.Empty<FileItemViewModel>());
                });
            }
        }
        finally
        {
            if (IsCurrent())
            {
                RunOnUi(() =>
                {
                    IsLoading = false;
                    OnPropertyChanged(nameof(SelectionStatusText));
                });
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

        List<FileItemViewModel> currentItems;
        lock (_itemsLock)
        {
            if (Items.Count <= 1) return;
            currentItems = Items.ToList();
        }
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

        SafeReplaceItems(sortedVms);

        // Restore ALL selections by FullPath
        if (selectedPaths.Count > 0)
        {
            List<FileItemViewModel> restored;
            lock (_itemsLock)
            {
                restored = Items.Where(i => selectedPaths.Contains(i.FullPath)).ToList();
            }
            UpdateSelectedItems(restored);
        }
    }

    private void RunOnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.HasShutdownStarted && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(action);
        }
        else
        {
            action();
        }
    }

    private async Task RunOnUiAsync(Func<Task> action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.HasShutdownStarted && !dispatcher.CheckAccess())
        {
            await dispatcher.InvokeAsync(action).Task.Unwrap();
        }
        else
        {
            await action();
        }
    }

    private void SafeReplaceItems(IEnumerable<FileItemViewModel> items)
    {
        RunOnUi(() =>
        {
            lock (_itemsLock)
            {
                Items.ReplaceAll(items);
            }
        });
    }

    private void SafeAddItems(IEnumerable<FileItemViewModel> items)
    {
        RunOnUi(() =>
        {
            lock (_itemsLock)
            {
                Items.AddRange(items);
            }
        });
    }

    private void UpdateHistoryCommandStates()
    {
        RunOnUi(() =>
        {
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(CanGoForward));
            OnPropertyChanged(nameof(CanGoUp));

            GoBackCommand.NotifyCanExecuteChanged();
            GoForwardCommand.NotifyCanExecuteChanged();
            GoUpCommand.NotifyCanExecuteChanged();
        });
    }

    public static List<BreadcrumbItem> GenerateBreadcrumbs(string? path)
    {
        var result = new List<BreadcrumbItem>();
        if (string.IsNullOrWhiteSpace(path)) return result;

        try
        {
            string fullPath = Path.GetFullPath(path);
            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrEmpty(root))
            {
                result.Add(new BreadcrumbItem(fullPath, fullPath, true));
                return result;
            }

            string rootDisplay = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrEmpty(rootDisplay)) rootDisplay = root;
            result.Add(new BreadcrumbItem(rootDisplay, root, root.Equals(fullPath, StringComparison.OrdinalIgnoreCase)));

            string relative = fullPath.Substring(root.Length).Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.IsNullOrEmpty(relative))
            {
                var parts = relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
                string accumulated = root;
                for (int i = 0; i < parts.Length; i++)
                {
                    accumulated = Path.Combine(accumulated, parts[i]);
                    bool isLast = (i == parts.Length - 1);
                    result.Add(new BreadcrumbItem(parts[i], accumulated, isLast));
                }
            }
        }
        catch
        {
            result.Clear();
            result.Add(new BreadcrumbItem(path, path, true));
        }

        return result;
    }
}
