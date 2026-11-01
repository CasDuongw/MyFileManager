using System.IO;
using FM.App.Tests.Fakes;
using FM.App.ViewModels;
using FM.Core.Models;

namespace FM.App.Tests;

/// <summary>
/// Unit tests for FilePaneViewModel.
/// Strictly touches NO physical disk; all file entries and errors are simulated in-memory via FakeDirectoryService.
/// </summary>
public class FilePaneViewModelTests
{
    private const string MockRoot = @"C:\MockTest";
    private readonly FakeDirectoryService _fakeDirService;
    private readonly FakeIconService _fakeIconService;
    private readonly FilePaneViewModel _viewModel;

    public FilePaneViewModelTests()
    {
        _fakeDirService = new FakeDirectoryService();
        _fakeIconService = new FakeIconService();
        _viewModel = new FilePaneViewModel(_fakeDirService, _fakeIconService);
    }

    [Fact]
    public async Task RapidSuccessiveNavigations_OnlyLastResultShown()
    {
        string dirA = Path.Combine(MockRoot, "DirA");
        string dirB = Path.Combine(MockRoot, "DirB");

        _fakeDirService.AddDirectory(dirA, new[]
        {
            new FileEntry("fileA1.txt", Path.Combine(dirA, "fileA1.txt"), false, 100, DateTime.Now),
            new FileEntry("fileA2.txt", Path.Combine(dirA, "fileA2.txt"), false, 200, DateTime.Now)
        });

        _fakeDirService.AddDirectory(dirB, new[]
        {
            new FileEntry("fileB1.txt", Path.Combine(dirB, "fileB1.txt"), false, 300, DateTime.Now),
            new FileEntry("fileB2.txt", Path.Combine(dirB, "fileB2.txt"), false, 400, DateTime.Now)
        });

        _fakeDirService.SetDelay(TimeSpan.FromMilliseconds(50));

        // Rapid navigations
        var taskA = _viewModel.NavigateInternalAsync(dirA, NavigationPhaseType.New);
        var taskB = _viewModel.NavigateInternalAsync(dirB, NavigationPhaseType.New);

        await Task.WhenAll(taskA, taskB);

        Assert.Equal(dirB, _viewModel.CurrentPath);
        Assert.Equal(2, _viewModel.Items.Count);
        Assert.All(_viewModel.Items, item => Assert.StartsWith("fileB", item.Name));
    }

    [Fact]
    public async Task ErrorState_DirectoryNotFound_ShowsErrorMessage()
    {
        string missingPath = Path.Combine(MockRoot, "NoSuchDirectory");

        await _viewModel.NavigateInternalAsync(missingPath, NavigationPhaseType.New);

        Assert.True(_viewModel.HasError);
        Assert.Contains("Directory not found", _viewModel.ErrorMessage);
        Assert.Empty(_viewModel.Items);
    }

    [Fact]
    public async Task ErrorState_AccessDenied_ShowsErrorMessage()
    {
        string deniedPath = Path.Combine(MockRoot, "DeniedDir");

        _fakeDirService.SetError(deniedPath, new UnauthorizedAccessException("Forbidden"));

        await _viewModel.NavigateInternalAsync(deniedPath, NavigationPhaseType.New);

        Assert.True(_viewModel.HasError);
        Assert.Contains("Access denied", _viewModel.ErrorMessage);
        Assert.Empty(_viewModel.Items);
    }

    [Fact]
    public async Task HistoryConsistency_BackToDeletedFolder_MaintainsHistory()
    {
        string dir1 = Path.Combine(MockRoot, "Dir1");
        string dir2 = Path.Combine(MockRoot, "Dir2");

        _fakeDirService.AddDirectory(dir1, Array.Empty<FileEntry>());
        _fakeDirService.AddDirectory(dir2, Array.Empty<FileEntry>());

        // Navigate Dir1 -> Dir2
        await _viewModel.NavigateInternalAsync(dir1, NavigationPhaseType.New);
        await _viewModel.NavigateInternalAsync(dir2, NavigationPhaseType.New);

        Assert.Equal(dir2, _viewModel.CurrentPath);
        Assert.True(_viewModel.CanGoBack);

        // Delete Dir1 from fake service (simulating external deletion)
        _fakeDirService.RemoveDirectory(dir1);

        // Attempt GoBack and await command execution deterministically (no Task.Delay)
        await _viewModel.GoBackCommand.ExecuteAsync(null);

        // Dir1 was deleted, so error is displayed, but CurrentPath remains Dir2 and CanGoBack remains true
        Assert.True(_viewModel.HasError);
        Assert.Contains("Directory not found", _viewModel.ErrorMessage);
        Assert.Equal(dir2, _viewModel.CurrentPath);
        Assert.True(_viewModel.CanGoBack);
    }

