using System.IO;
using FM.App.Models;
using FM.App.Tests.Fakes;
using FM.App.ViewModels;
using FM.Core.FileSystem;
using FM.Core.Models;
using FM.Core.Watching;
using Xunit;

namespace FM.App.Tests;

public class LiveAutoRefreshTests
{
    private const string TestRoot = @"C:\TestFolder";
    private readonly FakeDirectoryService _dirService;
    private readonly FakeIconService _iconService;
    private readonly InMemoryFileSystem _fileSystem;
    private readonly FakeFolderWatcherFactory _watcherFactory;
    private readonly TestTimeProvider _timeProvider;

    public LiveAutoRefreshTests()
    {
        _dirService = new FakeDirectoryService();
        _iconService = new FakeIconService();
        _fileSystem = new InMemoryFileSystem();
        _watcherFactory = new FakeFolderWatcherFactory();
        _timeProvider = new TestTimeProvider();

        _fileSystem.CreateDirectory(TestRoot);
    }

    private FilePaneViewModel CreatePane()
    {
        var pane = new FilePaneViewModel(
            _dirService,
            _iconService,
            _fileSystem,
            folderWatcherFactory: _watcherFactory,
            timeProvider: _timeProvider);
        pane.TimeProvider = _timeProvider;
        return pane;
    }

    [Fact]
    public async Task CreatedFile_AppearsIncrementallyWithoutFullReload_AndPreservesSortOrder()
    {
        string subDir = Path.Combine(TestRoot, "B_Folder");
        string file1 = Path.Combine(TestRoot, "A_File.txt");
        string file2 = Path.Combine(TestRoot, "C_File.txt");

        _fileSystem.CreateDirectory(subDir);
        _fileSystem.CreateFile(file1, new byte[] { 1 });
        _fileSystem.CreateFile(file2, new byte[] { 2 });

        _dirService.AddDirectory(TestRoot, new[]
        {
            new FileEntry("B_Folder", subDir, true, 0, DateTime.Now),
            new FileEntry("A_File.txt", file1, false, 1, DateTime.Now),
            new FileEntry("C_File.txt", file2, false, 2, DateTime.Now)
        });

        var pane = CreatePane();
        await pane.NavigateInternalAsync(TestRoot, NavigationPhaseType.New);

        var watcher = _watcherFactory.LastCreatedWatcher;
        Assert.NotNull(watcher);

        // Simulate creation of a new folder "A_Folder" which should be placed at the very top (folders first, sorted by name)
        string newFolder = Path.Combine(TestRoot, "A_Folder");
        _fileSystem.CreateDirectory(newFolder);

        watcher.TriggerCreated(newFolder);

        // Advance past the 150ms debounce window
        _timeProvider.Advance(TimeSpan.FromMilliseconds(200));
        await pane.WaitForPendingWatcherUpdatesAsync();

        // The item must be inserted incrementally into Items
        Assert.Equal(4, pane.Items.Count);
        Assert.Equal("A_Folder", pane.Items[0].Name);
        Assert.True(pane.Items[0].IsDirectory);
        Assert.Equal("B_Folder", pane.Items[1].Name);
        Assert.True(pane.Items[1].IsDirectory);
    }

    [Fact]
    public async Task DeletedFile_IsRemovedIncrementally_AndUpdatesSelection()
    {
        string file1 = Path.Combine(TestRoot, "file1.txt");
        string file2 = Path.Combine(TestRoot, "file2.txt");

        _fileSystem.CreateFile(file1, new byte[] { 1 });
        _fileSystem.CreateFile(file2, new byte[] { 2 });

        _dirService.AddDirectory(TestRoot, new[]
        {
            new FileEntry("file1.txt", file1, false, 1, DateTime.Now),
            new FileEntry("file2.txt", file2, false, 2, DateTime.Now)
        });

        var pane = CreatePane();
        await pane.NavigateInternalAsync(TestRoot, NavigationPhaseType.New);

        // Select file1
        pane.UpdateSelectedItems(new[] { pane.Items[0] });
        Assert.Single(pane.SelectedItems);

        var watcher = _watcherFactory.LastCreatedWatcher!;
        _fileSystem.DeleteFile(file1);
        watcher.TriggerDeleted(file1);

        _timeProvider.Advance(TimeSpan.FromMilliseconds(200));
        await pane.WaitForPendingWatcherUpdatesAsync();

        Assert.Single(pane.Items);
        Assert.Equal("file2.txt", pane.Items[0].Name);
        Assert.Empty(pane.SelectedItems);
    }

