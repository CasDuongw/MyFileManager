using System.IO;
using FM.App.Tests.Fakes;
using FM.App.ViewModels;
using FM.Core.Models;

namespace FM.App.Tests;

public class FilePaneViewModelTests : IDisposable
{
    private readonly string _testRoot;
    private readonly FakeDirectoryService _fakeDirService;
    private readonly FakeIconService _fakeIconService;
    private readonly FilePaneViewModel _viewModel;

    public FilePaneViewModelTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"FM_AppTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);

        _fakeDirService = new FakeDirectoryService();
        _fakeIconService = new FakeIconService();
        _viewModel = new FilePaneViewModel(_fakeDirService, _fakeIconService);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, true);
            }
        }
        catch
        {
            // Ignore cleanup failure
        }
    }

    [Fact]
    public async Task RapidSuccessiveNavigations_OnlyLastResultShown()
    {
        string dirA = Path.Combine(_testRoot, "DirA");
        string dirB = Path.Combine(_testRoot, "DirB");
        Directory.CreateDirectory(dirA);
        Directory.CreateDirectory(dirB);

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
        string missingPath = Path.Combine(_testRoot, "NoSuchDirectory");

        await _viewModel.NavigateInternalAsync(missingPath, NavigationPhaseType.New);

        Assert.True(_viewModel.HasError);
        Assert.Contains("Directory not found", _viewModel.ErrorMessage);
        Assert.Empty(_viewModel.Items);
    }

    [Fact]
    public async Task ErrorState_AccessDenied_ShowsErrorMessage()
    {
        string deniedPath = Path.Combine(_testRoot, "DeniedDir");
        Directory.CreateDirectory(deniedPath);

        _fakeDirService.SetError(deniedPath, new UnauthorizedAccessException("Forbidden"));

        await _viewModel.NavigateInternalAsync(deniedPath, NavigationPhaseType.New);

        Assert.True(_viewModel.HasError);
        Assert.Contains("Access denied", _viewModel.ErrorMessage);
        Assert.Empty(_viewModel.Items);
    }

    [Fact]
    public async Task HistoryConsistency_BackToDeletedFolder_MaintainsHistory()
    {
        string dir1 = Path.Combine(_testRoot, "Dir1");
        string dir2 = Path.Combine(_testRoot, "Dir2");
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);

        _fakeDirService.AddDirectory(dir1, Array.Empty<FileEntry>());
        _fakeDirService.AddDirectory(dir2, Array.Empty<FileEntry>());

        // Navigate Dir1 -> Dir2
        await _viewModel.NavigateInternalAsync(dir1, NavigationPhaseType.New);
        await _viewModel.NavigateInternalAsync(dir2, NavigationPhaseType.New);

        Assert.Equal(dir2, _viewModel.CurrentPath);
        Assert.True(_viewModel.CanGoBack);

        // Delete Dir1 on disk
        Directory.Delete(dir1, true);

        // Attempt GoBack
        _viewModel.GoBackCommand.Execute(null);

        // Allow async handler to run
        await Task.Delay(50);

        // Dir1 was deleted, so error is displayed, but CurrentPath remains Dir2 and CanGoBack remains true
        Assert.True(_viewModel.HasError);
        Assert.Contains("Directory not found", _viewModel.ErrorMessage);
        Assert.Equal(dir2, _viewModel.CurrentPath);
        Assert.True(_viewModel.CanGoBack);
    }

    [Fact]
    public async Task Sort_PreservesSelection()
    {
        string dir = Path.Combine(_testRoot, "SortDir");
        Directory.CreateDirectory(dir);

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
    public async Task MultiSelect_SyncUpdatesSelectionStatusText()
    {
        string dir = Path.Combine(_testRoot, "MultiSelectDir");
        Directory.CreateDirectory(dir);

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
    public async Task Cancellation_WhenNavigatingAwayMidLoad()
    {
        string dirSlow = Path.Combine(_testRoot, "DirSlow");
        string dirFast = Path.Combine(_testRoot, "DirFast");
        Directory.CreateDirectory(dirSlow);
        Directory.CreateDirectory(dirFast);

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
        string dirA = Path.Combine(_testRoot, "DirA_Large");
        string dirB = Path.Combine(_testRoot, "DirB_Small");
        Directory.CreateDirectory(dirA);
        Directory.CreateDirectory(dirB);

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
}
