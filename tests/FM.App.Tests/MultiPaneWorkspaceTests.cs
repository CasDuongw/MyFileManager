using FM.App.Models;
using FM.App.Services;
using FM.App.Tests.Fakes;
using FM.App.ViewModels;
using FM.Core.FileSystem;

namespace FM.App.Tests;

public class InMemorySettingsService : ISettingsService
{
    public AppSettings Settings { get; set; } = new();

    public AppSettings LoadSettings() => Settings;

    public void SaveSettings(AppSettings settings)
    {
        Settings = settings;
    }

    public Task ValidatePathsAsync(AppSettings settings, TimeSpan? timeout = null) => Task.CompletedTask;
}

public class MultiPaneWorkspaceTests
{
    private readonly InMemoryFileSystem _fs;
    private readonly FileSystemBackedDirectoryService _dirService;
    private readonly FakeIconService _iconService;
    private readonly InMemorySettingsService _settingsService;

    public MultiPaneWorkspaceTests()
    {
        _fs = new InMemoryFileSystem();
        _fs.CreateDirectory(@"C:\dir1");
        _fs.CreateDirectory(@"C:\dir2");
        _fs.CreateDirectory(@"C:\dir3");
        _fs.CreateDirectory(@"C:\dir4");

        _dirService = new FileSystemBackedDirectoryService(_fs);
        _iconService = new FakeIconService();
        _settingsService = new InMemorySettingsService();
    }

    [Fact]
    public void DefaultState_HasTwoPanes_DualVerticalLayout_ActivePaneIsLeft()
    {
        var vm = new MainViewModel(_dirService, _iconService, _settingsService, _fs);

        Assert.Equal(2, vm.Panes.Count);
        Assert.Equal(PaneLayoutMode.DualVertical, vm.LayoutMode);
        Assert.Same(vm.Panes[0], vm.LeftPane);
        Assert.Same(vm.Panes[1], vm.RightPane);
        Assert.Same(vm.Panes[0], vm.ActivePane);
        Assert.True(vm.CanClosePanes);
    }

    [Fact]
    public async Task SplitVertical_AddsPane_InheritsPath_SwitchesLayout()
    {
        var vm = new MainViewModel(_dirService, _iconService, _settingsService, _fs);
        await vm.ActivePane.NavigateAsync(@"C:\dir1");

        vm.SplitVerticalCommand.Execute(vm.ActivePane);

        Assert.Equal(3, vm.Panes.Count);
        Assert.Equal(PaneLayoutMode.TripleLeft, vm.LayoutMode);
        Assert.Equal(@"C:\dir1", vm.Panes[1].CurrentPath);
        Assert.Same(vm.Panes[1], vm.ActivePane);
    }

    [Fact]
    public void SplitHorizontal_FromSingle_BecomesDualHorizontal()
    {
        var vm = new MainViewModel(_dirService, _iconService, _settingsService, _fs);
        vm.SetLayoutCommand.Execute("Single");
        Assert.Single(vm.Panes);
        Assert.Equal(PaneLayoutMode.Single, vm.LayoutMode);

        vm.SplitHorizontalCommand.Execute(null);

        Assert.Equal(2, vm.Panes.Count);
        Assert.Equal(PaneLayoutMode.DualHorizontal, vm.LayoutMode);
    }

    [Fact]
    public void SetLayout_QuadGrid_CreatesFourPanes_AndSetsLayout()
    {
        var vm = new MainViewModel(_dirService, _iconService, _settingsService, _fs);

        vm.SetLayoutCommand.Execute("QuadGrid");

        Assert.Equal(4, vm.Panes.Count);
        Assert.Equal(PaneLayoutMode.QuadGrid, vm.LayoutMode);
        Assert.True(vm.CanClosePanes);
    }

    [Fact]
    public void SetLayout_Single_ReducesPanesToOne_DisablesClose()
    {
        var vm = new MainViewModel(_dirService, _iconService, _settingsService, _fs);
        Assert.Equal(2, vm.Panes.Count);

        vm.SetLayoutCommand.Execute("Single");

        Assert.Single(vm.Panes);
        Assert.Equal(PaneLayoutMode.Single, vm.LayoutMode);
        Assert.False(vm.CanClosePanes);
        Assert.False(vm.ClosePaneCommand.CanExecute(vm.ActivePane));
    }

