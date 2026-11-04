using FM.App.Models;
using FM.App.Services;
using FM.App.Services.QuickOpen;
using FM.App.Tests.Fakes;
using FM.App.ViewModels;
using FM.Core.FileSystem;
using FM.Core.Models;
using FM.Core.Services;
using FM.Core.Watching;

namespace FM.App.Tests;

public class CountingDirectoryService : IDirectoryService
{
    private readonly IDirectoryService _inner;
    public int DirectoryQueriesCount { get; private set; }

    public CountingDirectoryService(IDirectoryService inner)
    {
        _inner = inner;
    }

    public async IAsyncEnumerable<IReadOnlyList<FileEntry>> EnumerateBatchesAsync(string path, int batchSize = 200, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        DirectoryQueriesCount++;
        await foreach (var batch in _inner.EnumerateBatchesAsync(path, batchSize, ct).WithCancellation(ct))
        {
            yield return batch;
        }
    }
}

public class RecordingFolderWatcher : IFolderWatcher
{
    public string WatchedPath { get; }
    public bool EnableRaisingEvents { get; set; } = true;
    public bool IsDisposed { get; private set; }

#pragma warning disable CS0067
    public event EventHandler<FolderChangeEventArgs>? Changed;
    public event EventHandler<FolderWatcherErrorEventArgs>? Error;
    public event EventHandler? FolderDeleted;
#pragma warning restore CS0067

    public RecordingFolderWatcher(string path)
    {
        WatchedPath = path;
    }

    public void Dispose()
    {
        IsDisposed = true;
    }
}

public class RecordingFolderWatcherFactory : IFolderWatcherFactory
{
    public List<RecordingFolderWatcher> CreatedWatchers { get; } = new();

    public IFolderWatcher CreateWatcher(string folderPath)
    {
        var watcher = new RecordingFolderWatcher(folderPath);
        CreatedWatchers.Add(watcher);
        return watcher;
    }
}

public class M6TabsTests
{
    private readonly InMemoryFileSystem _fs;
    private readonly FileSystemBackedDirectoryService _innerDirService;
    private readonly FakeIconService _iconService;
    private readonly InMemorySettingsService _settingsService;

    public M6TabsTests()
    {
        _fs = new InMemoryFileSystem();
        _fs.CreateDirectory(@"C:\dir1");
        _fs.CreateDirectory(@"C:\dir2");
        _fs.CreateDirectory(@"C:\dir3");
        _fs.CreateFile(@"C:\dir1\file1.txt");
        _fs.CreateFile(@"C:\dir2\file2.txt");

        _innerDirService = new FileSystemBackedDirectoryService(_fs);
        _iconService = new FakeIconService();
        _settingsService = new InMemorySettingsService();
    }

    [Fact]
    public void Pane_InitializesWithSingleDefaultTab()
    {
        var pane = new FilePaneViewModel(
            _innerDirService,
            _iconService,
            _fs,
            settingsService: _settingsService);

        Assert.Single(pane.Tabs);
        Assert.NotNull(pane.ActiveTab);
        Assert.Same(pane.Tabs[0], pane.ActiveTab);
        Assert.True(pane.ActiveTab.IsActive);
    }

    [Fact]
    public void NewTab_CreatesTabAndActivatesIt()
    {
        var pane = new FilePaneViewModel(
            _innerDirService,
            _iconService,
            _fs,
            settingsService: _settingsService);

        pane.NewTab(@"C:\dir2");

        Assert.Equal(2, pane.Tabs.Count);
        Assert.NotNull(pane.ActiveTab);
        Assert.Equal(@"C:\dir2", pane.ActiveTab.CurrentPath);
        Assert.Equal("dir2", pane.ActiveTab.Title);
        Assert.True(pane.ActiveTab.IsActive);
        Assert.False(pane.Tabs[0].IsActive);
    }

