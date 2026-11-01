using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FM.App.Models;
using FM.App.Services;
using FM.App.Services.QuickOpen;
using FM.App.Tests.Fakes;
using FM.App.ViewModels;
using FM.Core.FileSystem;
using FM.Core.Services;
using Xunit;

namespace FM.App.Tests;

public class QuickOpenPlacesTests
{
    private readonly InMemoryFileSystem _fileSystem;
    private readonly IDirectoryService _directoryService;
    private readonly FakeIconService _iconService;
    private readonly FakeSettingsService _settingsService;
    private readonly MainViewModel _mainViewModel;

    public QuickOpenPlacesTests()
    {
        _fileSystem = new InMemoryFileSystem();
        _fileSystem.CreateDirectory(@"C:\Users\User\Documents");
        _fileSystem.CreateDirectory(@"C:\Users\User\Downloads");
        _fileSystem.CreateDirectory(@"C:\Users\User\Desktop");
        _fileSystem.CreateDirectory(@"D:\Projects\MyApp");

        _directoryService = new FileSystemBackedDirectoryService(_fileSystem);
        _iconService = new FakeIconService();
        _settingsService = new FakeSettingsService();

        _mainViewModel = new MainViewModel(
            _directoryService,
            _iconService,
            _settingsService,
            _fileSystem);

        // Set up test favorites and drives
        _mainViewModel.SidebarFavorites.Clear();
        _mainViewModel.SidebarFavorites.Add(new SidebarItemViewModel("Documents", @"C:\Users\User\Documents", SidebarItemCategory.Favorite));
        _mainViewModel.SidebarFavorites.Add(new SidebarItemViewModel("Downloads", @"C:\Users\User\Downloads", SidebarItemCategory.Favorite));

        _mainViewModel.SidebarDrives.Clear();
        _mainViewModel.SidebarDrives.Add(new SidebarItemViewModel("D:", @"D:\", SidebarItemCategory.Drive));

        // Set up recent folders
        _mainViewModel.RecentFolders.Clear();
        _mainViewModel.RecentFolders.Add(@"D:\Projects\MyApp");
        _mainViewModel.RecentFolders.Add(@"C:\Users\User\Documents"); // Duplicate of favorite!
    }

    private QuickOpenViewModel CreateViewModel(IQuickOpenSourceProvider? provider = null)
    {
        provider ??= new QuickOpenSourceProvider(_mainViewModel, _fileSystem, _mainViewModel.FolderIndex);
        return new QuickOpenViewModel(provider, uiDispatcher: a => a());
    }

    [Fact]
    public void Open_EmptyQuery_PopulatesPlaces_AndSelectsFirstItem()
    {
        var vm = CreateViewModel();

        vm.Open();

        Assert.True(vm.IsOpen);
        Assert.True(vm.HasResults);
        Assert.Equal(0, vm.SelectedIndex);
        Assert.NotNull(vm.SelectedItem);
        Assert.Equal(QuickOpenMode.Places, vm.Mode);
    }

    [Fact]
    public void Deduplication_FavoritesWinOverRecents_ForIdenticalPath()
    {
        var vm = CreateViewModel();

        vm.Open();

        // Documents exists in both Favorites and Recents
        var docItems = vm.Results.Where(r => string.Equals(r.TargetPath, @"C:\Users\User\Documents", StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.Single(docItems);
        Assert.Equal("Favorite", docItems[0].Badge);
    }

    [Fact]
    public void Query_FuzzyMatching_RanksPrefixContiguousMatchesFirst()
    {
        var vm = CreateViewModel();

        vm.Open("doc");

        Assert.True(vm.Results.Count > 0);
        Assert.Equal("Documents", vm.Results[0].Title);
    }

    [Fact]
    public void MoveSelection_WrapsAroundBothDirections()
    {
        var vm = CreateViewModel();
        vm.Open();

        int total = vm.Results.Count;
        Assert.True(total >= 2);

        // Initially at 0
        Assert.Equal(0, vm.SelectedIndex);

        // Move up -> wraps to total - 1
        vm.MoveSelection(-1);
        Assert.Equal(total - 1, vm.SelectedIndex);

        // Move down -> wraps back to 0
        vm.MoveSelection(1);
        Assert.Equal(0, vm.SelectedIndex);

        // Move down -> index 1
        vm.MoveSelection(1);
        Assert.Equal(1, vm.SelectedIndex);
    }

    [Fact]
    public void ExecuteSelected_NavigatesActivePane_AndClosesOverlay()
    {
        var vm = CreateViewModel();
        vm.Open("doc");

        Assert.Equal("Documents", vm.SelectedItem?.Title);

        vm.ExecuteSelected();

        Assert.False(vm.IsOpen);
        Assert.Equal(@"C:\Users\User\Documents", _mainViewModel.ActivePane.CurrentPath);
    }

    [Fact]
    public void ExecuteSelected_WhenItemUnavailable_StillExecutesAction()
    {
        var vm = CreateViewModel();
        vm.Open("doc");

        Assert.NotNull(vm.SelectedItem);
        vm.SelectedItem.IsAvailable = false; // Simulated offline or timed-out drive

        bool executed = false;
        vm.SelectedItem.ExecuteAction = () => executed = true;

        vm.ExecuteSelected();

        Assert.False(vm.IsOpen);
        Assert.True(executed, "Enter on an unavailable row must still attempt navigation");
    }

    [Fact]
    public void ExecuteSelected_OpenPaneItem_ActivatesTargetPane()
    {
        // Add a 2nd pane navigated to MyApp
        var pane2 = _mainViewModel.Panes[1];
        pane2.Navigate(@"D:\Projects\MyApp");

        _mainViewModel.SetActivePane(_mainViewModel.Panes[0]);
        Assert.Equal(_mainViewModel.Panes[0], _mainViewModel.ActivePane);

        var vm = CreateViewModel();
        vm.Open();

        // Find the open pane item for pane2
        var pane2Item = vm.Results.FirstOrDefault(r => r.Badge == "Open pane" && r.TargetPath == @"D:\Projects\MyApp");
        Assert.NotNull(pane2Item);

        vm.SelectedItem = pane2Item;
        vm.ExecuteSelected();

        Assert.False(vm.IsOpen);
        Assert.Equal(pane2, _mainViewModel.ActivePane);
    }

    [Fact]
    public void Close_ClearsResults_AndResetsState()
    {
        var vm = CreateViewModel();
        vm.Open("MyApp");

        Assert.True(vm.IsOpen);
        Assert.NotEmpty(vm.Results);

        vm.Close();

        Assert.False(vm.IsOpen);
        Assert.Empty(vm.QueryText);
        Assert.Empty(vm.Results);
        Assert.Equal(-1, vm.SelectedIndex);
        Assert.Null(vm.SelectedItem);
    }

    [Fact]
    public async Task CheckAvailabilityAsync_MarksMissingDirectoriesUnavailable()
    {
        var provider = new QuickOpenSourceProvider(_mainViewModel, _fileSystem);
        var missingItem = new QuickOpenItemViewModel
        {
            Title = "Ghost Folder",
            TargetPath = @"C:\NonExistent\Ghost",
            Kind = QuickOpenItemKind.Place,
            IsAvailable = true
        };

        var existingItem = new QuickOpenItemViewModel
        {
            Title = "Real Documents",
            TargetPath = @"C:\Users\User\Documents",
            Kind = QuickOpenItemKind.Place,
            IsAvailable = true
        };

        await provider.CheckAvailabilityAsync(new[] { missingItem, existingItem });

        Assert.False(missingItem.IsAvailable);
        Assert.True(existingItem.IsAvailable);
    }

    [Fact]
    public void Scenario1_LoadedPane_DU_AN_NHAN_Finds_DU_AN_CA_NHAN_AtTop()
    {
        string fptPath = @"C:\Users\User\Desktop\FPT_student";
        _fileSystem.CreateDirectory(fptPath);
        _fileSystem.CreateDirectory(System.IO.Path.Combine(fptPath, "DU_AN_CA_NHAN"));
        _fileSystem.CreateDirectory(System.IO.Path.Combine(fptPath, "ky_1"));
        _fileSystem.CreateDirectory(System.IO.Path.Combine(fptPath, "ky_2"));
        _fileSystem.CreateDirectory(System.IO.Path.Combine(fptPath, "sinh viên"));
        _fileSystem.CreateDirectory(System.IO.Path.Combine(fptPath, "thi_that"));

        var pane = _mainViewModel.ActivePane;
        pane.Navigate(fptPath);

        var vm = CreateViewModel();

        // 1. DU_AN_NHAN (subsequence match) -> DU_AN_CA_NHAN must be row 1
        vm.Open("DU_AN_NHAN");
        Assert.NotEmpty(vm.Results);
        Assert.Equal("DU_AN_CA_NHAN", vm.Results[0].Title);

        // 2. du an -> returns DU_AN_CA_NHAN
        vm.Open("du an");
        Assert.NotEmpty(vm.Results);
        Assert.Contains(vm.Results.Take(3), r => r.Title == "DU_AN_CA_NHAN");

        // 3. dacn (initials) -> returns DU_AN_CA_NHAN
        vm.Open("dacn");
        Assert.NotEmpty(vm.Results);
        Assert.Contains(vm.Results.Take(3), r => r.Title == "DU_AN_CA_NHAN");

        // 4. ca nhan -> returns DU_AN_CA_NHAN
        vm.Open("ca nhan");
        Assert.NotEmpty(vm.Results);
        Assert.Contains(vm.Results.Take(3), r => r.Title == "DU_AN_CA_NHAN");
    }

    [Fact]
    public void Scenario2_LoadedPane_VietnameseDiacriticsAndInitials()
    {
        string fptPath = @"C:\Users\User\Desktop\FPT_student";
        _fileSystem.CreateDirectory(fptPath);
        _fileSystem.CreateDirectory(System.IO.Path.Combine(fptPath, "sinh viên"));
        _fileSystem.CreateDirectory(System.IO.Path.Combine(fptPath, "Đồ án"));

        var pane = _mainViewModel.ActivePane;
        pane.Navigate(fptPath);

        var vm = CreateViewModel();

        // 1. 'sinh vien' finds 'sinh viên'
        vm.Open("sinh vien");
        Assert.NotEmpty(vm.Results);
        Assert.Equal("sinh viên", vm.Results[0].Title);

        // 2. 'sv' (initials) finds 'sinh viên'
        vm.Open("sv");
        Assert.NotEmpty(vm.Results);
        Assert.Contains(vm.Results.Take(3), r => r.Title == "sinh viên");

        // 3. 'do an' finds 'Đồ án' (maps đ/Đ to d)
        vm.Open("do an");
        Assert.NotEmpty(vm.Results);
        Assert.Equal("Đồ án", vm.Results[0].Title);
    }

    [Fact]
    public void Scenario4_QueryWithNoMatch_ShowsExplicitEmptyState()
    {
        var vm = CreateViewModel();
        vm.Open("xyzNonExistentQuery999");

        Assert.True(vm.IsOpen);
        Assert.Empty(vm.Results);
        Assert.True(vm.HasNoMatches);
        Assert.Equal("No matching folders", vm.EmptyStateTitle);
        Assert.False(string.IsNullOrEmpty(vm.StatusMessage));
    }

    [Fact]
    public void Scenario5_EnterOpensInActivePaneAtOpenTime()
    {
        string fptPath = @"C:\Users\User\Desktop\FPT_student";
        _fileSystem.CreateDirectory(fptPath);
        string targetDir = System.IO.Path.Combine(fptPath, "DU_AN_CA_NHAN");
        _fileSystem.CreateDirectory(targetDir);

        var activePane = _mainViewModel.ActivePane;
        activePane.Navigate(fptPath);

        var vm = CreateViewModel();
        vm.Open("DU_AN_NHAN");

        Assert.Equal("DU_AN_CA_NHAN", vm.SelectedItem?.Title);
        vm.ExecuteSelected();

        Assert.False(vm.IsOpen);
        Assert.Equal(targetDir, activePane.CurrentPath);
    }

    [Fact]
    public void Scenario6_StaleKeystrokeQuery_NeverOverwritesNewerResults()
    {
        var vm = CreateViewModel();
        vm.Open("doc");
        Assert.NotEmpty(vm.Results);
        Assert.Equal("Documents", vm.Results[0].Title);

        // Rapid keystroke update
        vm.Open("down");
        Assert.NotEmpty(vm.Results);
        Assert.Equal("Downloads", vm.Results[0].Title);
    }

    [Fact]
    public void Scenario3_UnvisitedFolder_NotInOpenPane_FoundViaFolderIndex()
    {
        _mainViewModel.FolderIndex.BulkAdd(new[]
        {
            @"D:\HoYoPlay\games",
            @"D:\TeraBoxDownload",
            @"D:\Cisco Packet Tracer 9.0.1"
        });

        var vm = CreateViewModel();

        // 1. games -> finds D:\HoYoPlay\games
        vm.Open("games");
        Assert.NotEmpty(vm.Results);
        Assert.Equal("games", vm.Results[0].Title);
        Assert.Equal(@"D:\HoYoPlay\games", vm.Results[0].TargetPath);

        // 2. tera -> finds D:\TeraBoxDownload
        vm.Open("tera");
        Assert.NotEmpty(vm.Results);
        Assert.Equal("TeraBoxDownload", vm.Results[0].Title);

        // 3. cisco -> finds Cisco Packet Tracer 9.0.1
        vm.Open("cisco");
        Assert.NotEmpty(vm.Results);
        Assert.Equal("Cisco Packet Tracer 9.0.1", vm.Results[0].Title);
    }

    [Fact]
    public void Scenario3_ProgressiveStatus_WhileIndexBuilding()
    {
        _mainViewModel.FolderIndex.Clear();
        _mainViewModel.FolderIndex.State = FM.Core.Search.FolderIndexState.Building;
        _mainViewModel.FolderIndex.BulkAdd(new[] { @"C:\Folder1", @"C:\Folder2", @"C:\Folder3" });

        var vm = CreateViewModel();
        vm.Open("xyzNonExistent");

        Assert.True(vm.HasNoMatches);
        Assert.StartsWith("Indexing...", vm.StatusMessage);
        Assert.Contains("3", vm.StatusMessage);
    }

    [Fact]
    public void PersistSettings_DoesNotEraseIndexedRootsAndExcludes()
    {
        var initialSettings = _settingsService.LoadSettings();
        initialSettings.IndexedRoots = new List<string> { @"D:\MyCode", @"E:\Data" };
        initialSettings.IndexExcludes = new List<string> { "target", "build" };
        _settingsService.SaveSettings(initialSettings);

        _mainViewModel.PersistSettings(100, 100, 1200, 800, "Normal");

        var reloadedSettings = _settingsService.LoadSettings();
        Assert.Equal(new[] { @"D:\MyCode", @"E:\Data" }, reloadedSettings.IndexedRoots);
        Assert.Equal(new[] { "target", "build" }, reloadedSettings.IndexExcludes);
    }

    [Fact]
    public void Frecency_SelectingRow_BoostsVisitScore_AndElevatesRanking()
    {
        string baseDir = @"C:\Users\User\Desktop\FPT_student";
        string p1 = Path.Combine(baseDir, "DU_AN_CA_NHAN");
        string p2 = Path.Combine(baseDir, "DU_AN_NHAN");
        _fileSystem.CreateDirectory(baseDir);
        _fileSystem.CreateDirectory(p1);
        _fileSystem.CreateDirectory(p2);

        _mainViewModel.ActivePane.Navigate(baseDir);

        var vm = CreateViewModel();
        vm.Open("du an");

        var target = vm.Results.FirstOrDefault(r => r.TargetPath == p2);
        Assert.NotNull(target);
        vm.SelectedItem = target;
        vm.ExecuteSelected();

        Assert.True(_mainViewModel.VisitStore.GetFrecencyScore(p2) > 0);
    }
}

