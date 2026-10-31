using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FM.App.Helpers;
using FM.App.Services;
using FM.Core.FileSystem;
using FM.Core.Operations.Transfer;
using FM.Core.Services;

namespace FM.App.ViewModels;

/// <summary>
/// Root ViewModel orchestrating dual file panes, the sidebar, active pane state, and settings persistence.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly IIconService _iconService;
    private readonly ISettingsService _settingsService;
    private readonly IFileSystem _fileSystem;
    private readonly ITransferQueue _transferQueue;
    private readonly ITransferDialogService _dialogService;
    private readonly IUndoJournalStore? _journalStore;
    private readonly IStagingBackupStore? _stagingStore;
    private readonly object _transferStatusLock = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, Task> _completionTasks = new();
    private int _activeTransfersCount;

    public FilePaneViewModel LeftPane { get; }
    public FilePaneViewModel RightPane { get; }
    public TransferDrawerViewModel TransferDrawer { get; }

    [ObservableProperty]
    private FilePaneViewModel _activePane;

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
        TransferDrawerViewModel? transferDrawer = null)
    {
        _iconService = iconService;
        _settingsService = settingsService;
        _fileSystem = fileSystem ?? new PhysicalFileSystem();
        _stagingStore = stagingStore;
        _journalStore = journalStore;
        _transferQueue = transferQueue ?? new TransferQueue(_fileSystem, _stagingStore, _journalStore);
        _dialogService = transferDialogService ?? new TransferDialogService();
        TransferDrawer = transferDrawer ?? new TransferDrawerViewModel(_transferQueue);

        LeftPane = new FilePaneViewModel(directoryService, iconService, _fileSystem, inputDialogService, shellLauncher: shellLauncher);
        RightPane = new FilePaneViewModel(directoryService, iconService, _fileSystem, inputDialogService, shellLauncher: shellLauncher);

        LeftPane.Activated += OnPaneActivated;
        RightPane.Activated += OnPaneActivated;

        LeftPane.SelectionChanged += OnTransferCanExecuteChanged;
        RightPane.SelectionChanged += OnTransferCanExecuteChanged;

        LeftPane.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(FilePaneViewModel.CurrentPath))
                OnTransferCanExecuteChanged();
        };
        RightPane.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(FilePaneViewModel.CurrentPath))
                OnTransferCanExecuteChanged();
        };

        _transferQueue.ProgressChanged += OnTransferQueueProgressChanged;

        // Default to LeftPane active
        _activePane = LeftPane;
        LeftPane.IsActive = true;
        RightPane.IsActive = false;

        _ = PopulateSidebarAsync();
    }

    public FilePaneViewModel TargetPane => ActivePane == LeftPane ? RightPane : LeftPane;

    public bool CanTransferToOtherPane =>
        ActivePane != null &&
        ActivePane.SelectedItems.Count > 0 &&
        !string.IsNullOrWhiteSpace(TargetPane.CurrentPath);

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
        await LeftPane.RefreshAsync();
        await RightPane.RefreshAsync();
    }

    private void OnPaneActivated(FilePaneViewModel pane)
    {
        SetActivePane(pane);
    }

    public void SetActivePane(FilePaneViewModel pane)
    {
        if (ActivePane == pane) return;

        ActivePane = pane;
        LeftPane.IsActive = (pane == LeftPane);
        RightPane.IsActive = (pane == RightPane);
        OnTransferCanExecuteChanged();
    }

    [RelayCommand]
    public void SwitchActivePane()
    {
        SetActivePane(ActivePane == LeftPane ? RightPane : LeftPane);
    }

    [RelayCommand]
    public void NavigateActivePane(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            ActivePane.Navigate(path);
        }
    }

    [RelayCommand]
    public async Task ToggleHiddenFilesAsync()
    {
        ShowHiddenFiles = !ShowHiddenFiles;
        LeftPane.ShowHiddenFiles = ShowHiddenFiles;
        RightPane.ShowHiddenFiles = ShowHiddenFiles;

        var settings = _settingsService.LoadSettings();
        settings.ShowHiddenFiles = ShowHiddenFiles;
        _settingsService.SaveSettings(settings);

        string status = ShowHiddenFiles ? "Hidden files: Shown" : "Hidden files: Hidden";
        ActivePane.SetStatusText(status, TimeSpan.FromSeconds(3));

        await Task.WhenAll(LeftPane.RefreshAsync(), RightPane.RefreshAsync());
    }

    public void ToggleHiddenFiles() => _ = ToggleHiddenFilesAsync();

    public async Task InitializeSettingsAsync(AppSettings settings)
    {
        await _settingsService.ValidatePathsAsync(settings);

        ShowHiddenFiles = settings.ShowHiddenFiles;
        LeftPane.ShowHiddenFiles = settings.ShowHiddenFiles;
        RightPane.ShowHiddenFiles = settings.ShowHiddenFiles;

        // Navigate both panes to their persisted or fallback paths
        LeftPane.Navigate(settings.LeftPanePath);
        RightPane.Navigate(settings.RightPanePath);

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

    public void PersistSettings(double left, double top, double width, double height, string windowState)
    {
        var settings = new AppSettings
        {
            WindowLeft = left,
            WindowTop = top,
            WindowWidth = width,
            WindowHeight = height,
            WindowState = windowState,
            LeftPanePath = string.IsNullOrEmpty(LeftPane.CurrentPath) ? @"C:\" : LeftPane.CurrentPath,
            RightPanePath = string.IsNullOrEmpty(RightPane.CurrentPath) ? @"C:\" : RightPane.CurrentPath,
            ShowHiddenFiles = ShowHiddenFiles
        };

        _settingsService.SaveSettings(settings);
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