    [Fact]
    public void CloseTab_ClosesActiveTabAndActivatesAdjacent()
    {
        var pane = new FilePaneViewModel(
            _innerDirService,
            _iconService,
            _fs,
            settingsService: _settingsService);

        pane.NewTab(@"C:\dir2");
        pane.NewTab(@"C:\dir3");

        Assert.Equal(3, pane.Tabs.Count);
        var activeBeforeClose = pane.ActiveTab;
        Assert.Equal(@"C:\dir3", activeBeforeClose?.CurrentPath);

        pane.CloseTab();

        Assert.Equal(2, pane.Tabs.Count);
        Assert.True(pane.CanReopenClosedTab);
        Assert.Equal(@"C:\dir2", pane.ActiveTab?.CurrentPath);
    }

    [Fact]
    public void ReopenClosedTab_RestoresLastClosedTab()
    {
        var pane = new FilePaneViewModel(
            _innerDirService,
            _iconService,
            _fs,
            settingsService: _settingsService);

        pane.NewTab(@"C:\dir2");
        pane.CloseTab();

        Assert.Single(pane.Tabs);
        Assert.True(pane.CanReopenClosedTab);

        pane.ReopenClosedTab();

        Assert.Equal(2, pane.Tabs.Count);
        Assert.Equal(@"C:\dir2", pane.ActiveTab?.CurrentPath);
    }

    [Fact]
    public void DuplicateTab_CreatesDuplicateWithIdenticalPathAndMode()
    {
        var pane = new FilePaneViewModel(
            _innerDirService,
            _iconService,
            _fs,
            settingsService: _settingsService);

        pane.Navigate(@"C:\dir1");
        pane.ViewMode = FilePaneViewMode.Icons;
        pane.CurrentSortColumn = FileSortColumn.Size;
        pane.IsSortAscending = false;

        pane.DuplicateTab();

        Assert.Equal(2, pane.Tabs.Count);
        var duplicate = pane.ActiveTab;
        Assert.NotNull(duplicate);
        Assert.Equal(@"C:\dir1", duplicate.CurrentPath);
        Assert.Equal(FilePaneViewMode.Icons, duplicate.ViewMode);
        Assert.Equal(FileSortColumn.Size, duplicate.CurrentSortColumn);
        Assert.False(duplicate.IsSortAscending);
    }

    [Fact]
    public void NextTab_And_PreviousTab_CycleCorrectly()
    {
        var pane = new FilePaneViewModel(
            _innerDirService,
            _iconService,
            _fs,
            settingsService: _settingsService);

        pane.NewTab(@"C:\dir1");
        pane.NewTab(@"C:\dir2");

        Assert.Equal(3, pane.Tabs.Count);
        Assert.Same(pane.Tabs[2], pane.ActiveTab);

        pane.NextTab();
        Assert.Same(pane.Tabs[0], pane.ActiveTab);

        pane.PreviousTab();
        Assert.Same(pane.Tabs[2], pane.ActiveTab);
    }

    [Fact]
    public void CloseOtherTabs_And_CloseTabsToRight_Work()
    {
        var pane = new FilePaneViewModel(
            _innerDirService,
            _iconService,
            _fs,
            settingsService: _settingsService);

        pane.NewTab(@"C:\dir1");
        pane.NewTab(@"C:\dir2");
        pane.NewTab(@"C:\dir3");

        Assert.Equal(4, pane.Tabs.Count);

        // Select tab 1 (dir1) and close tabs to right
        pane.ActiveTab = pane.Tabs[1];
        pane.CloseTabsToRight();

        Assert.Equal(2, pane.Tabs.Count);
        Assert.Same(pane.Tabs[1], pane.ActiveTab);

        // Close other tabs
        pane.CloseOtherTabs();
        Assert.Single(pane.Tabs);
        Assert.Equal(@"C:\dir1", pane.ActiveTab?.CurrentPath);
    }

    [Fact]
    public void MoveTabToPane_TransfersTabBetweenPanes()
    {
        var pane1 = new FilePaneViewModel(_innerDirService, _iconService, _fs, settingsService: _settingsService);
        var pane2 = new FilePaneViewModel(_innerDirService, _iconService, _fs, settingsService: _settingsService);

        pane1.NewTab(@"C:\dir2");
        Assert.Equal(2, pane1.Tabs.Count);
        Assert.Single(pane2.Tabs);

        var tabToMove = pane1.ActiveTab!;
        pane1.MoveTabToPane(tabToMove, pane2);

        Assert.Single(pane1.Tabs);
        Assert.Equal(2, pane2.Tabs.Count);
        Assert.Same(tabToMove, pane2.ActiveTab);
    }

