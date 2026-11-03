using FM.Core.ShellIntegration;
using FM.Core.Drives;
using FM.Core.RecycleBin;
using FM.Core.PathResolution;
using FM.Core.Operations.MultiRename;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
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
using FM.Core.Operations.Transfer;
using FM.Core.Search;
using FM.Core.Services;
using FM.Core.Watching;
using System.Text;

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
public partial class FilePaneViewModel : ObservableObject, IDisposable
{
    private readonly IDirectoryService _directoryService;
    private readonly IFolderWatcherFactory _folderWatcherFactory;
    private IFolderWatcher? _folderWatcher;
    public IFolderWatcher? ActiveWatcher => _folderWatcher;
    private readonly List<FolderChangeEventArgs> _pendingWatcherEvents = new();
    private readonly object _watcherLock = new();
    private ITimer? _watcherDebounceTimer;
    private int _activeTransferCount;
    private readonly IIconService _iconService;
    private readonly IFileSystem _fileSystem;
    private readonly IInputDialogService _inputDialogService;
    private readonly IShellLauncher _shellLauncher;
    private readonly IDriveService _driveService;
    private readonly IRecycleBinService _recycleBinService;
    private readonly IPathResolver _pathResolver;
    private readonly ISearchService _searchService;
    private readonly IShellNewService _shellNewService;
    private readonly ISendToService _sendToService;
    private readonly IMultiRenameDialogService _multiRenameDialogService;
    private readonly ISettingsService? _settingsService;
    private readonly ITransferQueue? _transferQueue;
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _searchDebounceCts;
    private List<FileItemViewModel>? _savedFolderItems;

    public Func<IReadOnlyList<string>>? GetOpenPanePaths { get; set; }
    public Func<IReadOnlyList<string>>? GetApprovedRoots { get; set; }
    public event Action? FindBarOpened;
    private readonly NavigationHistory _history = new();
    private CancellationTokenSource? _loadCts;