    [Fact]
    public async Task HistoryConsistency_ForwardToDeletedFolder_MaintainsHistory()
    {
        string dir1 = Path.Combine(MockRoot, "DirFwd1");
        string dir2 = Path.Combine(MockRoot, "DirFwd2");

        _fakeDirService.AddDirectory(dir1, Array.Empty<FileEntry>());
        _fakeDirService.AddDirectory(dir2, Array.Empty<FileEntry>());

        // Navigate Dir1 -> Dir2
        await _viewModel.NavigateInternalAsync(dir1, NavigationPhaseType.New);
        await _viewModel.NavigateInternalAsync(dir2, NavigationPhaseType.New);

        // GoBack to Dir1
        await _viewModel.GoBackCommand.ExecuteAsync(null);
        Assert.Equal(dir1, _viewModel.CurrentPath);
        Assert.True(_viewModel.CanGoForward);

        // Delete Dir2 from fake service
        _fakeDirService.RemoveDirectory(dir2);

        // Attempt GoForward and await execution
        await _viewModel.GoForwardCommand.ExecuteAsync(null);

        // Dir2 was deleted: error is displayed, CurrentPath remains Dir1, CanGoForward remains true
        Assert.True(_viewModel.HasError);
        Assert.Contains("Directory not found", _viewModel.ErrorMessage);
        Assert.Equal(dir1, _viewModel.CurrentPath);
        Assert.True(_viewModel.CanGoForward);
    }

    [Fact]
    public async Task Sort_PreservesSelection()
    {
        string dir = Path.Combine(MockRoot, "SortDir");

        _fakeDirService.AddDirectory(dir, new[]
        {
            new FileEntry("zebra.txt", Path.Combine(dir, "zebra.txt"), false, 1000, DateTime.Now),
            new FileEntry("apple.txt", Path.Combine(dir, "apple.txt"), false, 10, DateTime.Now),
            new FileEntry("banana.txt", Path.Combine(dir, "banana.txt"), false, 500, DateTime.Now)
        });

        await _viewModel.NavigateInternalAsync(dir, NavigationPhaseType.New);

        // Select apple.txt
        var appleItem = _viewModel.Items.First(i => i.Name == "apple.txt");
        _viewModel.SelectedItem = appleItem;

        // Sort by Size
        await _viewModel.Sort("Size");

        // Verify apple.txt is still selected
        Assert.NotNull(_viewModel.SelectedItem);
        Assert.Equal("apple.txt", _viewModel.SelectedItem.Name);
    }

    [Fact]
    public async Task Sort_PreservesAllSelectedItems_ByFullPath()
    {
        string dir = Path.Combine(MockRoot, "MultiSortDir");

        _fakeDirService.AddDirectory(dir, new[]
        {
            new FileEntry("zebra.txt", Path.Combine(dir, "zebra.txt"), false, 1000, DateTime.Now),
            new FileEntry("apple.txt", Path.Combine(dir, "apple.txt"), false, 10, DateTime.Now),
            new FileEntry("banana.txt", Path.Combine(dir, "banana.txt"), false, 500, DateTime.Now),
            new FileEntry("cherry.txt", Path.Combine(dir, "cherry.txt"), false, 250, DateTime.Now)
        });

        await _viewModel.NavigateInternalAsync(dir, NavigationPhaseType.New);

        // Select apple.txt and banana.txt
        var selected = _viewModel.Items.Where(i => i.Name == "apple.txt" || i.Name == "banana.txt").ToList();
        _viewModel.UpdateSelectedItems(selected);

        Assert.Equal(2, _viewModel.SelectedItems.Count);

        // Sort by Size
        await _viewModel.Sort("Size");

        // Verify BOTH items are still selected after sort
        Assert.Equal(2, _viewModel.SelectedItems.Count);
        Assert.Contains(_viewModel.SelectedItems, i => i.Name == "apple.txt");
        Assert.Contains(_viewModel.SelectedItems, i => i.Name == "banana.txt");
    }