    [Fact]
    public async Task SwitchTab_DoesNotReloadFolderFromDisk()
    {
        var countingService = new CountingDirectoryService(_innerDirService);
        var pane = new FilePaneViewModel(
            countingService,
            _iconService,
            _fs,
            settingsService: _settingsService);

        // Navigate tab 1 to C:\dir1
        await pane.NavigateAsync(@"C:\dir1");
        int queriesAfterTab1 = countingService.DirectoryQueriesCount;
        Assert.True(queriesAfterTab1 > 0);

        // Create tab 2 and navigate to C:\dir2
        pane.NewTab(@"C:\dir2");
        await pane.NavigateAsync(@"C:\dir2");
        int queriesAfterTab2 = countingService.DirectoryQueriesCount;
        Assert.True(queriesAfterTab2 > queriesAfterTab1);

        // Switch back to tab 1
        var tab1 = pane.Tabs[0];
        pane.ActiveTab = tab1;

        // Switching should NOT trigger any new directory queries!
        int queriesAfterSwitch = countingService.DirectoryQueriesCount;
        Assert.Equal(queriesAfterTab2, queriesAfterSwitch);
        Assert.Equal(@"C:\dir1", pane.CurrentPath);
    }

    [Fact]
    public async Task Watcher_ActiveOnlyOnVisibleTab()
    {
        var watcherFactory = new RecordingFolderWatcherFactory();
        var pane = new FilePaneViewModel(
            _innerDirService,
            _iconService,
            _fs,
            folderWatcherFactory: watcherFactory,
            settingsService: _settingsService);

        await pane.NavigateAsync(@"C:\dir1");
        Assert.NotEmpty(watcherFactory.CreatedWatchers);
        var watcher1 = watcherFactory.CreatedWatchers.Last();
        Assert.False(watcher1.IsDisposed);
        Assert.Equal(@"C:\dir1", watcher1.WatchedPath);

        // Switch to new tab at C:\dir2
        pane.NewTab(@"C:\dir2");
        await pane.NavigateAsync(@"C:\dir2");

        // Old watcher must be disposed! Inactive tab does not consume CPU / watcher
        Assert.True(watcher1.IsDisposed);

        var watcher2 = watcherFactory.CreatedWatchers.Last();
        Assert.False(watcher2.IsDisposed);
        Assert.Equal(@"C:\dir2", watcher2.WatchedPath);
    }

    [Fact]
    public void PersistSettings_PreservesAllFieldsAndSavesPaneTabs()
    {
        var mainVm = new MainViewModel(_innerDirService, _iconService, _settingsService, _fs);
        mainVm.Panes[0].NewTab(@"C:\dir2");
        mainVm.Panes[0].ViewMode = FilePaneViewMode.Icons;
        mainVm.Panes[0].CurrentSortColumn = FileSortColumn.Size;
        mainVm.Panes[0].IsSortAscending = false;

        mainVm.PersistSettings(10, 20, 1000, 600, "Normal");

        var saved = _settingsService.LoadSettings();
        Assert.NotNull(saved.PaneTabs);
        Assert.True(saved.PaneTabs.Count >= 2);
        Assert.Equal(2, saved.PaneTabs[0].Tabs.Count);
        Assert.Equal(@"C:\dir2", saved.PaneTabs[0].Tabs[1].Path);
        Assert.Equal("Icons", saved.PaneTabs[0].Tabs[1].ViewMode);
        Assert.Equal("Size", saved.PaneTabs[0].Tabs[1].SortColumn);
        Assert.False(saved.PaneTabs[0].Tabs[1].SortAscending);
    }