    [ObservableProperty]
    private bool _showHiddenFiles;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsThisPcMode))]
    [NotifyPropertyChangedFor(nameof(IsRecycleBinMode))]
    [NotifyPropertyChangedFor(nameof(IsStandardFolderMode))]
    private SpecialPaneMode _specialMode = SpecialPaneMode.None;

    public bool IsThisPcMode => SpecialMode == SpecialPaneMode.ThisPC;
    public bool IsRecycleBinMode => SpecialMode == SpecialPaneMode.RecycleBin;
    public bool IsStandardFolderMode => SpecialMode == SpecialPaneMode.None;

    public System.Collections.ObjectModel.ObservableCollection<DriveCardViewModel> Drives { get; } = new();
    public System.Collections.ObjectModel.ObservableCollection<RecycleBinItemViewModel> RecycleBinItems { get; } = new();

    public IReadOnlyList<ShellNewTemplate> NewTemplates => _shellNewService.GetTemplates();
    public IReadOnlyList<SendToTarget> SendToTargets => _sendToService.GetTargets();

    [ObservableProperty]
    private bool _isFindBarOpen;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private bool _isSearchMode;

    [ObservableProperty]
    private string _searchQueryText = string.Empty;

    [ObservableProperty]
    private SearchScope _selectedSearchScope = SearchScope.CurrentFolder;

    public static IReadOnlyList<SearchScope> AvailableSearchScopes { get; } =
    [
        SearchScope.CurrentFolder,
        SearchScope.OpenPanes,
        SearchScope.ThisPC
    ];

    [ObservableProperty]
    private string _searchResultCountText = string.Empty;

    [ObservableProperty]
    private string? _searchWarningText;

    [ObservableProperty]
    private string? _searchSkippedText;

    [ObservableProperty]
    private bool _isResultsLimited;

    public IUndoStack UndoStack { get; }
    public IFileSystem FileSystem => _fileSystem;
    public Action<string>? OnOpenInNewTabRequested { get; set; }

    private readonly object _itemsLock = new();
    public BulkObservableCollection<FileItemViewModel> Items { get; } = new();

    private readonly List<FileItemViewModel> _unfilteredItems = new();
    public IReadOnlyList<FileItemViewModel> UnfilteredItems
    {
        get
        {
            lock (_itemsLock)
            {
                return _unfilteredItems.ToList();
            }
        }
    }
    private long _filterVersion;
    private CancellationTokenSource? _filterCts;

    private readonly StringBuilder _typeToSelectBuffer = new();
    private DateTimeOffset _lastTypeToSelectTime = DateTimeOffset.MinValue;
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

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
    [NotifyPropertyChangedFor(nameof(SelectionStatusText))]
    [NotifyPropertyChangedFor(nameof(IsFiltering))]
    private string _filterText = string.Empty;

    public bool IsFiltering => !string.IsNullOrWhiteSpace(FilterText);

    public string SearchText
    {
        get => FilterText;
        set => FilterText = value;
    }

    private bool _suppressFilterDebounce;

    partial void OnFilterTextChanged(string value)
    {
        if (_suppressFilterDebounce) return;
        _ = ApplyFilterDebouncedAsync(value);
    }

    public async Task ApplyFilterDebouncedAsync(string filterText)
    {
        _filterCts?.Cancel();
        _filterCts?.Dispose();
        _filterCts = new CancellationTokenSource();
        var ct = _filterCts.Token;

        try
        {
            await Task.Delay(150, ct);
            await ApplyFilterInternalAsync(filterText, ct);
        }
        catch (OperationCanceledException)
        {
            // Superseded
        }
    }

    public async Task ApplyFilterImmediateAsync(string filterText, CancellationToken ct = default)
    {
        _filterCts?.Cancel();
        _suppressFilterDebounce = true;
        try
        {
            FilterText = filterText ?? string.Empty;
        }
        finally
        {
            _suppressFilterDebounce = false;
        }
        await ApplyFilterInternalAsync(FilterText, ct);
    }

    private async Task ApplyFilterInternalAsync(string filterText, CancellationToken ct)
    {
        long currentVersion = Interlocked.Increment(ref _filterVersion);
        bool IsCurrent() => currentVersion == Interlocked.Read(ref _filterVersion)
                         && !ct.IsCancellationRequested
                         && (_loadCts == null || !_loadCts.IsCancellationRequested);

        if (!IsCurrent()) return;

        List<FileItemViewModel> sourceItems;
        lock (_itemsLock)
        {
            sourceItems = _unfilteredItems.ToList();
        }

        var selectedPaths = new HashSet<string>(SelectedItems.Select(i => i.FullPath), StringComparer.OrdinalIgnoreCase);

        List<FileItemViewModel> filteredItems;
        if (string.IsNullOrWhiteSpace(filterText))
        {
            filteredItems = sourceItems;
        }
        else
        {
            if (sourceItems.Count > 500)
            {
                filteredItems = await Task.Run(() =>
                    sourceItems.Where(item => NameMatcher.Matches(item.Name, filterText)).ToList(),
                    ct);
            }
            else
            {
                filteredItems = sourceItems.Where(item => NameMatcher.Matches(item.Name, filterText)).ToList();
            }
        }

        if (!IsCurrent()) return;

        var sortCol = CurrentSortColumn;
        var isAsc = IsSortAscending;
        if (filteredItems.Count > 1)
        {
            var map = filteredItems.ToDictionary(i => i.FullPath, StringComparer.OrdinalIgnoreCase);
            var entries = filteredItems.Select(i => i.Entry).ToList();
            if (filteredItems.Count > 500)
            {
                var sortedEntries = await Task.Run(() => entries.OrderEntries(sortCol, isAsc), ct);
                filteredItems = sortedEntries.Select(e => map[e.FullPath]).ToList();
            }
            else
            {
                var sortedEntries = entries.OrderEntries(sortCol, isAsc);
                filteredItems = sortedEntries.Select(e => map[e.FullPath]).ToList();
            }
        }

        if (!IsCurrent()) return;

        RunOnUi(() =>
        {
            if (!IsCurrent()) return;

            SafeReplaceItems(filteredItems);

            if (selectedPaths.Count > 0)
            {
                var restored = filteredItems.Where(i => selectedPaths.Contains(i.FullPath)).ToList();
                UpdateSelectedItems(restored);
            }
            else
            {
                UpdateSelectedItems(Array.Empty<FileItemViewModel>());
            }

            OnPropertyChanged(nameof(SelectionStatusText));
        });
    }

    public void ResetTypeToSelect()
    {
        _typeToSelectBuffer.Clear();
        _lastTypeToSelectTime = DateTimeOffset.MinValue;
    }

    public FileItemViewModel? TypeToSelect(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;

        var now = TimeProvider.GetUtcNow();
        if (_lastTypeToSelectTime != DateTimeOffset.MinValue)
        {
            double elapsed = (now - _lastTypeToSelectTime).TotalSeconds;
            if (elapsed > 1.0)
            {
                _typeToSelectBuffer.Clear();
            }
        }
        _lastTypeToSelectTime = now;

        _typeToSelectBuffer.Append(text);
        string query = _typeToSelectBuffer.ToString();

        List<FileItemViewModel> currentItems;
        lock (_itemsLock)
        {
            currentItems = Items.ToList();
        }

        if (currentItems.Count == 0) return null;

        string normQuery = NameMatcher.Normalize(query);
        if (string.IsNullOrEmpty(normQuery)) return null;

        var match = currentItems.FirstOrDefault(item =>
            NameMatcher.Normalize(item.Name).StartsWith(normQuery, StringComparison.Ordinal));

        if (match == null)
        {
            match = currentItems.FirstOrDefault(item =>
                NameMatcher.Normalize(item.Name).Contains(normQuery, StringComparison.Ordinal));
        }

        if (match != null)
        {
            UpdateSelectedItems(new[] { match });
        }

        return match;
    }

    public FileItemViewModel? TypeToSelect(char c) => TypeToSelect(c.ToString());

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

            int visibleCount;
            int totalCount;
            lock (_itemsLock)
            {
                visibleCount = Items.Count;
                totalCount = _unfilteredItems.Count > 0 ? _unfilteredItems.Count : visibleCount;
            }

            if (IsFiltering)
            {
                int selectedCount = SelectedItems.Count;
                if (selectedCount == 0)
                {
                    return $"{visibleCount} of {totalCount} items";
                }

                long totalSelectedBytes = SelectedItems.Where(i => !i.IsDirectory).Sum(i => i.Size);
                string formattedBytes = FormatHelper.FormatFileSize(totalSelectedBytes, isDirectory: false);
                return $"{selectedCount} of {visibleCount} selected ({visibleCount} of {totalCount} items)";
            }

            int selCount = SelectedItems.Count;
            if (selCount == 0)
            {
                return $"{visibleCount} items";
            }

            long selBytes = SelectedItems.Where(i => !i.IsDirectory).Sum(i => i.Size);
            string formatted = FormatHelper.FormatFileSize(selBytes, isDirectory: false);

            return $"{selCount} of {visibleCount} selected ({formatted})";
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
    public event Action<FilePaneViewModel, string>? FolderOpened;

    public FilePaneViewModel(
        IDirectoryService directoryService,
        IIconService iconService,
        IFileSystem? fileSystem = null,
        IInputDialogService? inputDialogService = null,
        IUndoStack? undoStack = null,
        IShellLauncher? shellLauncher = null,
        IFolderWatcherFactory? folderWatcherFactory = null,
        TimeProvider? timeProvider = null,
        IDriveService? driveService = null,
        IRecycleBinService? recycleBinService = null,
        IPathResolver? pathResolver = null,
        ISearchService? searchService = null,
        IShellNewService? shellNewService = null,
        ISendToService? sendToService = null,
        IMultiRenameDialogService? multiRenameDialogService = null,
        ISettingsService? settingsService = null,
        ITransferQueue? transferQueue = null)
    {
        _directoryService = directoryService;
        _iconService = iconService;
        _fileSystem = fileSystem ?? new PhysicalFileSystem();
        _inputDialogService = inputDialogService ?? new InputDialogService();
        UndoStack = undoStack ?? new UndoStack();
        _shellLauncher = shellLauncher ?? new ShellLauncher();
        TimeProvider = timeProvider ?? TimeProvider.System;
        _folderWatcherFactory = folderWatcherFactory ?? (_fileSystem is InMemoryFileSystem ? NullFolderWatcherFactory.Instance : new PhysicalFolderWatcherFactory(_fileSystem, TimeProvider));
        _driveService = driveService ?? (_fileSystem is InMemoryFileSystem ? new FakeDriveService() : PhysicalDriveService.Instance);
        _recycleBinService = recycleBinService ?? (_fileSystem is InMemoryFileSystem ? new FakeRecycleBinService() : WindowsRecycleBinService.Instance);
        _pathResolver = pathResolver ?? PathResolver.Instance;
        _searchService = searchService ?? new SearchService(_fileSystem);
        _shellNewService = shellNewService ?? new WindowsShellNewService();
        _sendToService = sendToService ?? new WindowsSendToService();
        _multiRenameDialogService = multiRenameDialogService ?? new WindowsMultiRenameDialogService();
        _settingsService = settingsService;
        _transferQueue = transferQueue;
        _driveService.DrivesChanged += OnDriveServiceDrivesChanged;

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
        if (IsSearchMode)
        {
            CloseFindBar();
            return;
        }
        string? target = _history.PeekBack();
        if (target != null)
        {
            await NavigateInternalAsync(target, NavigationPhaseType.Back);
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    public async Task GoForwardAsync()
    {
        if (IsSearchMode)
        {
            CloseFindBar();
            return;
        }
        string? target = _history.PeekForward();
        if (target != null)
        {
            await NavigateInternalAsync(target, NavigationPhaseType.Forward);
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoUp))]
    public async Task GoUpAsync()
    {
        if (IsSearchMode)
        {
            CloseFindBar();
            return;
        }
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

    public async Task NavigateAndSelectAsync(string targetPath, string? selectTargetName = null)
    {
        await NavigateInternalAsync(targetPath, NavigationPhaseType.New, selectTargetName);
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
        if (IsSearchMode)
        {
            SetStatusText("Không thể tạo thư mục trong kết quả tìm kiếm.", TimeSpan.FromSeconds(3));
            return;
        }
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

        // If multiple items are selected, delegate to MultiRename
        if (SelectedItems.Count > 1)
        {
            await MultiRenameAsync();
            return;
        }

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

    [RelayCommand]
    public async Task MultiRenameAsync()
    {
        var targets = SelectedItems.ToList();
        if (targets.Count == 0 && SelectedItem != null)
        {
            targets.Add(SelectedItem);
        }
        if (targets.Count == 0) return;

        var inputs = targets.Select(t => new MultiRenameItemInput(
            FullPath: t.FullPath,
            Name: t.Name,
            IsDirectory: t.IsDirectory,
            DateModified: t.Modified)).ToList();

        bool dialogSuccess = await _multiRenameDialogService.ShowDialogAsync(
            inputs,
            _fileSystem,
            async (plan) =>
            {
                var op = new MultiRenameOperation(_fileSystem, plan);
                var result = await op.DoAsync();
                if (result.Success)
                {
                    UndoStack.Push(op);
                    ShowToast($"Batch renamed {op.ChangedItems.Count} item(s)");
                    await RefreshAsync();
                }
                return result;
            });
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

    #region Context Menu & Clipboard Commands

    [RelayCommand]
    public void Copy()
    {
        RunOnUi(() =>
        {
            var selected = SelectedItems.Select(x => x.FullPath).ToList();
            if (selected.Count == 0 && SelectedItem != null)
            {
                selected.Add(SelectedItem.FullPath);
            }
            if (selected.Count == 0) return;

            try
            {
                var data = new DataObject();
                var sc = new System.Collections.Specialized.StringCollection();
                sc.AddRange(selected.ToArray());
                data.SetFileDropList(sc);
                Clipboard.SetDataObject(data, true);
                ShowToast($"Copied {selected.Count} item(s)");
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Clipboard error: {ex.Message}";
            }
        });
    }

    [RelayCommand]
    public void Cut()
    {
        RunOnUi(() =>
        {
            var selected = SelectedItems.Select(x => x.FullPath).ToList();
            if (selected.Count == 0 && SelectedItem != null)
            {
                selected.Add(SelectedItem.FullPath);
            }
            if (selected.Count == 0) return;

            try
            {
                var data = new DataObject();
                var sc = new System.Collections.Specialized.StringCollection();
                sc.AddRange(selected.ToArray());
                data.SetFileDropList(sc);
                byte[] moveEffect = new byte[] { 2, 0, 0, 0 }; // DROPEFFECT_MOVE = 2
                var ms = new MemoryStream(moveEffect);
                data.SetData("Preferred DropEffect", ms);
                Clipboard.SetDataObject(data, true);
                ShowToast($"Cut {selected.Count} item(s)");
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Clipboard error: {ex.Message}";
            }
        });
    }

    [RelayCommand]
    public async Task PasteAsync()
    {
        if (IsSearchMode)
        {
            SetStatusText("Không thể dán tệp vào kết quả tìm kiếm.", TimeSpan.FromSeconds(3));
            return;
        }
        List<string>? sourcePaths = null;
        bool isCut = false;

        RunOnUi(() =>
        {
            try
            {
                if (Clipboard.ContainsFileDropList())
                {
                    var fileDropList = Clipboard.GetFileDropList();
                    if (fileDropList != null && fileDropList.Count > 0)
                    {
                        sourcePaths = fileDropList.Cast<string>().ToList();

                        var data = Clipboard.GetDataObject();
                        if (data != null && data.GetDataPresent("Preferred DropEffect"))
                        {
                            if (data.GetData("Preferred DropEffect") is MemoryStream ms)
                            {
                                int dropEffect = ms.ReadByte();
                                isCut = (dropEffect == 2);
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore clipboard access errors
            }
        });

        if (sourcePaths == null || sourcePaths.Count == 0 || string.IsNullOrEmpty(CurrentPath)) return;

        var mainVm = System.Windows.Application.Current?.MainWindow?.DataContext as MainViewModel;
        if (mainVm != null)
        {
            var job = mainVm.EnqueueTransfer(
                isCut ? TransferKind.Move : TransferKind.Copy,
                sourcePaths,
                CurrentPath,
                this);

            if (job != null)
            {
                await mainVm.WaitForJobCompletionAsync(job);
                if (isCut)
                {
                    RunOnUi(() => Clipboard.Clear());
                }
            }
        }
    }

    public bool SupportsRecycleBin(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (path.StartsWith(@"\\")) return false; // UNC paths have no Recycle Bin
        if (_fileSystem is InMemoryFileSystem) return true; // In-memory tests simulate Recycle Bin unless UNC

        try
        {
            string? root = Path.GetPathRoot(path);
            if (!string.IsNullOrEmpty(root))
            {
                var d = new DriveInfo(root);
                if (d.DriveType == DriveType.Network || d.DriveType == DriveType.NoRootDirectory)
                {
                    return false;
                }
            }
        }
        catch
        {
            return !path.StartsWith(@"\\");
        }

        return true;
    }

    [RelayCommand]
    public async Task DeleteToRecycleBinAsync()
    {
        var selected = SelectedItems.Select(x => x.FullPath).ToList();
        if (selected.Count == 0 && SelectedItem != null)
        {
            selected.Add(SelectedItem.FullPath);
        }
        if (selected.Count == 0) return;

        // Check for locations without Recycle Bin (network drives, UNC)
        bool anyNoRecycle = selected.Any(p => !SupportsRecycleBin(p));
        if (anyNoRecycle)
        {
            string msg = selected.Count == 1
                ? $"'{Path.GetFileName(selected[0])}' is located on a network drive or volume without a Recycle Bin.\n\nAre you sure you want to permanently delete it?"
                : $"These {selected.Count} items are located on a network drive or volume without a Recycle Bin.\n\nAre you sure you want to permanently delete them?";

            if (!_inputDialogService.Confirm("Delete Permanently", msg))
            {
                return;
            }

            await DeletePermanentlyCoreAsync(selected);
            return;
        }

        // Setting: Confirm normal delete
        bool confirmNormal = _settingsService?.LoadSettings().ConfirmNormalDelete ?? false;
        if (confirmNormal)
        {
            string msg = selected.Count == 1
                ? $"Are you sure you want to move '{Path.GetFileName(selected[0])}' to the Recycle Bin?"
                : $"Are you sure you want to move these {selected.Count} items to the Recycle Bin?";

            if (!_inputDialogService.Confirm("Delete File", msg))
            {
                return;
            }
        }

        bool ok = MoveToRecycleBinCore(selected);
        if (ok)
        {
            var op = new RecycleBinDeleteOperation(_recycleBinService, paths => MoveToRecycleBinCore(paths.ToList()), selected);
            UndoStack.Push(op);

            ShowToast($"Deleted {selected.Count} item(s) to Recycle Bin");
            await RefreshAsync();
        }
        else
        {
            ErrorMessage = "Could not move item(s) to Recycle Bin";
        }
    }

    private bool MoveToRecycleBinCore(IReadOnlyList<string> paths)
    {
        if (_fileSystem is InMemoryFileSystem)
        {
            if (_recycleBinService is FakeRecycleBinService fakeBin)
            {
                foreach (var path in paths)
                {
                    string name = Path.GetFileName(path);
                    string dir = Path.GetDirectoryName(path) ?? "";
                    fakeBin.AddItem(new RecycleBinItem(
                        id: Guid.NewGuid().ToString(),
                        name: name,
                        originalLocation: dir,
                        dateDeleted: DateTime.UtcNow,
                        size: 500,
                        itemType: "File",
                        isDirectory: false));
                }
            }
            foreach (var path in paths)
            {
                if (_fileSystem.FileExists(path)) _fileSystem.DeleteFile(path);
                else if (_fileSystem.DirectoryExists(path)) _fileSystem.DeleteDirectory(path, recursive: true);
            }
            return true;
        }

        return ShellInterop.MoveToRecycleBin(paths);
    }

    [RelayCommand]
    public async Task DeletePermanentlyAsync()
    {
        var selected = SelectedItems.Select(x => x.FullPath).ToList();
        if (selected.Count == 0 && SelectedItem != null)
        {
            selected.Add(SelectedItem.FullPath);
        }
        if (selected.Count == 0) return;

        string msg = selected.Count == 1
            ? $"Are you sure you want to permanently delete '{Path.GetFileName(selected[0])}'?\n\nThis action cannot be undone."
            : $"Are you sure you want to permanently delete these {selected.Count} items?\n\nThis action cannot be undone.";

        if (!_inputDialogService.Confirm("Delete Permanently", msg))
        {
            return;
        }

        await DeletePermanentlyCoreAsync(selected);
    }

    private async Task DeletePermanentlyCoreAsync(IReadOnlyList<string> paths)
    {
        int deleted = 0;
        foreach (var path in paths)
        {
            try
            {
                if (_fileSystem.DirectoryExists(path))
                {
                    _fileSystem.DeleteDirectory(path, recursive: true);
                    deleted++;
                }
                else if (_fileSystem.FileExists(path))
                {
                    _fileSystem.DeleteFile(path);
                    deleted++;
                }
                else if (File.Exists(path))
                {
                    File.Delete(path);
                    deleted++;
                }
                else if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                    deleted++;
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Could not delete '{Path.GetFileName(path)}': {ex.Message}";
            }
        }

        ShowToast($"Permanently deleted {deleted} item(s) (cannot be undone)");
        await RefreshAsync();
    }

    [RelayCommand]
    public async Task NewTextDocumentAsync()
    {
        if (string.IsNullOrEmpty(CurrentPath)) return;
        var template = new ShellNewTemplate(".txt", "Text Document", ShellNewKind.NullFile);
        string createdPath = await _shellNewService.CreateFileFromTemplateAsync(template, CurrentPath, _fileSystem);
        ShowToast($"Created '{Path.GetFileName(createdPath)}'");
        await RefreshAsync();
        SelectAndFocusItem(createdPath);
    }

    [RelayCommand]
    public async Task NewShortcutAsync()
    {
        if (string.IsNullOrEmpty(CurrentPath)) return;
        string? target = _inputDialogService.Prompt("Create Shortcut", "Enter target file or folder path:", CurrentPath);
        if (string.IsNullOrWhiteSpace(target)) return;

        string name = Path.GetFileName(target.TrimEnd('\\'));
        if (string.IsNullOrEmpty(name)) name = "Shortcut";
        string shortcutPath = Path.Combine(CurrentPath, $"{name}.lnk");
        int counter = 2;
        while (_fileSystem.FileExists(shortcutPath) || File.Exists(shortcutPath))
        {
            shortcutPath = Path.Combine(CurrentPath, $"{name} ({counter++}).lnk");
        }

        CreateShortcut(target, shortcutPath);
        ShowToast($"Created shortcut '{Path.GetFileName(shortcutPath)}'");
        await RefreshAsync();
        SelectAndFocusItem(shortcutPath);
    }

    [RelayCommand]
    public void CreateShortcutForSelected()
    {
        var item = SelectedItem;
        if (item == null || string.IsNullOrEmpty(CurrentPath)) return;

        string shortcutPath = Path.Combine(CurrentPath, $"{Path.GetFileNameWithoutExtension(item.Name)} - Shortcut.lnk");
        int counter = 2;
        while (_fileSystem.FileExists(shortcutPath) || File.Exists(shortcutPath))
        {
            shortcutPath = Path.Combine(CurrentPath, $"{Path.GetFileNameWithoutExtension(item.Name)} - Shortcut ({counter++}).lnk");
        }

        CreateShortcut(item.FullPath, shortcutPath);
        ShowToast($"Created shortcut '{Path.GetFileName(shortcutPath)}'");
        _ = RefreshAsync();
    }

    private void CreateShortcut(string target, string shortcutPath)
    {
        if (_fileSystem is InMemoryFileSystem)
        {
            using (_fileSystem.CreateFileForWrite(shortcutPath)) { }
        }
        else
        {
            WindowsSendToService.CreateShortcutFile(target, shortcutPath);
        }
    }

    [RelayCommand]
    public async Task CreateFromTemplateAsync(ShellNewTemplate template)
    {
        if (template == null || string.IsNullOrEmpty(CurrentPath)) return;
        string createdPath = await _shellNewService.CreateFileFromTemplateAsync(template, CurrentPath, _fileSystem);
        ShowToast($"Created '{Path.GetFileName(createdPath)}'");
        await RefreshAsync();
        SelectAndFocusItem(createdPath);
    }

    [RelayCommand]
    public async Task SendToAsync(SendToTarget target)
    {
        if (target == null) return;
        var selected = SelectedItems.Select(x => x.FullPath).ToList();
        if (selected.Count == 0 && SelectedItem != null) selected.Add(SelectedItem.FullPath);
        if (selected.Count == 0) return;

        bool ok = await _sendToService.ExecuteSendToAsync(
            target,
            selected,
            copyToFolderAction: async (sources, dest) =>
            {
                if (_transferQueue != null)
                {
                    var req = new TransferRequest(TransferKind.Copy, sources, dest, new TransferOptions(ConflictAction.KeepBoth));
                    var job = _transferQueue.Enqueue(req);
                    await job.Completion;
                }
                else
                {
                    foreach (var s in sources)
                    {
                        string targetFile = Path.Combine(dest, Path.GetFileName(s));
                        if (_fileSystem.FileExists(s))
                        {
                            using var inStream = _fileSystem.OpenRead(s);
                            using var outStream = _fileSystem.CreateFileForWrite(targetFile);
                            inStream.CopyTo(outStream);
                        }
                        else if (File.Exists(s))
                        {
                            File.Copy(s, targetFile, true);
                        }
                    }
                }
                ShowToast($"Sent {sources.Count} item(s) to '{target.DisplayName}'");
            },
            zipAction: async (sources) =>
            {
                await CompressToZipAsync();
            });

        if (ok)
        {
            ShowToast($"Sent to '{target.DisplayName}'");
        }
    }

    [RelayCommand]
    public void SelectAll()
    {
        UpdateSelectedItems(Items.ToList());
    }

    [RelayCommand]
    public void InvertSelection()
    {
        var currentSet = new HashSet<FileItemViewModel>(SelectedItems);
        var inverted = Items.Where(i => !currentSet.Contains(i)).ToList();
        UpdateSelectedItems(inverted);
    }

    [RelayCommand]
    public void SelectByPattern()
    {
        string? pattern = _inputDialogService.Prompt("Select by Pattern", "Enter pattern (e.g. *.txt, report*.*):", "*.*");
        if (string.IsNullOrWhiteSpace(pattern)) return;

        var matching = Items.Where(i => System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, i.Name, ignoreCase: true)).ToList();
        UpdateSelectedItems(matching);
        ShowToast($"Selected {matching.Count} item(s) matching '{pattern}'");
    }

    [RelayCommand]
    public void CopyPath()
    {
        var paths = SelectedItems.Select(x => x.FullPath).ToList();
        if (paths.Count == 0 && SelectedItem != null) paths.Add(SelectedItem.FullPath);
        if (paths.Count == 0 && !string.IsNullOrEmpty(CurrentPath)) paths.Add(CurrentPath);
        if (paths.Count == 0) return;

        RunOnUi(() =>
        {
            try
            {
                Clipboard.SetText(string.Join(Environment.NewLine, paths));
                ShowToast("Copied full path(s) to clipboard");
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Clipboard error: {ex.Message}";
            }
        });
    }

    [RelayCommand]
    public void CopyDirectoryPath()
    {
        var dirs = SelectedItems.Select(x => Path.GetDirectoryName(x.FullPath) ?? x.FullPath).ToList();
        if (dirs.Count == 0 && SelectedItem != null) dirs.Add(Path.GetDirectoryName(SelectedItem.FullPath) ?? SelectedItem.FullPath);
        if (dirs.Count == 0 && !string.IsNullOrEmpty(CurrentPath)) dirs.Add(CurrentPath);
        if (dirs.Count == 0) return;

        RunOnUi(() =>
        {
            try
            {
                Clipboard.SetText(string.Join(Environment.NewLine, dirs.Distinct(StringComparer.OrdinalIgnoreCase)));
                ShowToast("Copied containing folder path to clipboard");
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Clipboard error: {ex.Message}";
            }
        });
    }

    [RelayCommand]
    public void CopyFileName()
    {
        var names = SelectedItems.Select(x => x.Name).ToList();
        if (names.Count == 0 && SelectedItem != null) names.Add(SelectedItem.Name);
        if (names.Count == 0) return;

        RunOnUi(() =>
        {
            try
            {
                Clipboard.SetText(string.Join(Environment.NewLine, names));
                ShowToast("Copied file name(s) to clipboard");
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Clipboard error: {ex.Message}";
            }
        });
    }

    [RelayCommand]
    public void CopyRelativePath()
    {
        if (string.IsNullOrEmpty(CurrentPath)) return;
        var rels = SelectedItems.Select(x => Path.GetRelativePath(CurrentPath, x.FullPath)).ToList();
        if (rels.Count == 0 && SelectedItem != null) rels.Add(Path.GetRelativePath(CurrentPath, SelectedItem.FullPath));
        if (rels.Count == 0) return;

        RunOnUi(() =>
        {
            try
            {
                Clipboard.SetText(string.Join(Environment.NewLine, rels));
                ShowToast("Copied relative path(s) to clipboard");
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Clipboard error: {ex.Message}";
            }
        });
    }

    private void SelectAndFocusItem(string path)
    {
        var match = Items.FirstOrDefault(i => string.Equals(i.FullPath, path, StringComparison.OrdinalIgnoreCase));
        if (match != null)
        {
            UpdateSelectedItems(new[] { match });
        }
    }

    [RelayCommand]
    public async Task CompressToZipAsync()
    {
        var selected = SelectedItems.Select(x => x.FullPath).ToList();
        if (selected.Count == 0 && SelectedItem != null)
        {
            selected.Add(SelectedItem.FullPath);
        }
        if (selected.Count == 0 || string.IsNullOrEmpty(CurrentPath)) return;

        string zipBaseName = selected.Count == 1 ? Path.GetFileNameWithoutExtension(selected[0]) : Path.GetFileName(CurrentPath);
        if (string.IsNullOrEmpty(zipBaseName)) zipBaseName = "Archive";
        string zipPath = Path.Combine(CurrentPath, $"{zipBaseName}.zip");

        int counter = 1;
        while (File.Exists(zipPath))
        {
            zipPath = Path.Combine(CurrentPath, $"{zipBaseName} ({counter++}).zip");
        }

        try
        {
            await Task.Run(() => ShellInterop.CreateZipArchive(selected, zipPath));
            ShowToast($"Created '{Path.GetFileName(zipPath)}'");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Compression error: {ex.Message}";
        }
    }

    [RelayCommand]
    public void OpenProperties()
    {
        string? path = SelectedItem?.FullPath ?? CurrentPath;
        if (!string.IsNullOrEmpty(path))
        {
            ShellInterop.ShowProperties(path);
        }
    }

    [RelayCommand]
    public void OpenInTerminal()
    {
        string? path = (SelectedItem?.IsDirectory == true ? SelectedItem.FullPath : null) ?? CurrentPath;
        if (!string.IsNullOrEmpty(path))
        {
            ShellInterop.OpenInTerminal(path);
        }
    }

    [RelayCommand]
    public void OpenWithCode()
    {
        string? path = SelectedItem?.FullPath ?? CurrentPath;
        if (!string.IsNullOrEmpty(path))
        {
            ShellInterop.OpenWithCode(path);
        }
    }

    [RelayCommand]
    public void OpenSegmentInNewTab(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        if (OnOpenInNewTabRequested != null)
        {
            OnOpenInNewTabRequested.Invoke(path);
            return;
        }

        var mainVm = System.Windows.Application.Current?.MainWindow?.DataContext as MainViewModel;
        if (mainVm != null)
        {
            mainVm.OpenNewTab(path);
        }
    }

    [RelayCommand]
    public void CopyPathname(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            Clipboard.SetText(path);
            ShowToast("Copied path to clipboard");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Clipboard error: {ex.Message}";
        }
    }

    [RelayCommand]
    public void OpenInNewTab()
    {
        string? targetPath = (SelectedItem?.IsDirectory == true ? SelectedItem.FullPath : null) ?? CurrentPath;
        if (string.IsNullOrEmpty(targetPath)) return;

        var mainVm = System.Windows.Application.Current?.MainWindow?.DataContext as MainViewModel;
        if (mainVm != null)
        {
            mainVm.SplitVertical(this);
            var newPane = mainVm.ActivePane;
            if (newPane != null && newPane != this)
            {
                newPane.Navigate(targetPath);
            }
        }
    }

    [RelayCommand]
    public void OpenInNewWindow()
    {
        string? targetPath = (SelectedItem?.IsDirectory == true ? SelectedItem.FullPath : null) ?? CurrentPath;
        if (string.IsNullOrEmpty(targetPath)) return;

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{targetPath}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Cannot open explorer: {ex.Message}";
        }
    }


    public bool IsWinRarInstalled => ShellInterop.IsWinRarInstalled;

    public bool IsSelectedArchive
    {
        get
        {
            if (SelectedItem == null || SelectedItem.IsDirectory) return false;
            string ext = Path.GetExtension(SelectedItem.FullPath).ToLowerInvariant();
            return ext is ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".iso";
        }
    }

    [RelayCommand]
    public void PinToStart()
    {
        string? path = SelectedItem?.FullPath ?? CurrentPath;
        if (!string.IsNullOrEmpty(path))
        {
            ShellInterop.PinToStart(path);
            ShowToast("Pinned to Start");
        }
    }

    [RelayCommand]
    public void OpenWith()
    {
        string? path = SelectedItem?.FullPath;
        if (!string.IsNullOrEmpty(path))
        {
            ShellInterop.ShowOpenWithDialog(path);
        }
    }

    [RelayCommand]
    public void RunAsAdmin()
    {
        string? path = SelectedItem?.FullPath;
        if (!string.IsNullOrEmpty(path))
        {
            ShellInterop.RunAsAdministrator(path);
        }
    }

    [RelayCommand]
    public void WinRarAdd()
    {
        var paths = SelectedItems.Select(x => x.FullPath).ToList();
        if (paths.Count == 0 && SelectedItem != null) paths.Add(SelectedItem.FullPath);
        if (paths.Count > 0)
        {
            ShellInterop.WinRarAddGui(paths);
        }
    }

    [RelayCommand]
    public void WinRarAddToRar()
    {
        var paths = SelectedItems.Select(x => x.FullPath).ToList();
        if (paths.Count == 0 && SelectedItem != null) paths.Add(SelectedItem.FullPath);
        if (paths.Count > 0)
        {
            string first = paths[0];
            string dir = Path.GetDirectoryName(first) ?? CurrentPath;
            string name = Path.GetFileNameWithoutExtension(first);
            string dest = Path.Combine(dir, $"{name}.rar");
            ShellInterop.WinRarAddToRar(paths, dest);
        }
    }

    [RelayCommand]
    public void WinRarExtractHere()
    {
        if (SelectedItem != null)
        {
            ShellInterop.WinRarExtractHere(SelectedItem.FullPath);
        }
    }

    [RelayCommand]
    public void WinRarExtractToFolder()
    {
        if (SelectedItem != null)
        {
            ShellInterop.WinRarExtractToFolder(SelectedItem.FullPath);
        }
    }

    [RelayCommand]
    public void PinToQuickAccess()
    {
        string? targetPath = (SelectedItem?.IsDirectory == true ? SelectedItem.FullPath : null) ?? CurrentPath;
        if (string.IsNullOrEmpty(targetPath)) return;

        var mainVm = System.Windows.Application.Current?.MainWindow?.DataContext as MainViewModel;
        if (mainVm != null)
        {
            string name = Path.GetFileName(targetPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(name)) name = targetPath;
            mainVm.AddFavorite(name, targetPath);
            ShowToast($"Pinned '{name}' to Quick access");
        }
    }

    #endregion

    private long _navigationVersion;

    public async Task NavigateInternalAsync(string targetPath, NavigationPhaseType phaseType, string? selectTargetName = null)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.HasShutdownStarted && !dispatcher.CheckAccess())
        {
            await dispatcher.InvokeAsync(() => NavigateInternalAsync(targetPath, phaseType, selectTargetName)).Task.Unwrap().ConfigureAwait(false);
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
        OnPropertyChanged(nameof(HasError));
        IsLoading = true;
        StopWatcher();

        var resolution = _pathResolver.Resolve(targetPath);
        if (!resolution.IsSuccess)
        {
            RunOnUi(() =>
            {
                ErrorMessage = resolution.ErrorMessage ?? "Đường dẫn không hợp lệ.";
                IsLoading = false;
                OnPropertyChanged(nameof(HasError));
            });
            return;
        }

        if (resolution.PathType == ResolvedPathType.ThisPC)
        {
            await LoadThisPcViewAsync(phaseType, ct, IsCurrent).ConfigureAwait(false);
            return;
        }

        if (resolution.PathType == ResolvedPathType.RecycleBin)
        {
            await LoadRecycleBinViewAsync(phaseType, ct, IsCurrent).ConfigureAwait(false);
            return;
        }

        RunOnUi(() => SpecialMode = SpecialPaneMode.None);
        string resolvedPath = resolution.ResolvedPath;

        if (resolvedPath.StartsWith(@"\\"))
        {
            bool reachable = false;
            try
            {
                reachable = await Task.Run(() =>
                {
                    try
                    {
                        return _fileSystem.DirectoryExists(resolvedPath) || Directory.Exists(resolvedPath);
                    }
                    catch
                    {
                        return false;
                    }
                }, ct).WaitAsync(TimeSpan.FromMilliseconds(1500), ct).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                reachable = false;
            }

            if (!IsCurrent()) return;

            if (!reachable)
            {
                RunOnUi(() =>
                {
                    ErrorMessage = $"Không thể truy cập đường dẫn mạng '{resolvedPath}'. Kiểm tra kết nối mạng hoặc quyền truy cập máy chủ.";
                    IsLoading = false;
                    OnPropertyChanged(nameof(HasError));
                });
                return;
            }
        }

        try
        {
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

                    if (phaseType != NavigationPhaseType.Refresh)
                    {
                        FolderOpened?.Invoke(this, resolvedPath);
                    }
                });
            }

            var previousSelectedPaths = phaseType == NavigationPhaseType.Refresh
                ? new HashSet<string>(SelectedItems.Select(i => i.FullPath), StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (phaseType == NavigationPhaseType.New)
            {
                FilterText = string.Empty;
            }

            lock (_itemsLock)
            {
                _unfilteredItems.Clear();
            }
            ResetTypeToSelect();

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
                    lock (_itemsLock)
                    {
                        _unfilteredItems.AddRange(batchVms);
                    }

                    var visibleBatch = !string.IsNullOrWhiteSpace(FilterText)
                        ? batchVms.Where(item => NameMatcher.Matches(item.Name, FilterText)).ToList()
                        : batchVms;

                    if (visibleBatch.Count > 0)
                    {
                        SafeAddItems(visibleBatch);
                    }
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

                if (!string.IsNullOrEmpty(selectTargetName))
                {
                    FileItemViewModel? matching;
                    lock (_itemsLock)
                    {
                        matching = Items.FirstOrDefault(i => string.Equals(i.Name, selectTargetName, StringComparison.OrdinalIgnoreCase) ||
                                                             string.Equals(i.FullPath, selectTargetName, StringComparison.OrdinalIgnoreCase));
                    }
                    if (matching != null)
                    {
                        UpdateSelectedItems(new[] { matching });
                        SelectedItem = matching;
                    }
                }
                else if (previousSelectedPaths.Count > 0)
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

            if (!IsCurrent()) return;
            StartWatcher(resolvedPath);
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

    internal void SafeReplaceItems(IEnumerable<FileItemViewModel> items)
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

        if (string.Equals(path, "thispc://", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(path, "This PC", StringComparison.OrdinalIgnoreCase))
        {
            result.Add(new BreadcrumbItem("This PC", "thispc://", true));
            return result;
        }

        if (string.Equals(path, "recyclebin://", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(path, "Recycle Bin", StringComparison.OrdinalIgnoreCase))
        {
            result.Add(new BreadcrumbItem("Recycle Bin", "recyclebin://", true));
            return result;
        }

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

    public IReadOnlyList<BreadcrumbFolderItem> GetSubfolders(string? path)
    {
        var list = new List<BreadcrumbFolderItem>();
        if (string.IsNullOrWhiteSpace(path)) return list;

        try
        {
            if (_fileSystem is PhysicalFileSystem)
            {
                if (Directory.Exists(path))
                {
                    var options = new EnumerationOptions
                    {
                        IgnoreInaccessible = true,
                        RecurseSubdirectories = false,
                        ReturnSpecialDirectories = false
                    };
                    var dirs = Directory.EnumerateDirectories(path, "*", options);
                    foreach (var d in dirs)
                    {
                        if (!ShowHiddenFiles)
                        {
                            try
                            {
                                var di = new DirectoryInfo(d);
                                if (di.Attributes.HasFlag(FileAttributes.Hidden) || di.Attributes.HasFlag(FileAttributes.System))
                                    continue;
                            }
                            catch { }
                        }

                        string name = Path.GetFileName(d);
                        if (!string.IsNullOrEmpty(name))
                        {
                            list.Add(new BreadcrumbFolderItem(name, d));
                        }
                    }
                }
            }
            else if (_fileSystem.DirectoryExists(path))
            {
                var entries = _fileSystem.EnumerateEntries(path);
                foreach (var entry in entries)
                {
                    var info = _fileSystem.GetInfo(entry);
                    if (info?.IsDirectory == true || _fileSystem.DirectoryExists(entry))
                    {
                        string name = Path.GetFileName(entry);
                        if (string.IsNullOrEmpty(name)) name = entry;
                        list.Add(new BreadcrumbFolderItem(name, entry));
                    }
                }
            }
        }
        catch
        {
            // Ignore access errors
        }

        return list.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    #region Live Auto-Refresh & Folder Watching

    private void StartWatcher(string path)
    {
        StopWatcher();
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            _folderWatcher = _folderWatcherFactory.CreateWatcher(path);
            if (_folderWatcher != null)
            {
                _folderWatcher.Changed += OnFolderWatcherChanged;
                _folderWatcher.Error += OnFolderWatcherError;
                _folderWatcher.FolderDeleted += OnWatchedFolderDeleted;
                _folderWatcher.EnableRaisingEvents = true;
            }
        }
        catch
        {
            // Suppress watcher initialization errors; folder remains viewable
        }
    }

    private void StopWatcher()
    {
        lock (_watcherLock)
        {
            _pendingWatcherEvents.Clear();
            _watcherDebounceTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        if (_folderWatcher != null)
        {
            try
            {
                _folderWatcher.EnableRaisingEvents = false;
                _folderWatcher.Changed -= OnFolderWatcherChanged;
                _folderWatcher.Error -= OnFolderWatcherError;
                _folderWatcher.FolderDeleted -= OnWatchedFolderDeleted;
                _folderWatcher.Dispose();
            }
            catch
            {
                // Suppress disposal errors
            }
            _folderWatcher = null;
        }
    }

    private void OnFolderWatcherChanged(object? sender, FolderChangeEventArgs e)
    {
        lock (_watcherLock)
        {
            _pendingWatcherEvents.Add(e);
            _watcherDebounceTimer ??= TimeProvider.CreateTimer(OnWatcherDebounceTimerFired, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _watcherDebounceTimer.Change(TimeSpan.FromMilliseconds(150), Timeout.InfiniteTimeSpan);
        }
    }

    private Task _lastProcessTask = Task.CompletedTask;

    public async Task WaitForPendingWatcherUpdatesAsync()
    {
        Task task;
        lock (_watcherLock)
        {
            task = _lastProcessTask;
        }
        await task;
    }

    private void OnWatcherDebounceTimerFired(object? state)
    {
        List<FolderChangeEventArgs> batch;
        lock (_watcherLock)
        {
            if (_pendingWatcherEvents.Count == 0) return;
            batch = _pendingWatcherEvents.ToList();
            _pendingWatcherEvents.Clear();
        }

        var task = ProcessWatcherEventsAsync(batch);
        lock (_watcherLock)
        {
            _lastProcessTask = task;
        }
    }

    private async Task ProcessWatcherEventsAsync(List<FolderChangeEventArgs> batch)
    {
        var coalesced = FolderChangeCoalescer.Coalesce(batch);
        if (coalesced.RequiresFullRefresh)
        {
            RunOnUi(() => _ = RefreshAsync());
            return;
        }

        // Pre-fetch entries on background thread
        var addedEntries = new List<FileEntry>();
        var modifiedEntries = new List<FileEntry>();

        await Task.Run(() =>
        {
            foreach (var path in coalesced.Added)
            {
                var info = _fileSystem.GetInfo(path);
                if (info != null && info.Exists)
                {
                    bool isHidden = info.Attributes.HasFlag(FileAttributes.Hidden);
                    bool isSystem = info.Attributes.HasFlag(FileAttributes.System);
                    if (ShowHiddenFiles || (!isHidden && !isSystem))
                    {
                        addedEntries.Add(new FileEntry(Path.GetFileName(path), path, info.IsDirectory, info.Size, info.Modified, isHidden, isSystem));
                    }
                }
            }

            foreach (var path in coalesced.Modified)
            {
                var info = _fileSystem.GetInfo(path);
                if (info != null && info.Exists)
                {
                    bool isHidden = info.Attributes.HasFlag(FileAttributes.Hidden);
                    bool isSystem = info.Attributes.HasFlag(FileAttributes.System);
                    modifiedEntries.Add(new FileEntry(Path.GetFileName(path), path, info.IsDirectory, info.Size, info.Modified, isHidden, isSystem));
                }
            }
        });

        RunOnUi(() =>
        {
            if (string.IsNullOrEmpty(CurrentPath) || IsLoading) return;

            var selectedPaths = new HashSet<string>(SelectedItems.Select(i => i.FullPath), StringComparer.OrdinalIgnoreCase);
            bool collectionModified = false;

            // 1. Removals
            foreach (var removedPath in coalesced.Removed)
            {
                FileItemViewModel? target;
                lock (_itemsLock)
                {
                    target = _unfilteredItems.FirstOrDefault(i => string.Equals(i.FullPath, removedPath, StringComparison.OrdinalIgnoreCase));
                    if (target != null)
                    {
                        _unfilteredItems.Remove(target);
                    }
                }
                if (target != null)
                {
                    Items.Remove(target);
                    selectedPaths.Remove(removedPath);
                    collectionModified = true;
                }
            }

            // 2. Renames
            foreach (var (oldPath, newPath) in coalesced.Renamed)
            {
                FileItemViewModel? target;
                lock (_itemsLock)
                {
                    target = _unfilteredItems.FirstOrDefault(i => string.Equals(i.FullPath, oldPath, StringComparison.OrdinalIgnoreCase));
                }
                if (target != null)
                {
                    if (target.IsRenaming) continue;

                    var info = _fileSystem.GetInfo(newPath);
                    if (info != null && info.Exists)
                    {
                        bool isHidden = info.Attributes.HasFlag(FileAttributes.Hidden);
                        bool isSystem = info.Attributes.HasFlag(FileAttributes.System);
                        if (!ShowHiddenFiles && (isHidden || isSystem))
                        {
                            lock (_itemsLock) { _unfilteredItems.Remove(target); }
                            Items.Remove(target);
                            selectedPaths.Remove(oldPath);
                            collectionModified = true;
                            continue;
                        }

                        var newEntry = new FileEntry(Path.GetFileName(newPath), newPath, info.IsDirectory, info.Size, info.Modified, isHidden, isSystem);
                        target.UpdateEntry(newEntry);

                        bool matchesFilter = string.IsNullOrWhiteSpace(FilterText) || NameMatcher.Matches(target.Name, FilterText);
                        if (matchesFilter && !Items.Contains(target))
                        {
                            int insertIdx = FileEntrySorter.FindSortedIndex(Items, newEntry, vm => vm.Entry, CurrentSortColumn, IsSortAscending);
                            Items.Insert(insertIdx, target);
                        }
                        else if (!matchesFilter && Items.Contains(target))
                        {
                            Items.Remove(target);
                        }

                        if (selectedPaths.Remove(oldPath))
                        {
                            selectedPaths.Add(newPath);
                        }
                        collectionModified = true;
                    }
                }
            }

            // 3. Modifications
            foreach (var newEntry in modifiedEntries)
            {
                FileItemViewModel? target;
                lock (_itemsLock)
                {
                    target = _unfilteredItems.FirstOrDefault(i => string.Equals(i.FullPath, newEntry.FullPath, StringComparison.OrdinalIgnoreCase));
                }
                if (target != null)
                {
                    target.UpdateEntry(newEntry);

                    // Re-sort if sorted by Size or Modified and position shifted
                    if (CurrentSortColumn == FileSortColumn.Size || CurrentSortColumn == FileSortColumn.Modified)
                    {
                        int currentIdx = Items.IndexOf(target);
                        if (currentIdx >= 0)
                        {
                            Items.RemoveAt(currentIdx);
                            int newIdx = FileEntrySorter.FindSortedIndex(Items, newEntry, vm => vm.Entry, CurrentSortColumn, IsSortAscending);
                            Items.Insert(newIdx, target);
                        }
                    }
                    collectionModified = true;
                }
            }

            // 4. Additions
            foreach (var newEntry in addedEntries)
            {
                bool alreadyPresent;
                lock (_itemsLock)
                {
                    alreadyPresent = _unfilteredItems.Any(i => string.Equals(i.FullPath, newEntry.FullPath, StringComparison.OrdinalIgnoreCase));
                }
                if (alreadyPresent) continue;

                var newVm = new FileItemViewModel(newEntry, _iconService, _loadCts?.Token ?? CancellationToken.None);
                lock (_itemsLock)
                {
                    _unfilteredItems.Add(newVm);
                }

                bool matchesFilter = string.IsNullOrWhiteSpace(FilterText) || NameMatcher.Matches(newVm.Name, FilterText);
                if (matchesFilter)
                {
                    int insertIdx = FileEntrySorter.FindSortedIndex(Items, newEntry, vm => vm.Entry, CurrentSortColumn, IsSortAscending);
                    Items.Insert(insertIdx, newVm);
                }
                collectionModified = true;
            }

            if (collectionModified)
            {
                var restored = selectedPaths.Count > 0
                    ? Items.Where(i => selectedPaths.Contains(i.FullPath)).ToList()
                    : (IReadOnlyList<FileItemViewModel>)Array.Empty<FileItemViewModel>();
                UpdateSelectedItems(restored);
                OnPropertyChanged(nameof(SelectionStatusText));
            }
        });
    }

    private void OnWatchedFolderDeleted(object? sender, EventArgs e)
    {
        RunOnUi(HandleWatchedFolderDeleted);
    }

    private void HandleWatchedFolderDeleted()
    {
        string oldPath = CurrentPath;
        string folderName = CurrentFolderName;

        string? nearestParent = Path.GetDirectoryName(oldPath);
        while (!string.IsNullOrEmpty(nearestParent) && !_fileSystem.DirectoryExists(nearestParent))
        {
            nearestParent = Path.GetDirectoryName(nearestParent);
        }

        if (string.IsNullOrEmpty(nearestParent) || !_fileSystem.DirectoryExists(nearestParent))
        {
            nearestParent = @"C:";
        }

        ShowToast($"Folder '{folderName}' was deleted or moved. Redirected to '{nearestParent}'.");
        Navigate(nearestParent);
    }

    private void OnFolderWatcherError(object? sender, FolderWatcherErrorEventArgs e)
    {
        RunOnUi(() =>
        {
            if (!_fileSystem.DirectoryExists(CurrentPath))
            {
                HandleWatchedFolderDeleted();
            }
            else
            {
                _ = RefreshAsync();
            }
        });
    }

    public void BeginTransferScope() => Interlocked.Increment(ref _activeTransferCount);

    public void EndTransferScope()
    {
        if (Interlocked.Decrement(ref _activeTransferCount) <= 0)
        {
            lock (_watcherLock)
            {
                if (_pendingWatcherEvents.Count > 0)
                {
                    _watcherDebounceTimer?.Change(TimeSpan.FromMilliseconds(50), Timeout.InfiniteTimeSpan);
                }
            }
        }
    }

    public void Dispose()
    {
        _driveService.DrivesChanged -= OnDriveServiceDrivesChanged;
        StopWatcher();
        _watcherDebounceTimer?.Dispose();
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _filterCts?.Cancel();
        _filterCts?.Dispose();
        _toastTimer?.Dispose();
    }

    #endregion


    private void OnDriveServiceDrivesChanged(object? sender, EventArgs e)
    {
        if (SpecialMode == SpecialPaneMode.ThisPC)
        {
            RunOnUi(() => _ = RefreshAsync());
        }
    }

    private async Task LoadThisPcViewAsync(NavigationPhaseType phaseType, CancellationToken ct, Func<bool> isCurrent)
    {
        RunOnUi(() =>
        {
            SpecialMode = SpecialPaneMode.ThisPC;
            CurrentPath = "thispc://";
            AddressText = "This PC";
            ErrorMessage = null;
            OnPropertyChanged(nameof(HasError));
            IsLoading = true;
            Drives.Clear();
            SafeReplaceItems(Array.Empty<FileItemViewModel>());

            switch (phaseType)
            {
                case NavigationPhaseType.New:
                    _history.CommitNavigate("thispc://");
                    break;
                case NavigationPhaseType.Back:
                    _history.CommitBack();
                    break;
                case NavigationPhaseType.Forward:
                    _history.CommitForward();
                    break;
            }
            UpdateHistoryCommandStates();
            OnPropertyChanged(nameof(Breadcrumbs));
            OnPropertyChanged(nameof(CurrentFolderName));
            OnPropertyChanged(nameof(SelectionStatusText));
        });

        try
        {
            var driveList = await _driveService.GetDrivesAsync(ct).ConfigureAwait(false);
            if (!isCurrent()) return;

            var cards = driveList.Select(d => new DriveCardViewModel(
                d,
                onOpen: path => Navigate(path),
                onEject: path => _ = EjectDriveAsync(path)
            )).ToList();

            foreach (var card in cards)
            {
                try
                {
                    var icon = await _iconService.GetIconAsync(card.Path, isDirectory: true, isDrive: true).ConfigureAwait(false);
                    if (icon != null) card.Icon = icon;
                }
                catch { }
            }

            if (!isCurrent()) return;

            RunOnUi(() =>
            {
                Drives.Clear();
                foreach (var c in cards) Drives.Add(c);
                IsLoading = false;
                OnPropertyChanged(nameof(SelectionStatusText));
            });
        }
        catch (Exception ex)
        {
            if (!isCurrent()) return;
            RunOnUi(() =>
            {
                ErrorMessage = $"Không thể tải danh sách ổ đĩa: {ex.Message}";
                IsLoading = false;
                OnPropertyChanged(nameof(HasError));
            });
        }
    }

    private async Task LoadRecycleBinViewAsync(NavigationPhaseType phaseType, CancellationToken ct, Func<bool> isCurrent)
    {
        RunOnUi(() =>
        {
            SpecialMode = SpecialPaneMode.RecycleBin;
            CurrentPath = "recyclebin://";
            AddressText = "Recycle Bin";
            ErrorMessage = null;
            OnPropertyChanged(nameof(HasError));
            IsLoading = true;
            RecycleBinItems.Clear();
            SafeReplaceItems(Array.Empty<FileItemViewModel>());

            switch (phaseType)
            {
                case NavigationPhaseType.New:
                    _history.CommitNavigate("recyclebin://");
                    break;
                case NavigationPhaseType.Back:
                    _history.CommitBack();
                    break;
                case NavigationPhaseType.Forward:
                    _history.CommitForward();
                    break;
            }
            UpdateHistoryCommandStates();
            OnPropertyChanged(nameof(Breadcrumbs));
            OnPropertyChanged(nameof(CurrentFolderName));
            OnPropertyChanged(nameof(SelectionStatusText));
        });

        try
        {
            var binItems = await _recycleBinService.GetItemsAsync(ct).ConfigureAwait(false);
            if (!isCurrent()) return;

            var itemVms = binItems.Select(item => new RecycleBinItemViewModel(
                item,
                onRestore: async id =>
                {
                    bool ok = await _recycleBinService.RestoreItemAsync(id, CancellationToken.None);
                    if (ok) await RefreshAsync();
                },
                onDelete: async id =>
                {
                    bool ok = await _recycleBinService.PermanentlyDeleteItemAsync(id, CancellationToken.None);
                    if (ok) await RefreshAsync();
                }
            )).ToList();

            foreach (var vm in itemVms)
            {
                try
                {
                    var icon = await _iconService.GetIconAsync(vm.Name, isDirectory: vm.IsDirectory, isDrive: false).ConfigureAwait(false);
                    if (icon != null) vm.Icon = icon;
                }
                catch { }
            }

            if (!isCurrent()) return;

            RunOnUi(() =>
            {
                RecycleBinItems.Clear();
                foreach (var vm in itemVms) RecycleBinItems.Add(vm);
                IsLoading = false;
                OnPropertyChanged(nameof(SelectionStatusText));
            });
        }
        catch (Exception ex)
        {
            if (!isCurrent()) return;
            RunOnUi(() =>
            {
                ErrorMessage = $"Không thể mở Thùng rác: {ex.Message}";
                IsLoading = false;
                OnPropertyChanged(nameof(HasError));
            });
        }
    }

    [RelayCommand]
    public async Task EmptyRecycleBinAsync()
    {
        bool ok = await _recycleBinService.EmptyRecycleBinAsync(confirm: true);
        if (ok) await RefreshAsync();
    }

    [RelayCommand]
    public async Task EjectDriveAsync(string drivePath)
    {
        bool ok = await _driveService.EjectDriveAsync(drivePath);
        if (ok) await RefreshAsync();
    }


    [RelayCommand]
    public void OpenFindBar()
    {
        IsFindBarOpen = true;
        FindBarOpened?.Invoke();
    }

    [RelayCommand]
    public void CloseFindBar()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;

        _searchDebounceCts?.Cancel();
        _searchDebounceCts?.Dispose();
        _searchDebounceCts = null;

        IsSearching = false;
        IsFindBarOpen = false;

        if (IsSearchMode)
        {
            IsSearchMode = false;
            SearchQueryText = string.Empty;
            SearchResultCountText = string.Empty;
            SearchWarningText = null;
            SearchSkippedText = null;
            IsResultsLimited = false;

            if (_savedFolderItems != null)
            {
                SafeReplaceItems(_savedFolderItems);
                lock (_itemsLock)
                {
                    _unfilteredItems.Clear();
                    _unfilteredItems.AddRange(_savedFolderItems);
                }
                _savedFolderItems = null;
            }
            else
            {
                _ = RefreshAsync();
            }
            OnPropertyChanged(nameof(SelectionStatusText));
        }
    }

    [RelayCommand]
    public void StopSearch()
    {
        _searchCts?.Cancel();
        IsSearching = false;
    }

    [RelayCommand]
    public async Task ExecuteSearchAsync()
    {
        await PerformSearchAsync(SearchQueryText);
    }

    [RelayCommand]
    public async Task ShowInContainingFolderAsync()
    {
        var item = SelectedItem;
        if (item == null) return;

        string parentDir = item.ParentDirectory;
        string fileName = item.Name;

        CloseFindBar();

        if (!string.IsNullOrEmpty(parentDir) && _fileSystem.DirectoryExists(parentDir))
        {
            await NavigateAndSelectAsync(parentDir, fileName);
        }
    }

    partial void OnSearchQueryTextChanged(string value)
    {
        _searchDebounceCts?.Cancel();
        _searchDebounceCts?.Dispose();
        _searchDebounceCts = new CancellationTokenSource();
        var token = _searchDebounceCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(250, token);
                if (!token.IsCancellationRequested)
                {
                    RunOnUi(() => _ = PerformSearchAsync(value));
                }
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    partial void OnSelectedSearchScopeChanged(SearchScope value)
    {
        if (IsFindBarOpen && !string.IsNullOrWhiteSpace(SearchQueryText))
        {
            _ = PerformSearchAsync(SearchQueryText);
        }
    }

    public async Task PerformSearchAsync(string rawQuery)
    {
        if (string.IsNullOrWhiteSpace(rawQuery))
        {
            if (IsSearchMode)
            {
                CloseFindBar();
            }
            return;
        }

        if (!IsSearchMode)
        {
            lock (_itemsLock)
            {
                _savedFolderItems = _unfilteredItems.Count > 0 ? _unfilteredItems.ToList() : Items.ToList();
            }
            IsSearchMode = true;
        }

        var parseResult = SearchQueryParser.Parse(rawQuery, TimeProvider);
        if (!parseResult.IsValid)
        {
            SearchWarningText = parseResult.ErrorMessage;
            IsSearching = false;
            return;
        }

        if (parseResult.Warnings.Count > 0)
        {
            SearchWarningText = string.Join("; ", parseResult.Warnings);
        }
        else
        {
            SearchWarningText = null;
        }

        IReadOnlyList<string> roots;
        switch (SelectedSearchScope)
        {
            case SearchScope.OpenPanes:
                roots = GetOpenPanePaths?.Invoke() ?? (string.IsNullOrEmpty(CurrentPath) ? Array.Empty<string>() : new[] { CurrentPath });
                if (roots.Count == 0 && !string.IsNullOrEmpty(CurrentPath)) roots = new[] { CurrentPath };
                break;
            case SearchScope.ThisPC:
                var approved = GetApprovedRoots?.Invoke() ?? Array.Empty<string>();
                if (approved.Count == 0)
                {
                    SearchWarningText = "This PC search unconfigured; searching current folder.";
                    roots = string.IsNullOrEmpty(CurrentPath) ? Array.Empty<string>() : new[] { CurrentPath };
                }
                else
                {
                    roots = approved;
                }
                break;
            case SearchScope.CurrentFolder:
            default:
                roots = string.IsNullOrEmpty(CurrentPath) ? Array.Empty<string>() : new[] { CurrentPath };
                break;
        }

        if (roots.Count == 0) return;

        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;

        RunOnUi(() =>
        {
            SafeReplaceItems(Array.Empty<FileItemViewModel>());
            lock (_itemsLock) { _unfilteredItems.Clear(); }
            IsSearching = true;
            SearchResultCountText = "Searching...";
        });

        var query = new SearchQuery(
            QueryText: parseResult.CleanQueryText,
            Scope: SelectedSearchScope,
            RootPaths: roots,
            MaxResults: 5000,
            FilterPredicate: parseResult.FilterPredicate);

        int totalFound = 0;
        try
        {
            await foreach (var batch in _searchService.SearchAsync(query, ct))
            {
                if (ct.IsCancellationRequested) break;

                var vms = batch.Select(r => new FileItemViewModel(r.Entry, _iconService, ct)).ToList();
                totalFound += vms.Count;

                RunOnUi(() =>
                {
                    lock (_itemsLock)
                    {
                        _unfilteredItems.AddRange(vms);
                    }
                    SafeAddItems(vms);
                    SearchResultCountText = $"{totalFound:N0} items found";
                    OnPropertyChanged(nameof(SelectionStatusText));
                });
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                RunOnUi(() =>
                {
                    IsSearching = false;
                    IsResultsLimited = _searchService.IsResultsLimited;
                    int skipped = _searchService.SkippedFolderCount;
                    SearchSkippedText = skipped > 0 ? $"{skipped} skipped" : null;
                    string countStr = $"{totalFound:N0} items found";
                    if (IsResultsLimited) countStr += " (Results limited)";
                    SearchResultCountText = countStr;
                    OnPropertyChanged(nameof(SelectionStatusText));
                });
            }
        }
    }

}
