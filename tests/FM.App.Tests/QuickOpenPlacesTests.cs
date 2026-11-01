using System;
using System.Collections.Generic;
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
        provider ??= new QuickOpenSourceProvider(_mainViewModel, _fileSystem);
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
}