    [Fact]
    public async Task Refresh_PreservesAllSelectedItems_ByFullPath()
    {
        string dir = Path.Combine(MockRoot, "MultiRefreshDir");

        _fakeDirService.AddDirectory(dir, new[]
        {
            new FileEntry("file1.txt", Path.Combine(dir, "file1.txt"), false, 100, DateTime.Now),
            new FileEntry("file2.txt", Path.Combine(dir, "file2.txt"), false, 200, DateTime.Now),
            new FileEntry("file3.txt", Path.Combine(dir, "file3.txt"), false, 300, DateTime.Now)
        });

        await _viewModel.NavigateInternalAsync(dir, NavigationPhaseType.New);

        // Select file1 and file3
        var selected = _viewModel.Items.Where(i => i.Name == "file1.txt" || i.Name == "file3.txt").ToList();
        _viewModel.UpdateSelectedItems(selected);

        Assert.Equal(2, _viewModel.SelectedItems.Count);

        // Refresh
        await _viewModel.RefreshCommand.ExecuteAsync(null);

        // Verify BOTH items remain selected after refresh
        Assert.Equal(2, _viewModel.SelectedItems.Count);
        Assert.Contains(_viewModel.SelectedItems, i => i.Name == "file1.txt");
        Assert.Contains(_viewModel.SelectedItems, i => i.Name == "file3.txt");
    }

    [Fact]
    public async Task MultiSelect_SyncUpdatesSelectionStatusText()
    {
        string dir = Path.Combine(MockRoot, "MultiSelectDir");

        _fakeDirService.AddDirectory(dir, new[]
        {
            new FileEntry("file1.txt", Path.Combine(dir, "file1.txt"), false, 1024, DateTime.Now),
            new FileEntry("file2.txt", Path.Combine(dir, "file2.txt"), false, 2048, DateTime.Now),
            new FileEntry("folder1", Path.Combine(dir, "folder1"), true, 0, DateTime.Now)
        });

        await _viewModel.NavigateInternalAsync(dir, NavigationPhaseType.New);

        // Initial: 0 selected
        Assert.Equal("3 items", _viewModel.SelectionStatusText);

        // Select file1 and file2
        var selected = _viewModel.Items.Where(i => !i.IsDirectory).ToList();
        _viewModel.UpdateSelectedItems(selected);

        Assert.Equal(2, _viewModel.SelectedItems.Count);
        Assert.Contains("2 of 3 selected", _viewModel.SelectionStatusText);
        Assert.Contains("3 KB", _viewModel.SelectionStatusText);
    }