    [Fact]
    public async Task RenamedFile_UpdatesPathAndName_PreservingSelection()
    {
        string oldPath = Path.Combine(TestRoot, "original.txt");
        string newPath = Path.Combine(TestRoot, "renamed.txt");

        _fileSystem.CreateFile(oldPath, new byte[] { 50 });
        _dirService.AddDirectory(TestRoot, new[]
        {
            new FileEntry("original.txt", oldPath, false, 50, DateTime.Now)
        });

        var pane = CreatePane();
        await pane.NavigateInternalAsync(TestRoot, NavigationPhaseType.New);

        pane.UpdateSelectedItems(new[] { pane.Items[0] });
        Assert.Equal(oldPath, pane.SelectedItem?.FullPath);

        var watcher = _watcherFactory.LastCreatedWatcher!;
        _fileSystem.MoveFile(oldPath, newPath);
        watcher.TriggerRenamed(newPath, oldPath);

        _timeProvider.Advance(TimeSpan.FromMilliseconds(200));
        await pane.WaitForPendingWatcherUpdatesAsync();

        Assert.Single(pane.Items);
        Assert.Equal("renamed.txt", pane.Items[0].Name);
        Assert.Equal(newPath, pane.Items[0].FullPath);
        Assert.Single(pane.SelectedItems);
        Assert.Equal(newPath, pane.SelectedItem?.FullPath);
    }

    [Fact]
    public async Task ModifiedFile_UpdatesSizeAndTimestamp_InPlace()
    {
        string filePath = Path.Combine(TestRoot, "document.txt");
        _fileSystem.CreateFile(filePath, new byte[100]);

        var originalTime = new DateTime(2026, 1, 1, 12, 0, 0);
        _dirService.AddDirectory(TestRoot, new[]
        {
            new FileEntry("document.txt", filePath, false, 100, originalTime)
        });

        var pane = CreatePane();
        await pane.NavigateInternalAsync(TestRoot, NavigationPhaseType.New);

        var originalVm = pane.Items[0];
        Assert.Equal("100 B", originalVm.FormattedSize);

        // Update file size and timestamp on filesystem
        var newTime = new DateTime(2026, 11, 2, 16, 0, 0);
        _fileSystem.SetModified(filePath, newTime);

        // Simulate appending 400 bytes (now 500 bytes)
        using (var stream = _fileSystem.OpenAppend(filePath))
        {
            stream.Write(new byte[400]);
        }

        var watcher = _watcherFactory.LastCreatedWatcher!;
        watcher.TriggerChanged(filePath);

        _timeProvider.Advance(TimeSpan.FromMilliseconds(200));
        await pane.WaitForPendingWatcherUpdatesAsync();

        // The exact same ViewModel instance should be updated in place!
        Assert.Same(originalVm, pane.Items[0]);
        Assert.Equal("500 B", originalVm.FormattedSize);
        Assert.Equal(500, originalVm.Size);
    }

    [Fact]
    public async Task ActiveFilter_ExcludesNonMatchingCreations()
    {
        _dirService.AddDirectory(TestRoot, Array.Empty<FileEntry>());

        var pane = CreatePane();
        await pane.NavigateInternalAsync(TestRoot, NavigationPhaseType.New);

        pane.FilterText = "report";
        var watcher = _watcherFactory.LastCreatedWatcher!;

        string nonMatch = Path.Combine(TestRoot, "notes.txt");
        string matching = Path.Combine(TestRoot, "quarterly_report.pdf");

        _fileSystem.CreateFile(nonMatch, new byte[10]);
        _fileSystem.CreateFile(matching, new byte[20]);

        watcher.TriggerCreated(nonMatch);
        watcher.TriggerCreated(matching);

        _timeProvider.Advance(TimeSpan.FromMilliseconds(200));
        await pane.WaitForPendingWatcherUpdatesAsync();

        // Items only contains the matching file
        Assert.Single(pane.Items);
        Assert.Equal("quarterly_report.pdf", pane.Items[0].Name);

        // But UnfilteredItems tracks both
        Assert.Equal(2, pane.UnfilteredItems.Count);
    }