    [Fact]
    public async Task InitializeSettingsAsync_RestoresTabsPerPane()
    {
        var settings = new AppSettings
        {
            PaneTabs = new List<PaneTabSettings>
            {
                new PaneTabSettings
                {
                    Tabs = new List<TabStateSetting>
                    {
                        new TabStateSetting { Path = @"C:\dir1", Title = "dir1", ViewMode = "Details" },
                        new TabStateSetting { Path = @"C:\nonexistent", Title = "Ghost", ViewMode = "Icons" }
                    },
                    ActiveTabIndex = 1
                }
            }
        };

        var mainVm = new MainViewModel(_innerDirService, _iconService, _settingsService, _fs);
        await mainVm.InitializeSettingsAsync(settings);

        var pane = mainVm.Panes[0];
        Assert.Equal(2, pane.Tabs.Count);
        Assert.Equal(1, pane.Tabs.IndexOf(pane.ActiveTab!));
        Assert.Equal(@"C:\nonexistent", pane.ActiveTab?.CurrentPath);
        Assert.True(pane.ActiveTab?.IsUnavailable);
    }

    [Fact]
    public void QuickOpen_ListsAllOpenTabsAcrossPanes()
    {
        var mainVm = new MainViewModel(_innerDirService, _iconService, _settingsService, _fs);
        mainVm.Panes[0].NewTab(@"C:\dir2");

        var provider = new QuickOpenSourceProvider(mainVm, _fs, mainVm.FolderIndex);
        var items = provider.GetPlaces();

        var openTabs = items.Where(i => i.Badge == "Open tab" || i.Badge == "Open pane").ToList();
        Assert.True(openTabs.Count >= 3); // 2 tabs in left pane + 1 tab in right pane
        Assert.Contains(openTabs, t => t.TargetPath == @"C:\dir2");
    }

    [Fact]
    public void MainViewModel_OpenNewTab_OpensRealTabInActivePane()
    {
        var mainVm = new MainViewModel(_innerDirService, _iconService, _settingsService, _fs);
        int initialPaneCount = mainVm.Panes.Count;
        int initialTabCount = mainVm.ActivePane!.Tabs.Count;

        mainVm.OpenNewTab(@"C:\dir3");

        Assert.Equal(initialPaneCount, mainVm.Panes.Count); // Does NOT split pane!
        Assert.Equal(initialTabCount + 1, mainVm.ActivePane.Tabs.Count);
        Assert.Equal(@"C:\dir3", mainVm.ActivePane.ActiveTab?.CurrentPath);
    }

    [Fact]
    public void MainViewModel_CloseActiveTab_ClosesTabOrPane()
    {
        var mainVm = new MainViewModel(_innerDirService, _iconService, _settingsService, _fs);
        mainVm.OpenNewTab(@"C:\dir3");

        Assert.Equal(2, mainVm.ActivePane!.Tabs.Count);

        mainVm.CloseActiveTab();

        Assert.Single(mainVm.ActivePane.Tabs);
        Assert.Equal(2, mainVm.Panes.Count); // Pane remains open because it has 1 tab left
    }

    [Fact]
    public void Tabs_ReorderViaMove_MaintainsActiveTabAndOrder()
    {
        var pane = new FilePaneViewModel(
            _innerDirService,
            _iconService,
            _fs,
            settingsService: _settingsService);

        pane.NewTab(@"C:\dir1");
        pane.NewTab(@"C:\dir2");
        pane.NewTab(@"C:\dir3");

        // Tabs: [0: Home, 1: dir1, 2: dir2, 3: dir3 (active)]
        Assert.Equal(4, pane.Tabs.Count);
        var activeTab = pane.ActiveTab;
        Assert.Equal(@"C:\dir3", activeTab?.CurrentPath);

        // Move dir3 from index 3 to index 1 (between Home and dir1)
        int oldIdx = pane.Tabs.IndexOf(activeTab!);
        int newIdx = 1;
        pane.Tabs.Move(oldIdx, newIdx);

        Assert.Equal(4, pane.Tabs.Count);
        Assert.Equal(@"C:\dir3", pane.Tabs[1].CurrentPath);
        Assert.Equal(@"C:\dir1", pane.Tabs[2].CurrentPath);
        Assert.Equal(@"C:\dir2", pane.Tabs[3].CurrentPath);
        Assert.Same(activeTab, pane.ActiveTab);
    }
}
