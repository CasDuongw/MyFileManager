using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FM.App.Helpers;
using FM.App.Models;
using FM.App.Services;
using FM.App.Services.QuickOpen;
using FM.Core.FileSystem;
using FM.Core.Operations.Transfer;
using FM.Core.Search;
using FM.Core.Services;

namespace FM.App.ViewModels;

/// <summary>
/// Root ViewModel orchestrating multi-pane workspace, the sidebar, layout switching, and settings persistence.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly IDirectoryService _directoryService;
    private readonly IIconService _iconService;
    private readonly ISettingsService _settingsService;
    private readonly IFileSystem _fileSystem;
    private readonly IInputDialogService? _inputDialogService;
    private readonly IShellLauncher? _shellLauncher;
    private readonly ITransferQueue _transferQueue;
    private readonly ITransferDialogService _dialogService;
    private readonly IUndoJournalStore? _journalStore;
    private readonly IStagingBackupStore? _stagingStore;
    private readonly object _transferStatusLock = new();
    private readonly TimeProvider _timeProvider;
    private readonly RecentFolderList _recentFolders = new();
    public RecentFolderList RecentFolders => _recentFolders;
    private bool _isRestoringSession;
    private ITimer? _recentSaveTimer;
    private readonly object _recentFoldersSaveLock = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, Task> _completionTasks = new();
    private int _activeTransfersCount;

    public ObservableCollection<FilePaneViewModel> Panes { get; } = new();

    private FilePaneViewModel? _fallbackPane;
    private FilePaneViewModel FallbackPane => _fallbackPane ??= CreatePane();

    public FilePaneViewModel LeftPane => (Panes.Count > 0 ? Panes[0] : ActivePane);
    public FilePaneViewModel RightPane => (Panes.Count > 1 ? Panes[1] : (Panes.Count > 0 ? Panes[0] : ActivePane));
    public TransferDrawerViewModel TransferDrawer { get; }
    public QuickOpenViewModel QuickOpen { get; }
    public IFolderIndex FolderIndex { get; } = new FolderIndex();
    public FolderVisitStore VisitStore { get; } = new();

    private FilePaneViewModel? _activePane;

    public FilePaneViewModel ActivePane
    {
        get => _activePane ?? FallbackPane;
        set => SetActivePane(value);
    }

    public bool HasActivePane => _activePane != null && Panes.Contains(_activePane);

    public string ActiveFolderName => _activePane?.CurrentFolderName ?? "MyFileManager";

    private bool _isSyncingFilter;

    [ObservableProperty]
    private string _searchText = string.Empty;

    partial void OnSearchTextChanged(string value)
    {
        if (_isSyncingFilter) return;

        if (ActivePane != null && !string.Equals(ActivePane.FilterText, value, StringComparison.Ordinal))
        {
            ActivePane.FilterText = value;
        }
    }

    [RelayCommand]
    public void SetGlobalViewMode(string modeStr)
    {
        foreach (var p in Panes)
        {
            p.SetViewMode(modeStr);
        }
    }

    private FilePaneViewModel? _lastActivePane;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanClosePanes))]
    private PaneLayoutMode _layoutMode = PaneLayoutMode.DualVertical;

    public bool CanClosePanes => Panes.Count > 0;

    [ObservableProperty]
    private bool _showHiddenFiles;

    public ObservableCollection<SidebarItemViewModel> SidebarFavorites { get; } = new();
    public ObservableCollection<SidebarItemViewModel> SidebarDrives { get; } = new();

    public MainViewModel(
        IDirectoryService directoryService,
        IIconService iconService,
        ISettingsService settingsService,
        IFileSystem? fileSystem = null,
        IInputDialogService? inputDialogService = null,
        ITransferQueue? transferQueue = null,
        IShellLauncher? shellLauncher = null,
        ITransferDialogService? transferDialogService = null,
        IUndoJournalStore? journalStore = null,
        IStagingBackupStore? stagingStore = null,
        TransferDrawerViewModel? transferDrawer = null,
        TimeProvider? timeProvider = null)
    {
        _directoryService = directoryService;
        _iconService = iconService;
        _settingsService = settingsService;
        _fileSystem = fileSystem ?? new PhysicalFileSystem();
        _inputDialogService = inputDialogService;
        _shellLauncher = shellLauncher;
        _stagingStore = stagingStore;
        _journalStore = journalStore;
        _transferQueue = transferQueue ?? new TransferQueue(_fileSystem, _stagingStore, _journalStore);
        _dialogService = transferDialogService ?? new TransferDialogService();
        TransferDrawer = transferDrawer ?? new TransferDrawerViewModel(_transferQueue);
        QuickOpen = new QuickOpenViewModel(new QuickOpenSourceProvider(this, _fileSystem, FolderIndex, VisitStore));
        _timeProvider = timeProvider ?? TimeProvider.System;

        // Initialize with default 2 panes
        var left = CreatePane();
        var right = CreatePane();
        Panes.Add(left);
        Panes.Add(right);

        _activePane = left;
        left.IsActive = true;
        right.IsActive = false;

        _transferQueue.ProgressChanged += OnTransferQueueProgressChanged;

        if (_fileSystem is not InMemoryFileSystem)
        {
            _ = PopulateSidebarAsync();
            StartFolderIndexCrawl();
        }
    }

    public FilePaneViewModel CreatePane(string? initialPath = null)
    {
        var pane = new FilePaneViewModel(
            _directoryService,
            _iconService,
            _fileSystem,
            _inputDialogService,
            shellLauncher: _shellLauncher);

        pane.ShowHiddenFiles = ShowHiddenFiles;
        pane.Activated += OnPaneActivated;
        pane.SelectionChanged += OnTransferCanExecuteChanged;
        pane.PropertyChanged += OnPanePropertyChanged;
        pane.FolderOpened += OnPaneFolderOpened;
        pane.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(FilePaneViewModel.CurrentPath))
                OnTransferCanExecuteChanged();
        };

        if (!string.IsNullOrWhiteSpace(initialPath))
        {
            pane.CurrentPath = initialPath;
            pane.AddressText = initialPath;
            pane.Navigate(initialPath);
        }

        return pane;
    }

    public FilePaneViewModel TargetPane
    {
        get
        {
            if (Panes.Count <= 1) return ActivePane ?? FallbackPane;
            if (_lastActivePane != null && _lastActivePane != ActivePane && Panes.Contains(_lastActivePane))
            {
                return _lastActivePane;
            }
            int idx = ActivePane != null ? Panes.IndexOf(ActivePane) : -1;
            int nextIdx = (idx >= 0 && idx + 1 < Panes.Count) ? idx + 1 : 0;
            return Panes[nextIdx];
        }
    }

    public bool CanTransferToOtherPane =>
        ActivePane != null &&
        ActivePane.SelectedItems.Count > 0 &&
        Panes.Count > 1 &&
        !string.IsNullOrWhiteSpace(TargetPane.CurrentPath) &&
        !string.Equals(ActivePane.CurrentPath, TargetPane.CurrentPath, StringComparison.OrdinalIgnoreCase);

    private void OnTransferCanExecuteChanged()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.HasShutdownStarted && !dispatcher.CheckAccess())
        {
            _ = dispatcher.InvokeAsync(() =>
            {
                CopyToOtherPaneCommand.NotifyCanExecuteChanged();
                MoveToOtherPaneCommand.NotifyCanExecuteChanged();
            });
            return;
        }

        CopyToOtherPaneCommand.NotifyCanExecuteChanged();
        MoveToOtherPaneCommand.NotifyCanExecuteChanged();
    }

    private void OnTransferQueueProgressChanged(object? sender, TransferProgressEventArgs e)
    {
        // Detailed progress is handled in TransferDrawerViewModel.
        // Status bar on pane only reflects consolidated summary to prevent clobbering dialog statuses.
    }

    [RelayCommand(CanExecute = nameof(CanTransferToOtherPane), AllowConcurrentExecutions = true)]
    public async Task CopyToOtherPaneAsync()
    {
        var sourcePaths = ActivePane.SelectedItems.Select(x => x.FullPath).ToList();
        var destDir = TargetPane.CurrentPath;
        var (_, completionTask) = EnqueueTransferInternal(TransferKind.Copy, sourcePaths, destDir, ActivePane);
        if (completionTask != null)
        {
            await completionTask;
        }
    }

    [RelayCommand(CanExecute = nameof(CanTransferToOtherPane), AllowConcurrentExecutions = true)]
    public async Task MoveToOtherPaneAsync()
    {
        var sourcePaths = ActivePane.SelectedItems.Select(x => x.FullPath).ToList();
        var destDir = TargetPane.CurrentPath;
        var (_, completionTask) = EnqueueTransferInternal(TransferKind.Move, sourcePaths, destDir, ActivePane);
        if (completionTask != null)
        {
            await completionTask;
        }
    }

    public ITransferJob? EnqueueTransfer(
        TransferKind kind,
        IReadOnlyList<string> sourcePaths,
        string destDir,
        FilePaneViewModel ownerPane)
    {
        return EnqueueTransferInternal(kind, sourcePaths, destDir, ownerPane).Job;
    }

    private (ITransferJob? Job, Task? CompletionTask) EnqueueTransferInternal(
        TransferKind kind,
        IReadOnlyList<string> sourcePaths,
        string destDir,
        FilePaneViewModel ownerPane)
    {
        if (sourcePaths == null || sourcePaths.Count == 0 || string.IsNullOrWhiteSpace(destDir))
        {
            return (null, null);
        }

        var interaction = new WpfTransferInteraction(_dialogService, () =>
        {
            ownerPane.SetStatusText("Waiting for your decision...");
        });

        var request = new TransferRequest(
            kind,
            sourcePaths,
            destDir,
            new TransferOptions(DefaultConflict: ConflictAction.KeepBoth),
            interaction);

        lock (_transferStatusLock)
        {
            _activeTransfersCount++;
            ownerPane.SetStatusText(_activeTransfersCount == 1 ? "1 transfer running..." : $"{_activeTransfersCount} transfers running...");
        }

        var job = _transferQueue.Enqueue(request);

        var completionTask = HandleJobCompletionAsync(job, kind, ownerPane);
        _completionTasks[job.Id] = completionTask;
        return (job, completionTask);
    }

    public async Task WaitForJobCompletionAsync(ITransferJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        await job.Completion.ConfigureAwait(false);
        if (_completionTasks.TryGetValue(job.Id, out var task))
        {
            await task.ConfigureAwait(false);
            _completionTasks.TryRemove(job.Id, out _);
        }
    }

    private async Task HandleJobCompletionAsync(ITransferJob job, TransferKind kind, FilePaneViewModel ownerPane)
    {
        TransferResult result;
        try
        {
            result = await job.Completion.ConfigureAwait(false);
        }
        catch
        {
            result = new TransferResult(TransferState.Failed, Array.Empty<TransferItemResult>());
        }

        try
        {
            await RunOnUiAsync(async () =>
            {
                // 1. Refresh panes
                await RefreshPanesAfterTransferAsync().ConfigureAwait(true);

                // 2. Summary
                int failed = result.Items.Count(i => i.Outcome == ItemOutcome.Failed);
                int skipped = result.Items.Count(i => i.Outcome == ItemOutcome.Skipped);
                int transferred = result.Items.Count(i => i.Outcome == ItemOutcome.Copied || i.Outcome == ItemOutcome.Moved || i.Outcome == ItemOutcome.KeptBoth || i.Outcome == ItemOutcome.Replaced);

                string summary;
                if (result.FinalState == TransferState.Cancelled)
                {
                    summary = transferred > 0
                        ? $"Transfer cancelled. {transferred} item(s) transferred."
                        : "Transfer cancelled.";
                }
                else if (failed > 0 || skipped > 0)
                {
                    summary = $"Completed with warnings: {transferred} transferred, {failed} failed, {skipped} skipped.";
                }
                else
                {
                    string verbPast = kind == TransferKind.Copy ? "Copied" : "Moved";
                    summary = $"{verbPast} {transferred} item(s) successfully.";
                }

                lock (_transferStatusLock)
                {
                    _activeTransfersCount = Math.Max(0, _activeTransfersCount - 1);
                    if (_activeTransfersCount > 0)
                    {
                        ownerPane.SetStatusText(_activeTransfersCount == 1 ? "1 transfer running..." : $"{_activeTransfersCount} transfers running...");
                    }
                    else
                    {
                        ownerPane.SetStatusText(summary, TimeSpan.FromSeconds(5));
                    }
                }

                // 3. Push TransferUndoOperation if undoable
                var undoOp = new TransferUndoOperation(_fileSystem, kind, result, _stagingStore, job.Id, _journalStore);
                if (undoOp.CanUndo)
                {
                    ownerPane.UndoStack.Push(undoOp);
                }
            }).ConfigureAwait(false);
        }
        catch
        {
            lock (_transferStatusLock)
            {
                _activeTransfersCount = Math.Max(0, _activeTransfersCount - 1);
                if (_activeTransfersCount > 0)
                {
                    ownerPane.SetStatusText(_activeTransfersCount == 1 ? "1 transfer running..." : $"{_activeTransfersCount} transfers running...");
                }
            }
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

    private async Task RefreshPanesAfterTransferAsync()
    {
        await Task.WhenAll(Panes.Select(p => p.RefreshAsync()));
    }

    private void OnPaneActivated(FilePaneViewModel pane)
    {
        SetActivePane(pane);
    }

    private void OnPanePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FilePaneViewModel.FilterText) && sender == ActivePane)
        {
            if (!_isSyncingFilter)
            {
                _isSyncingFilter = true;
                try
                {
                    SearchText = ActivePane.FilterText;
                }
                finally
                {
                    _isSyncingFilter = false;
                }
            }
        }
    }


    public void SetActivePane(FilePaneViewModel? pane)
    {
        if (_activePane == pane) return;

        _lastActivePane = _activePane;
        _activePane = pane;

        _isSyncingFilter = true;
        try
        {
            SearchText = pane?.FilterText ?? string.Empty;
        }
        finally
        {
            _isSyncingFilter = false;
        }

        OnPropertyChanged(nameof(ActivePane));
        OnPropertyChanged(nameof(ActiveFolderName));
        OnPropertyChanged(nameof(HasActivePane));

        foreach (var p in Panes)
        {
            p.IsActive = (p == pane);
        }

        OnTransferCanExecuteChanged();
    }

    [RelayCommand]
    public void SwitchActivePane()
    {
        if (Panes.Count <= 1) return;
        int idx = Panes.IndexOf(ActivePane);
        int next = (idx >= 0 && idx + 1 < Panes.Count) ? idx + 1 : 0;
        SetActivePane(Panes[next]);
    }

    [RelayCommand]
    public void SplitVertical(FilePaneViewModel? targetPane = null)
    {
        SplitVerticalInternal(targetPane, null);
    }

    public void SplitVertical(FilePaneViewModel? targetPane, string? initialPath)
    {
        SplitVerticalInternal(targetPane, initialPath);
    }

    private void SplitVerticalInternal(FilePaneViewModel? targetPane, string? initialPath)
    {
        if (Panes.Count >= 16) return;
        var source = targetPane ?? ActivePane;
        var startPath = !string.IsNullOrWhiteSpace(initialPath) ? initialPath : source?.CurrentPath;
        var newPane = CreatePane(startPath);
        int insertIdx = source != null ? Panes.IndexOf(source) + 1 : Panes.Count;
        if (insertIdx <= 0 || insertIdx > Panes.Count) insertIdx = Panes.Count;
        Panes.Insert(insertIdx, newPane);

        if (Panes.Count == 2) LayoutMode = PaneLayoutMode.DualVertical;
        else if (Panes.Count == 3) LayoutMode = PaneLayoutMode.TripleLeft;
        else if (Panes.Count >= 4) LayoutMode = PaneLayoutMode.QuadGrid;

        SetActivePane(newPane);
        if (!string.IsNullOrWhiteSpace(initialPath) && newPane.CurrentPath != initialPath)
        {
            newPane.Navigate(initialPath);
        }
        OnPropertyChanged(nameof(CanClosePanes));
        ClosePaneCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    public void SplitHorizontal(FilePaneViewModel? targetPane = null)
    {
        if (Panes.Count >= 16) return;
        var source = targetPane ?? ActivePane;
        var newPane = CreatePane(source?.CurrentPath);
        int insertIdx = source != null ? Panes.IndexOf(source) + 1 : Panes.Count;
        if (insertIdx <= 0 || insertIdx > Panes.Count) insertIdx = Panes.Count;
        Panes.Insert(insertIdx, newPane);

        if (Panes.Count == 2) LayoutMode = PaneLayoutMode.DualHorizontal;
        else if (Panes.Count == 3) LayoutMode = PaneLayoutMode.TripleLeft;
        else if (Panes.Count >= 4) LayoutMode = PaneLayoutMode.QuadGrid;

        SetActivePane(newPane);
        OnPropertyChanged(nameof(CanClosePanes));
        ClosePaneCommand.NotifyCanExecuteChanged();
    }

    public void SwapPanes(int sIdx, int tIdx)
    {
        if (sIdx == tIdx || sIdx < 0 || tIdx < 0 || sIdx >= Panes.Count || tIdx >= Panes.Count)
            return;

        if (Math.Abs(sIdx - tIdx) == 1)
        {
            Panes.Move(sIdx, tIdx);
        }
        else
        {
            int lower = Math.Min(sIdx, tIdx);
            int higher = Math.Max(sIdx, tIdx);
            Panes.Move(lower, higher);
            Panes.Move(higher - 1, lower);
        }
    }

    public void DockPane(FilePaneViewModel sourcePane, FilePaneViewModel targetPane, DockEdge edge)
    {
        if (sourcePane == null || targetPane == null || sourcePane == targetPane) return;
        if (!Panes.Contains(sourcePane) || !Panes.Contains(targetPane)) return;

        int sIdx = Panes.IndexOf(sourcePane);
        int tIdx = Panes.IndexOf(targetPane);

        if (edge == DockEdge.Center)
        {
            // Direct Swap of source and target panes using Panes.Move without duplicate intermediates
            SwapPanes(sIdx, tIdx);
            SetActivePane(sourcePane);
            OnPropertyChanged(nameof(CanClosePanes));
            return;
        }

        if (Panes.Count == 3 && LayoutMode == PaneLayoutMode.TripleLeft)
        {
            // In TripleLeft: Panes[0] is Left, Panes[1] is Top-Right, Panes[2] is Bottom-Right.
            // 1. Any drop onto Panes[0] (the Left pane) moves the dragged pane to the Left!
            if (tIdx == 0)
            {
                SwapPanes(sIdx, 0);
                SetActivePane(sourcePane);
                OnPropertyChanged(nameof(CanClosePanes));
                return;
            }

            // 2. Dragging from Panes[0] (Left) to a right pane:
            if (sIdx == 0)
            {
                SwapPanes(0, tIdx);
                SetActivePane(sourcePane);
                OnPropertyChanged(nameof(CanClosePanes));
                return;
            }

            // 3. Dragging between the two right panes (Panes[1] and Panes[2]):
            if (sIdx != 0 && tIdx != 0)
            {
                if (edge == DockEdge.Top)
                {
                    if (sIdx != 1)
                    {
                        SwapPanes(1, 2);
                    }
                }
                else if (edge == DockEdge.Bottom)
                {
                    if (sIdx != 2)
                    {
                        SwapPanes(1, 2);
                    }
                }
                else
                {
                    SwapPanes(sIdx, tIdx);
                }

                SetActivePane(sourcePane);
                OnPropertyChanged(nameof(CanClosePanes));
                return;
            }
        }

        int origSourceIdx = Panes.IndexOf(sourcePane);
        int origTargetIdx = Panes.IndexOf(targetPane);

        Panes.Remove(sourcePane);
        int targetIdx = Panes.IndexOf(targetPane);

        if (edge == DockEdge.Right || edge == DockEdge.Bottom)
        {
            Panes.Insert(targetIdx + 1, sourcePane);
        }
        else
        {
            Panes.Insert(targetIdx, sourcePane);
        }

        // If the resulting index did not change (no-op because source was already at that relative position),
        // swap them so user drag ALWAYS produces a visible move!
        int newSourceIdx = Panes.IndexOf(sourcePane);
        if (newSourceIdx == origSourceIdx && Panes.Count > 1)
        {
            Panes.Remove(sourcePane);
            int swapTargetIdx = Panes.IndexOf(targetPane);
            if (edge == DockEdge.Right || edge == DockEdge.Bottom)
            {
                Panes.Insert(swapTargetIdx, sourcePane);
            }
            else
            {
                Panes.Insert(swapTargetIdx + 1, sourcePane);
            }
        }

        if (Panes.Count == 2)
        {
            if (edge == DockEdge.Bottom || edge == DockEdge.Top)
                LayoutMode = PaneLayoutMode.DualHorizontal;
            else
                LayoutMode = PaneLayoutMode.DualVertical;
        }

        SetActivePane(sourcePane);
        OnPropertyChanged(nameof(CanClosePanes));
    }

    [RelayCommand(CanExecute = nameof(CanClosePanes))]
    public void ClosePane(FilePaneViewModel? targetPane = null)
    {
        if (Panes.Count == 0) return;

        var paneToClose = targetPane ?? ActivePane;
        if (paneToClose == null || !Panes.Contains(paneToClose)) return;

        int idx = Panes.IndexOf(paneToClose);
        Panes.Remove(paneToClose);

        paneToClose.Activated -= OnPaneActivated;
        paneToClose.SelectionChanged -= OnTransferCanExecuteChanged;
        paneToClose.FolderOpened -= OnPaneFolderOpened;

        if (ActivePane == paneToClose)
        {
            if (Panes.Count > 0)
            {
                int nextActiveIdx = Math.Min(idx, Panes.Count - 1);
                SetActivePane(Panes[nextActiveIdx]);
            }
            else
            {
                SetActivePane(null);
            }
        }

        if (Panes.Count <= 1)
        {
            LayoutMode = PaneLayoutMode.Single;
        }
        else if (Panes.Count == 2 && (LayoutMode == PaneLayoutMode.TripleLeft || LayoutMode == PaneLayoutMode.QuadGrid))
        {
            LayoutMode = PaneLayoutMode.DualVertical;
        }
        else if (Panes.Count == 3 && LayoutMode == PaneLayoutMode.QuadGrid)
        {
            LayoutMode = PaneLayoutMode.TripleLeft;
        }

        OnPropertyChanged(nameof(CanClosePanes));
        ClosePaneCommand.NotifyCanExecuteChanged();
        OnTransferCanExecuteChanged();
    }

    [RelayCommand]
    public void SetLayout(string layoutStr)
    {
        if (Enum.TryParse<PaneLayoutMode>(layoutStr, ignoreCase: true, out var mode))
        {
            SetLayoutMode(mode);
        }
    }

    public void SetLayoutMode(PaneLayoutMode mode)
    {
        LayoutMode = mode;
        int required = mode switch
        {
            PaneLayoutMode.Single => 1,
            PaneLayoutMode.DualVertical => 2,
            PaneLayoutMode.DualHorizontal => 2,
            PaneLayoutMode.TripleLeft => 3,
            PaneLayoutMode.QuadGrid => 4,
            _ => 2
        };

        while (Panes.Count < required)
        {
            var basePane = ActivePane ?? Panes.LastOrDefault();
            var newPane = CreatePane(basePane?.CurrentPath);
            Panes.Add(newPane);
        }

        while (Panes.Count > required && Panes.Count > 1)
        {
            int removeIdx = Panes.Count - 1;
            var p = Panes[removeIdx];
            if (p == ActivePane)
            {
                SetActivePane(Panes[0]);
            }
            Panes.RemoveAt(removeIdx);
            p.Activated -= OnPaneActivated;
            p.SelectionChanged -= OnTransferCanExecuteChanged;
            p.PropertyChanged -= OnPanePropertyChanged;
            p.FolderOpened -= OnPaneFolderOpened;
        }

        OnPropertyChanged(nameof(CanClosePanes));
        ClosePaneCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    public void OpenNewTab(string? initialPath = null)
    {
        SplitVerticalInternal(ActivePane, initialPath);
    }

    [RelayCommand]
    public void NavigateActivePane(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            if (ActivePane != null)
            {
                ActivePane.Navigate(path);
            }
            else
            {
                OpenNewTab(path);
            }
        }
    }

    [RelayCommand]
    public async Task ToggleHiddenFilesAsync()
    {
        ShowHiddenFiles = !ShowHiddenFiles;
        foreach (var p in Panes)
        {
            p.ShowHiddenFiles = ShowHiddenFiles;
        }

        var settings = _settingsService.LoadSettings();
        settings.ShowHiddenFiles = ShowHiddenFiles;
        _settingsService.SaveSettings(settings);

        string status = ShowHiddenFiles ? "Hidden files: Shown" : "Hidden files: Hidden";
        ActivePane?.SetStatusText(status, TimeSpan.FromSeconds(3));

        await Task.WhenAll(Panes.Select(p => p.RefreshAsync()));
    }

    public void ToggleHiddenFiles() => _ = ToggleHiddenFilesAsync();

    public async Task InitializeSettingsAsync(AppSettings settings)
    {
        _isRestoringSession = true;
        try
        {
            if (settings.RecentFolders != null)
            {
                _recentFolders.Load(settings.RecentFolders);
            }

            await _settingsService.ValidatePathsAsync(settings);

        ShowHiddenFiles = settings.ShowHiddenFiles;

        var pathsToLoad = (settings.PanePaths != null && settings.PanePaths.Count > 0)
            ? settings.PanePaths
            : new List<string> { settings.LeftPanePath, settings.RightPanePath };

        while (Panes.Count < pathsToLoad.Count)
        {
            Panes.Add(CreatePane());
        }
        while (Panes.Count > pathsToLoad.Count && Panes.Count > 1)
        {
            var p = Panes[Panes.Count - 1];
            Panes.RemoveAt(Panes.Count - 1);
            p.Activated -= OnPaneActivated;
            p.SelectionChanged -= OnTransferCanExecuteChanged;
            p.PropertyChanged -= OnPanePropertyChanged;
            p.FolderOpened -= OnPaneFolderOpened;
        }

        for (int i = 0; i < Panes.Count && i < pathsToLoad.Count; i++)
        {
            Panes[i].ShowHiddenFiles = ShowHiddenFiles;
            Panes[i].Navigate(pathsToLoad[i]);
        }

        if (Enum.TryParse<PaneLayoutMode>(settings.LayoutMode, ignoreCase: true, out var savedMode))
        {
            LayoutMode = savedMode;
        }
        else if (Panes.Count >= 4)
        {
            LayoutMode = PaneLayoutMode.QuadGrid;
        }
        else if (Panes.Count == 3)
        {
            LayoutMode = PaneLayoutMode.TripleLeft;
        }
        else if (Panes.Count == 2)
        {
            LayoutMode = PaneLayoutMode.DualVertical;
        }
        else
        {
            LayoutMode = PaneLayoutMode.Single;
        }

        SetActivePane(Panes[0]);
        OnPropertyChanged(nameof(CanClosePanes));
        ClosePaneCommand.NotifyCanExecuteChanged();

        // Recover interrupted operations and restore Undo stack from persistent journal
        if (_journalStore != null)
        {
            try
            {
                if (_journalStore is FileUndoJournalStore fileJournal)
                {
                    int recovered = await fileJournal.RecoverPendingEntriesAsync(_fileSystem, _stagingStore);
                    await fileJournal.CompactAsync(TimeSpan.FromDays(7), maxEntries: 50);

                    if (recovered > 0)
                    {
                        ActivePane.SetStatusText($"Recovered {recovered} interrupted operation(s)", TimeSpan.FromSeconds(5));
                    }
                }

                var entries = await _journalStore.LoadEntriesAsync();
                int restoredCount = 0;

                foreach (var entry in entries.OrderBy(e => e.Timestamp))
                {
                    var results = entry.Items.Select(i => new TransferItemResult(
                        i.SourcePath,
                        i.DestinationPath,
                        i.IsDirectory,
                        i.Outcome,
                        i.Size,
                        i.Modified,
                        null,
                        i.BackupPath)).ToList();

                    var transferResult = new TransferResult(TransferState.Completed, results);
                    var undoOp = new TransferUndoOperation(_fileSystem, entry.Kind, transferResult, _stagingStore, entry.OperationId, _journalStore);

                    if (undoOp.CanUndo)
                    {
                        var targetPane = (entry.OwnerPane == "Left") ? LeftPane
                            : (entry.OwnerPane == "Right") ? RightPane
                            : ActivePane;

                        targetPane.UndoStack.Push(undoOp);
                        restoredCount++;
                    }
                }

                if (restoredCount > 0)
                {
                    ActivePane.SetStatusText($"Restored {restoredCount} undoable operation(s) from previous session", TimeSpan.FromSeconds(5));
                }
            }
            catch
            {
                // Best effort recovery on startup
            }
        }
        }
        finally
        {
            _isRestoringSession = false;
        }
    }

    private void OnPaneFolderOpened(FilePaneViewModel pane, string path)
    {
        if (_isRestoringSession) return;
        if (string.IsNullOrWhiteSpace(path)) return;
        _recentFolders.Add(path);
        FolderIndex.Add(path);
        VisitStore.RecordVisit(path);
        ScheduleSaveRecentFolders();
    }

    public void ScheduleSaveRecentFolders()
    {
        lock (_recentFoldersSaveLock)
        {
            _recentSaveTimer ??= _timeProvider.CreateTimer(_ => SaveRecentFoldersInternal(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _recentSaveTimer.Change(TimeSpan.FromMilliseconds(1500), Timeout.InfiniteTimeSpan);
        }
    }

    public void SaveRecentFoldersInternal()
    {
        lock (_recentFoldersSaveLock)
        {
            _recentSaveTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        try
        {
            var settings = _settingsService.LoadSettings();
            settings.RecentFolders = _recentFolders.Items.ToList();
            _settingsService.SaveSettings(settings);
        }
        catch
        {
            // Best effort
        }
    }

    public void PersistSettings(double left, double top, double width, double height, string windowState)
    {
        lock (_recentFoldersSaveLock)
        {
            _recentSaveTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        var settings = _settingsService.LoadSettings();
        settings.WindowLeft = left;
        settings.WindowTop = top;
        settings.WindowWidth = width;
        settings.WindowHeight = height;
        settings.WindowState = windowState;
        settings.LeftPanePath = string.IsNullOrEmpty(LeftPane.CurrentPath) ? @"C:\" : LeftPane.CurrentPath;
        settings.RightPanePath = string.IsNullOrEmpty(RightPane.CurrentPath) ? @"C:\" : RightPane.CurrentPath;
        settings.PanePaths = Panes.Select(p => string.IsNullOrEmpty(p.CurrentPath) ? @"C:\" : p.CurrentPath).ToList();
        settings.LayoutMode = LayoutMode.ToString();
        settings.ShowHiddenFiles = ShowHiddenFiles;
        settings.RecentFolders = _recentFolders.Items.ToList();

        _settingsService.SaveSettings(settings);
    }

    public void StartFolderIndexCrawl(CancellationToken cancellationToken = default)
    {
        if (_fileSystem is InMemoryFileSystem) return;

        _ = Task.Run(async () =>
        {
            string cachePath = FolderIndexCache.GetDefaultCachePath();
            if (FolderIndexCache.TryLoad(cachePath, out var cached) && cached.Count > 0)
            {
                FolderIndex.SetEntries(cached);
                FolderIndex.State = FolderIndexState.Ready;
            }

            var crawler = new FolderCrawler(_fileSystem, FolderIndex);
            var settings = _settingsService.LoadSettings();

            var roots = new List<string>();
            if (settings.IndexedRoots != null && settings.IndexedRoots.Count > 0)
            {
                roots.AddRange(settings.IndexedRoots);
            }
            else
            {
                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(userProfile) && Directory.Exists(userProfile))
                {
                    roots.Add(userProfile);
                }

                try
                {
                    foreach (var drive in DriveInfo.GetDrives())
                    {
                        if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                        {
                            roots.Add(drive.RootDirectory.FullName);
                        }
                    }
                }
                catch
                {
                    roots.Add(@"C:\");
                }
            }

            IEnumerable<string> excludes = (settings.IndexExcludes != null && settings.IndexExcludes.Count > 0)
                ? settings.IndexExcludes
                : FolderCrawler.DefaultExcludes;

            await crawler.CrawlAsync(roots, excludes, batchSize: 500, cancellationToken: cancellationToken);

            try
            {
                FolderIndexCache.Save(cachePath, FolderIndex.Snapshot());
            }
            catch
            {
                // Best effort
            }
        }, cancellationToken);
    }

    public async Task PopulateSidebarAsync()
    {
        // 1. Favorites - evaluate paths off UI thread
        var favorites = await Task.Run(() =>
        {
            var list = new List<SidebarItemViewModel>();
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var docsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var downloadsPath = Path.Combine(userProfile, "Downloads");

            if (!string.IsNullOrEmpty(desktopPath) && Directory.Exists(desktopPath))
                list.Add(new SidebarItemViewModel("Desktop", desktopPath, SidebarItemCategory.Favorite));

            if (!string.IsNullOrEmpty(docsPath) && Directory.Exists(docsPath))
                list.Add(new SidebarItemViewModel("Documents", docsPath, SidebarItemCategory.Favorite));

            if (!string.IsNullOrEmpty(downloadsPath) && Directory.Exists(downloadsPath))
                list.Add(new SidebarItemViewModel("Downloads", downloadsPath, SidebarItemCategory.Favorite));

            return list;
        });

        SidebarFavorites.Clear();
        foreach (var item in favorites)
        {
            SidebarFavorites.Add(item);
        }

        // 2. Drives - enumerate drives off UI thread
        var drives = await Task.Run(() =>
        {
            var list = new List<SidebarItemViewModel>();
            try
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (!drive.IsReady) continue;

                    string label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                        ? drive.Name.TrimEnd('\\')
                        : $"{drive.VolumeLabel} ({drive.Name.TrimEnd('\\')})";

                    list.Add(new SidebarItemViewModel(label, drive.RootDirectory.FullName, SidebarItemCategory.Drive));
                }
            }
            catch
            {
                // Fall back gracefully if drive interrogation throws security exception
            }
            return list;
        });

        SidebarDrives.Clear();
        foreach (var drive in drives)
        {
            SidebarDrives.Add(drive);
        }

        // 3. Asynchronously load shell icons for sidebar items
        _ = LoadSidebarIconsAsync();
    }

    public void AddFavorite(string name, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (!SidebarFavorites.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            var item = new SidebarItemViewModel(name, path, SidebarItemCategory.Favorite);
            SidebarFavorites.Add(item);
            _ = LoadSingleSidebarIconAsync(item);
        }
    }

    private async Task LoadSingleSidebarIconAsync(SidebarItemViewModel item)
    {
        try
        {
            var icon = await _iconService.GetIconAsync(item.Path, isDirectory: true, isDrive: false);
            if (icon != null)
            {
                item.Icon = icon;
            }
        }
        catch
        {
            // Non-critical if individual sidebar item icon cannot be retrieved
        }
    }

    private async Task LoadSidebarIconsAsync()
    {
        foreach (var item in SidebarFavorites)
        {
            try
            {
                var icon = await _iconService.GetIconAsync(item.Path, isDirectory: true, isDrive: false);
                if (icon != null)
                {
                    item.Icon = icon;
                }
            }
            catch
            {
                // Non-critical if individual sidebar item icon cannot be retrieved
            }
        }

        foreach (var item in SidebarDrives)
        {
            try
            {
                var icon = await _iconService.GetIconAsync(item.Path, isDirectory: true, isDrive: true);
                if (icon != null)
                {
                    item.Icon = icon;
                }
            }
            catch
            {
                // Non-critical if individual sidebar item icon cannot be retrieved
            }
        }
    }
}