    [Fact]
    public async Task InProgressInlineRename_IsNotDisrupted()
    {
        string filePath = Path.Combine(TestRoot, "active_edit.txt");
        _fileSystem.CreateFile(filePath, new byte[10]);
        _dirService.AddDirectory(TestRoot, new[]
        {
            new FileEntry("active_edit.txt", filePath, false, 10, DateTime.Now)
        });

        var pane = CreatePane();
        await pane.NavigateInternalAsync(TestRoot, NavigationPhaseType.New);

        var item = pane.Items[0];
        item.IsRenaming = true;

        var watcher = _watcherFactory.LastCreatedWatcher!;
        watcher.TriggerRenamed(Path.Combine(TestRoot, "new_name.txt"), filePath);

        _timeProvider.Advance(TimeSpan.FromMilliseconds(200));
        await pane.WaitForPendingWatcherUpdatesAsync();

        // Item undergoing rename was preserved without disrupting user
        Assert.Same(item, pane.Items[0]);
    }

    [Fact]
    public async Task BufferOverflow_FallsBackToSingleFullRefresh()
    {
        _dirService.AddDirectory(TestRoot, new[]
        {
            new FileEntry("initial.txt", Path.Combine(TestRoot, "initial.txt"), false, 10, DateTime.Now)
        });

        var pane = CreatePane();
        await pane.NavigateInternalAsync(TestRoot, NavigationPhaseType.New);
        Assert.Single(pane.Items);

        // Update directory service with updated listing (simulating external changes during overflow)
        _dirService.AddDirectory(TestRoot, new[]
        {
            new FileEntry("initial.txt", Path.Combine(TestRoot, "initial.txt"), false, 10, DateTime.Now),
            new FileEntry("overflow_recovery.txt", Path.Combine(TestRoot, "overflow_recovery.txt"), false, 20, DateTime.Now)
        });

        var watcher = _watcherFactory.LastCreatedWatcher!;
        watcher.TriggerError(new InternalBufferOverflowException("Buffer full"), isBufferOverflow: true);

        // Give async dispatch time to run
        await Task.Delay(100);

        Assert.Equal(2, pane.Items.Count);
        Assert.Contains(pane.Items, i => i.Name == "overflow_recovery.txt");
    }

    [Fact]
    public async Task WatchedFolderDeleted_RedirectsToNearestExistingParent_AndShowsToast()
    {
        string childFolder = Path.Combine(TestRoot, "ChildFolder");
        _fileSystem.CreateDirectory(childFolder);

        _dirService.AddDirectory(childFolder, Array.Empty<FileEntry>());
        _dirService.AddDirectory(TestRoot, Array.Empty<FileEntry>());

        var pane = CreatePane();
        await pane.NavigateInternalAsync(childFolder, NavigationPhaseType.New);
        Assert.Equal(childFolder, pane.CurrentPath);

        // Delete childFolder and trigger deletion
        _fileSystem.DeleteDirectory(childFolder);

        var watcher = _watcherFactory.LastCreatedWatcher!;
        watcher.TriggerFolderDeleted();

        await Task.Delay(100);

        // Must redirect to TestRoot (nearest existing parent)
        Assert.Equal(TestRoot, pane.CurrentPath);
        Assert.Contains("deleted or moved", pane.SelectionStatusText);
    }