    [Fact]
    public async Task Navigate_RaisesPropertyChangedForSelectionStatusText()
    {
        string dir = Path.Combine(MockRoot, "StatusTextDir");

        _fakeDirService.AddDirectory(dir, new[]
        {
            new FileEntry("file1.txt", Path.Combine(dir, "file1.txt"), false, 1024, DateTime.Now),
            new FileEntry("file2.txt", Path.Combine(dir, "file2.txt"), false, 2048, DateTime.Now),
            new FileEntry("file3.txt", Path.Combine(dir, "file3.txt"), false, 4096, DateTime.Now)
        });

        var propertyChangedEvents = new List<string>();
        _viewModel.PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName == nameof(FilePaneViewModel.SelectionStatusText))
            {
                propertyChangedEvents.Add(_viewModel.SelectionStatusText);
            }
        };

        await _viewModel.NavigateInternalAsync(dir, NavigationPhaseType.New);

        // Must have received PropertyChanged notifications, with final value reflecting the loaded count
        Assert.NotEmpty(propertyChangedEvents);
        Assert.Equal("3 items", propertyChangedEvents.Last());
    }

    [Fact]
    public async Task Cancellation_WhenNavigatingAwayMidLoad()
    {
        string dirSlow = Path.Combine(MockRoot, "DirSlow");
        string dirFast = Path.Combine(MockRoot, "DirFast");

        _fakeDirService.AddDirectory(dirSlow, new[]
        {
            new FileEntry("slow.txt", Path.Combine(dirSlow, "slow.txt"), false, 100, DateTime.Now)
        });
        _fakeDirService.AddDirectory(dirFast, new[]
        {
            new FileEntry("fast.txt", Path.Combine(dirFast, "fast.txt"), false, 200, DateTime.Now)
        });

        // Set slow directory delay
        _fakeDirService.SetDelay(TimeSpan.FromMilliseconds(200));
        var slowTask = _viewModel.NavigateInternalAsync(dirSlow, NavigationPhaseType.New);

        Assert.True(_viewModel.IsLoading);

        // Switch to fast directory immediately
        _fakeDirService.SetDelay(TimeSpan.Zero);
        var fastTask = _viewModel.NavigateInternalAsync(dirFast, NavigationPhaseType.New);

        await Task.WhenAll(slowTask, fastTask);

        Assert.False(_viewModel.IsLoading);
        Assert.Equal(dirFast, _viewModel.CurrentPath);
        Assert.Single(_viewModel.Items);
        Assert.Equal("fast.txt", _viewModel.Items[0].Name);
    }

    [Fact]
    public async Task StaleSort_WhenNavigatingToNewerFolder_DoesNotOverwriteNewerFolder()
    {
        string dirA = Path.Combine(MockRoot, "DirA_Large");
        string dirB = Path.Combine(MockRoot, "DirB_Small");

        var entriesA = Enumerable.Range(1, 501)
            .Select(i => new FileEntry($"fileA_{i:D4}.txt", Path.Combine(dirA, $"fileA_{i:D4}.txt"), false, i, DateTime.Now))
            .ToArray();

        var entriesB = new[]
        {
            new FileEntry("fileB_1.txt", Path.Combine(dirB, "fileB_1.txt"), false, 10, DateTime.Now),
            new FileEntry("fileB_2.txt", Path.Combine(dirB, "fileB_2.txt"), false, 20, DateTime.Now)
        };

        _fakeDirService.AddDirectory(dirA, entriesA);
        _fakeDirService.AddDirectory(dirB, entriesB);

        var sortStartedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSortTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        _viewModel.SortGate = async () =>
        {
            sortStartedTcs.TrySetResult(true);
            await releaseSortTcs.Task;
        };

        // Start navigation to A (501 items, triggers off-thread sort)
        var navATask = _viewModel.NavigateInternalAsync(dirA, NavigationPhaseType.New);

        // Wait until navigation to A reaches the sort gate
        await sortStartedTcs.Task;

        // Reset SortGate so B completes without blocking
        _viewModel.SortGate = null;

        // Navigate to B and await its completion
        await _viewModel.NavigateInternalAsync(dirB, NavigationPhaseType.New);

        // Release the gated sort from navigation A
        releaseSortTcs.TrySetResult(true);
        await navATask;

        // Verify that B's items and CurrentPath remain intact and were not overwritten by A's stale sort
        Assert.Equal(dirB, _viewModel.CurrentPath);
        Assert.Equal(2, _viewModel.Items.Count);
        Assert.All(_viewModel.Items, item => Assert.StartsWith("fileB", item.Name));
    }

    [Fact]
    public void GetSubfolders_WithInMemoryFileSystem_ReturnsSortedSubfolders()
    {
        var memFs = new FM.Core.FileSystem.InMemoryFileSystem();
        string root = @"C:\Showcase";
        memFs.CreateDirectory(root);
        memFs.CreateDirectory(Path.Combine(root, "Projects 2026"));
        memFs.CreateDirectory(Path.Combine(root, "Clients"));
        memFs.CreateDirectory(Path.Combine(root, "Apps & Packages"));

        var vm = new FilePaneViewModel(_fakeDirService, _fakeIconService, fileSystem: memFs);
        var subfolders = vm.GetSubfolders(root);

        Assert.Equal(3, subfolders.Count);
        Assert.Equal("Apps & Packages", subfolders[0].Name);
        Assert.Equal("Clients", subfolders[1].Name);
        Assert.Equal("Projects 2026", subfolders[2].Name);
    }

    [Fact]
    public void GetSubfolders_WhenPathDoesNotExist_ReturnsEmptyList()
    {
        var subfolders = _viewModel.GetSubfolders(@"C:\NonExistent\Folder_98765");
        Assert.NotNull(subfolders);
        Assert.Empty(subfolders);
    }

    [Fact]
    public void OpenSegmentInNewTab_InvokesCallbackWithExpectedPath()
    {
        string? opened = null;
        _viewModel.OnOpenInNewTabRequested = path => opened = path;

        _viewModel.OpenSegmentInNewTabCommand.Execute(@"C:\Showcase\Clients");

        Assert.Equal(@"C:\Showcase\Clients", opened);
    }

    [Fact]
    public async Task Breadcrumbs_ClickingAncestorSegment_NavigatesDirectlyToIt()
    {
        string baseDir = Path.Combine(MockRoot, "Showcase");
        string projectsDir = Path.Combine(baseDir, "Projects 2026");
        string websiteDir = Path.Combine(projectsDir, "Website Redesign");

        _fakeDirService.AddDirectory(websiteDir, Array.Empty<FileEntry>());
        _fakeDirService.AddDirectory(projectsDir, Array.Empty<FileEntry>());

        await _viewModel.NavigateInternalAsync(websiteDir, NavigationPhaseType.New);

        var crumbs = _viewModel.Breadcrumbs;
        Assert.NotEmpty(crumbs);

        // Find the 'Projects 2026' crumb
        var projectCrumb = crumbs.FirstOrDefault(c => c.Name == "Projects 2026");
        Assert.NotNull(projectCrumb);

        // Click crumb (navigate to its full path)
        await _viewModel.NavigateInternalAsync(projectCrumb.FullPath, NavigationPhaseType.New);

        Assert.Equal(projectsDir, _viewModel.CurrentPath);
    }
}
