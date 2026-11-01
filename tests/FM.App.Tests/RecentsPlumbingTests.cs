using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FM.App.Models;
using FM.App.Services;
using FM.App.Tests.Fakes;
using FM.App.ViewModels;
using FM.Core.FileSystem;
using FM.Core.Services;
using Xunit;

namespace FM.App.Tests;

public class RecentsPlumbingTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _tempSettingsFile;
    private readonly InMemoryFileSystem _fileSystem;
    private readonly IDirectoryService _directoryService;
    private readonly FakeIconService _iconService;
    private readonly TestTimeProvider _timeProvider;

    public RecentsPlumbingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FM_RecentsTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _tempSettingsFile = Path.Combine(_tempDir, "settings.json");

        _fileSystem = new InMemoryFileSystem();
        _fileSystem.CreateDirectory(@"C:\FolderA");
        _fileSystem.CreateDirectory(@"C:\FolderB");
        _fileSystem.CreateDirectory(@"C:\FolderC");

        _directoryService = new FileSystemBackedDirectoryService(_fileSystem);
        _iconService = new FakeIconService();
        _timeProvider = new TestTimeProvider();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public void SettingsService_RoundTrips_RecentFolders()
    {
        var service = new SettingsService(_tempSettingsFile);
        var original = new AppSettings
        {
            RecentFolders = new List<string> { @"C:\FolderA", @"C:\FolderB", @"C:\FolderC" }
        };

        service.SaveSettings(original);
        var loaded = service.LoadSettings();

        Assert.NotNull(loaded.RecentFolders);
        Assert.Equal(3, loaded.RecentFolders.Count);
        Assert.Equal(@"C:\FolderA", loaded.RecentFolders[0]);
        Assert.Equal(@"C:\FolderB", loaded.RecentFolders[1]);
        Assert.Equal(@"C:\FolderC", loaded.RecentFolders[2]);
    }

    [Fact]
    public void SettingsService_ValidatesRecentFolders_DedupAndCap()
    {
        var service = new SettingsService(_tempSettingsFile);
        var list = new List<string>
        {
            @"C:\FolderA",
            "",
            "   ",
            @"c:\foldera", // duplicate case-insensitive
            @"C:\FolderB"
        };
        for (int i = 1; i <= 15; i++)
        {
            list.Add($@"C:\Folder_{i}");
        }

        var settings = new AppSettings { RecentFolders = list };
        service.SaveSettings(settings);
        var loaded = service.LoadSettings();

        Assert.Equal(10, loaded.RecentFolders.Count);
        Assert.Equal(@"C:\FolderA", loaded.RecentFolders[0]);
        Assert.Equal(@"C:\FolderB", loaded.RecentFolders[1]);
        Assert.DoesNotContain(loaded.RecentFolders, string.IsNullOrWhiteSpace);
    }

    [Fact]
    public async Task PersistSettings_DoesNotErase_RecentFolders()
    {
        var service = new SettingsService(_tempSettingsFile);
        var initial = new AppSettings
        {
            RecentFolders = new List<string> { @"C:\FolderA", @"C:\FolderB" }
        };
        service.SaveSettings(initial);

        var vm = new MainViewModel(
            _directoryService,
            _iconService,
            service,
            _fileSystem,
            timeProvider: _timeProvider);

        await vm.InitializeSettingsAsync(service.LoadSettings());

        vm.RecentFolders.Add(@"C:\FolderC");

        // When PersistSettings is invoked on window close
        vm.PersistSettings(100, 100, 1200, 800, "Normal");

        var reloaded = service.LoadSettings();
        Assert.NotNull(reloaded.RecentFolders);
        Assert.Equal(3, reloaded.RecentFolders.Count);
        Assert.Equal(@"C:\FolderC", reloaded.RecentFolders[0]);
        Assert.Contains(@"C:\FolderA", reloaded.RecentFolders);
        Assert.Contains(@"C:\FolderB", reloaded.RecentFolders);
    }

    [Fact]
    public async Task FilePaneViewModel_FolderOpened_FiresOnNavigation_NotOnRefresh()
    {
        var pane = new FilePaneViewModel(
            _directoryService,
            _iconService,
            _fileSystem,
            new FakeInputDialogService());

        var openedFolders = new List<string>();
        pane.FolderOpened += (p, path) => openedFolders.Add(path);

        pane.Navigate(@"C:\FolderA");
        await Task.Delay(50);

        Assert.Single(openedFolders);
        Assert.Equal(@"C:\FolderA", openedFolders[0]);

        // RefreshAsync must NOT fire FolderOpened
        await pane.RefreshAsync();
        Assert.Single(openedFolders);

        pane.Navigate(@"C:\FolderB");
        await Task.Delay(50);

        Assert.Equal(2, openedFolders.Count);
        Assert.Equal(@"C:\FolderB", openedFolders[1]);
    }

    [Fact]
    public async Task InitializeSettingsAsync_DoesNotRecordSessionRestoredFolders()
    {
        var service = new SettingsService(_tempSettingsFile);
        var settings = new AppSettings
        {
            LeftPanePath = @"C:\FolderA",
            RightPanePath = @"C:\FolderB",
            PanePaths = new List<string> { @"C:\FolderA", @"C:\FolderB" },
            RecentFolders = new List<string>() // empty recents initially
        };

        var vm = new MainViewModel(
            _directoryService,
            _iconService,
            service,
            _fileSystem,
            timeProvider: _timeProvider);

        await vm.InitializeSettingsAsync(settings);
        await Task.Delay(50);

        // Session restore should NOT have populated _recentFolders
        Assert.Empty(vm.RecentFolders.Items);
    }

    [Fact]
    public async Task PaneNavigation_RecordsRecents_AndDebouncedSaveFlushes()
    {
        var service = new SettingsService(_tempSettingsFile);
        service.SaveSettings(new AppSettings());

        var vm = new MainViewModel(
            _directoryService,
            _iconService,
            service,
            _fileSystem,
            timeProvider: _timeProvider);

        vm.ActivePane.Navigate(@"C:\FolderA");
        await Task.Delay(50);

        Assert.Contains(@"C:\FolderA", vm.RecentFolders.Items);

        // Advance timer by 1.5 seconds to trigger write-behind persistence
        _timeProvider.Advance(TimeSpan.FromSeconds(1.6));

        var loaded = service.LoadSettings();
        Assert.Contains(@"C:\FolderA", loaded.RecentFolders);
    }
}