    [Fact]
    public async Task Navigation_DisposesOldWatcher_AndCreatesNewWatcher()
    {
        string dirA = Path.Combine(TestRoot, "DirA");
        string dirB = Path.Combine(TestRoot, "DirB");
        _fileSystem.CreateDirectory(dirA);
        _fileSystem.CreateDirectory(dirB);
        _dirService.AddDirectory(dirA, Array.Empty<FileEntry>());
        _dirService.AddDirectory(dirB, Array.Empty<FileEntry>());

        var pane = CreatePane();
        await pane.NavigateInternalAsync(dirA, NavigationPhaseType.New);

        var watcherA = _watcherFactory.LastCreatedWatcher as FakeFolderWatcher;
        Assert.NotNull(watcherA);
        Assert.Equal(dirA, watcherA.WatchedPath);
        Assert.False(watcherA.IsDisposed);

        // Navigate to dirB
        await pane.NavigateInternalAsync(dirB, NavigationPhaseType.New);

        // watcherA must be disposed immediately
        Assert.True(watcherA.IsDisposed);

        var watcherB = _watcherFactory.LastCreatedWatcher as FakeFolderWatcher;
        Assert.NotNull(watcherB);
        Assert.Equal(dirB, watcherB.WatchedPath);
        Assert.False(watcherB.IsDisposed);
    }

    [Fact]
    public async Task ClosingPane_DisposesWatcher_NoLeaks()
    {
        var settingsService = new FakeSettingsService();
        var mainVm = new MainViewModel(
            _dirService,
            _iconService,
            settingsService,
            _fileSystem,
            folderWatcherFactory: _watcherFactory,
            timeProvider: _timeProvider);

        Assert.Equal(2, mainVm.Panes.Count);

        var leftPane = mainVm.Panes[0];
        _dirService.AddDirectory(TestRoot, Array.Empty<FileEntry>());
        await leftPane.NavigateInternalAsync(TestRoot, NavigationPhaseType.New);

        var watcher = leftPane.ActiveWatcher as FakeFolderWatcher;
        Assert.NotNull(watcher);
        Assert.False(watcher.IsDisposed);

        // Close the pane
        mainVm.ClosePane(leftPane);

        // Watcher must be disposed
        Assert.True(watcher.IsDisposed);
    }

    [Fact]
    public async Task BurstChanges_CoalescesIntoSingleBatch()
    {
        _dirService.AddDirectory(TestRoot, Array.Empty<FileEntry>());

        var pane = CreatePane();
        await pane.NavigateInternalAsync(TestRoot, NavigationPhaseType.New);

        var watcher = _watcherFactory.LastCreatedWatcher!;

        // Fire 1,000 rapid creation events for 200 files (5 events per file)
        for (int i = 0; i < 200; i++)
        {
            string p = Path.Combine(TestRoot, $"burst_{i:D4}.txt");
            _fileSystem.CreateFile(p, new byte[10]);

            watcher.TriggerCreated(p);
            watcher.TriggerChanged(p);
            watcher.TriggerChanged(p);
            watcher.TriggerChanged(p);
            watcher.TriggerChanged(p);
        }

        // Advance debounce timer once
        _timeProvider.Advance(TimeSpan.FromMilliseconds(200));
        await pane.WaitForPendingWatcherUpdatesAsync();

        // Exactly 200 files present, processed in a single coalesced batch
        Assert.Equal(200, pane.Items.Count);
    }

    [Fact]
    public async Task TransferScope_FlushesCoherentlyWithoutStorm()
    {
        _dirService.AddDirectory(TestRoot, Array.Empty<FileEntry>());

        var pane = CreatePane();
        await pane.NavigateInternalAsync(TestRoot, NavigationPhaseType.New);

        var watcher = _watcherFactory.LastCreatedWatcher!;

        // Enter transfer scope (simulating app's own transfer in progress)
        pane.BeginTransferScope();

        for (int i = 0; i < 5; i++)
        {
            string p = Path.Combine(TestRoot, $"transferred_{i}.dat");
            _fileSystem.CreateFile(p, new byte[50]);
            watcher.TriggerCreated(p);
        }

        // End transfer scope flushes the batch
        pane.EndTransferScope();
        _timeProvider.Advance(TimeSpan.FromMilliseconds(100));
        await pane.WaitForPendingWatcherUpdatesAsync();

        Assert.Equal(5, pane.Items.Count);
    }
}