    [Fact]
    public void ClosePane_RemovesTargetPane_ActivatesAdjacentPane()
    {
        var vm = new MainViewModel(_dirService, _iconService, _settingsService, _fs);
        vm.SetLayoutCommand.Execute("QuadGrid");
        Assert.Equal(4, vm.Panes.Count);

        var paneToClose = vm.Panes[2];
        vm.SetActivePane(paneToClose);
        Assert.Same(paneToClose, vm.ActivePane);

        vm.ClosePaneCommand.Execute(paneToClose);

        Assert.Equal(3, vm.Panes.Count);
        Assert.DoesNotContain(paneToClose, vm.Panes);
        Assert.Same(vm.Panes[2], vm.ActivePane);
    }

    [Fact]
    public void SwitchActivePane_CyclesThroughAllPanes()
    {
        var vm = new MainViewModel(_dirService, _iconService, _settingsService, _fs);
        vm.SetLayoutCommand.Execute("QuadGrid");
        Assert.Equal(4, vm.Panes.Count);

        vm.SetActivePane(vm.Panes[0]);
        Assert.Same(vm.Panes[0], vm.ActivePane);

        vm.SwitchActivePaneCommand.Execute(null);
        Assert.Same(vm.Panes[1], vm.ActivePane);

        vm.SwitchActivePaneCommand.Execute(null);
        Assert.Same(vm.Panes[2], vm.ActivePane);

        vm.SwitchActivePaneCommand.Execute(null);
        Assert.Same(vm.Panes[3], vm.ActivePane);

        vm.SwitchActivePaneCommand.Execute(null);
        Assert.Same(vm.Panes[0], vm.ActivePane);
    }

    [Fact]
    public async Task SettingsPersistence_SavesAndRestoresMultiPaneLayout()
    {
        _settingsService.Settings = new AppSettings
        {
            LayoutMode = "QuadGrid",
            PanePaths = new List<string> { @"C:\dir1", @"C:\dir2", @"C:\dir3", @"C:\dir4" }
        };

        var vm = new MainViewModel(_dirService, _iconService, _settingsService, _fs);
        await vm.InitializeSettingsAsync(_settingsService.Settings);

        Assert.Equal(4, vm.Panes.Count);
        Assert.Equal(PaneLayoutMode.QuadGrid, vm.LayoutMode);
        Assert.Equal(@"C:\dir1", vm.Panes[0].CurrentPath);
        Assert.Equal(@"C:\dir2", vm.Panes[1].CurrentPath);
        Assert.Equal(@"C:\dir3", vm.Panes[2].CurrentPath);
        Assert.Equal(@"C:\dir4", vm.Panes[3].CurrentPath);

        // Modify layout and panes
        vm.ClosePaneCommand.Execute(vm.Panes[3]);
        Assert.Equal(3, vm.Panes.Count);

        // Trigger settings save
        vm.PersistSettings(100, 100, 1200, 800, "Normal");

        Assert.Equal(3, _settingsService.Settings.PanePaths.Count);
        Assert.Equal(vm.LayoutMode.ToString(), _settingsService.Settings.LayoutMode);
    }

    [Fact]
    public void Breadcrumbs_GeneratesExpectedSegments()
    {
        var crumbs = FilePaneViewModel.GenerateBreadcrumbs(@"C:\Users\PC\Documents");

        Assert.Equal(4, crumbs.Count);
        Assert.Equal("C:", crumbs[0].Name);
        Assert.False(crumbs[0].IsLast);

        Assert.Equal("Users", crumbs[1].Name);
        Assert.False(crumbs[1].IsLast);

        Assert.Equal("PC", crumbs[2].Name);
        Assert.False(crumbs[2].IsLast);

        Assert.Equal("Documents", crumbs[3].Name);
        Assert.True(crumbs[3].IsLast);
    }

    [Fact]
    public void ViewModes_SwitchLocallyAndGlobally()
    {
        var vm = new MainViewModel(_dirService, _iconService, _settingsService, _fs);
        var pane = vm.ActivePane;

        Assert.Equal(FilePaneViewMode.Details, pane.ViewMode);

        pane.SetViewMode("Icons");
        Assert.Equal(FilePaneViewMode.Icons, pane.ViewMode);

        pane.SetViewMode("Preview");
        Assert.Equal(FilePaneViewMode.Preview, pane.ViewMode);

        // Global switch
        vm.SetGlobalViewMode("Details");
        Assert.All(vm.Panes, p => Assert.Equal(FilePaneViewMode.Details, p.ViewMode));
    }

    [Fact]
    public void AddressEditing_TogglesState()
    {
        var vm = new MainViewModel(_dirService, _iconService, _settingsService, _fs);
        var pane = vm.ActivePane;

        Assert.False(pane.IsEditingAddress);

        pane.StartEditingAddress();
        Assert.True(pane.IsEditingAddress);

        pane.StopEditingAddress();
        Assert.False(pane.IsEditingAddress);
    }
}
