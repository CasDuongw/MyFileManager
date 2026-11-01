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
    public void SetLayout_Single_ReducesPanesToOne_CanCloseToZero()
    {
        var vm = new MainViewModel(_dirService, _iconService, _settingsService, _fs);
        Assert.Equal(2, vm.Panes.Count);

        vm.SetLayoutCommand.Execute("Single");

        Assert.Single(vm.Panes);
        Assert.Equal(PaneLayoutMode.Single, vm.LayoutMode);
        Assert.True(vm.CanClosePanes);
        Assert.True(vm.ClosePaneCommand.CanExecute(vm.ActivePane));

        // Close the remaining pane to reach 0 panes
        vm.ClosePaneCommand.Execute(vm.ActivePane);
        Assert.Empty(vm.Panes);
        Assert.False(vm.HasActivePane);
        Assert.False(vm.CanClosePanes);
        Assert.False(vm.ClosePaneCommand.CanExecute(null));
    }

    [Fact]
    public void ClosePane_WhenTwoPanes_ClosingActivePane_DoesNotThrow()
    {
        var vm = new MainViewModel(_dirService, _iconService, _settingsService, _fs);
        Assert.Equal(2, vm.Panes.Count);

        var firstPane = vm.Panes[0];
        vm.SetActivePane(firstPane);

        // Closing the active first pane should cleanly switch active pane to remaining pane
        vm.ClosePaneCommand.Execute(firstPane);

        Assert.Single(vm.Panes);
        Assert.DoesNotContain(firstPane, vm.Panes);
        Assert.Same(vm.Panes[0], vm.ActivePane);
        Assert.True(vm.ActivePane.IsActive);
    }

    [Fact]
    public void CloseAllPanes_ReachesZeroPanes_AndReopening_Works()
    {
        var vm = new MainViewModel(_dirService, _iconService, _settingsService, _fs);
        Assert.Equal(2, vm.Panes.Count);

        vm.ClosePaneCommand.Execute(vm.Panes[0]);
        vm.ClosePaneCommand.Execute(vm.Panes[0]);

        Assert.Empty(vm.Panes);
        Assert.False(vm.HasActivePane);
        Assert.False(vm.CanClosePanes);

        // Re-open tab from Sidebar or New Tab
        vm.OpenNewTab();
        Assert.Single(vm.Panes);
        Assert.True(vm.HasActivePane);
        Assert.True(vm.CanClosePanes);
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

    [Fact]
    public void DockPane_MovesPaneToTargetEdge_ActivatesDraggedPane()
    {
        var vm = new MainViewModel(_dirService, _iconService, _settingsService, _fs);
        vm.SetLayoutCommand.Execute("QuadGrid");
        Assert.Equal(4, vm.Panes.Count);

        var pane0 = vm.Panes[0];
        var pane3 = vm.Panes[3];

        // Dock pane 3 to the right of pane 0
        vm.DockPane(pane3, pane0, DockEdge.Right);

        Assert.Equal(4, vm.Panes.Count);
        Assert.Equal(pane0, vm.Panes[0]);
        Assert.Equal(pane3, vm.Panes[1]);
        Assert.Same(pane3, vm.ActivePane);

        // Dock pane 3 to the left of pane 0
        vm.DockPane(pane3, pane0, DockEdge.Left);
        Assert.Equal(pane3, vm.Panes[0]);
        Assert.Equal(pane0, vm.Panes[1]);
    }

    [Fact]
    public void DockPane_CenterEdge_SwapsSourceAndTargetPanes()
    {
        var vm = new MainViewModel(_dirService, _iconService, _settingsService, _fs);
        vm.SetLayoutCommand.Execute("TripleLeft");
        Assert.Equal(3, vm.Panes.Count);

        var p0 = vm.Panes[0];
        var p1 = vm.Panes[1];
        var p2 = vm.Panes[2];

        // Drop p1 into center of p0 -> swaps p0 and p1
        vm.DockPane(p1, p0, DockEdge.Center);

        Assert.Equal(p1, vm.Panes[0]);
        Assert.Equal(p0, vm.Panes[1]);
        Assert.Equal(p2, vm.Panes[2]);
        Assert.Same(p1, vm.ActivePane);
    }

    [Fact]
    public void DockPane_InTripleLeft_DroppingBottomRightPaneOnLeftPane_MovesDraggedPaneToLeft()
    {
        var vm = new MainViewModel(_dirService, _iconService, _settingsService, _fs);
        vm.SetLayoutCommand.Execute("TripleLeft");
        Assert.Equal(3, vm.Panes.Count);

        var p0_left = vm.Panes[0];
        var p1_topRight = vm.Panes[1];
        var p2_bottomRight = vm.Panes[2];

        // Drag bottom-right pane (Downloads) onto left pane (p0) with DockEdge.Right
        vm.DockPane(p2_bottomRight, p0_left, DockEdge.Right);

        // The dragged pane must now be at index 0 (the Left pane), NOT jumping to top-right!
        Assert.Equal(p2_bottomRight, vm.Panes[0]);
        Assert.Equal(p1_topRight, vm.Panes[1]);
        Assert.Equal(p0_left, vm.Panes[2]);
        Assert.Same(p2_bottomRight, vm.ActivePane);
    }

    [Fact]
    public void DockPane_EdgeWhenAlreadyAdjacent_SwapsPanesInsteadOfNoOp()
    {
        var vm = new MainViewModel(_dirService, _iconService, _settingsService, _fs);
        vm.SetLayoutCommand.Execute("TripleLeft");
        Assert.Equal(3, vm.Panes.Count);

        var p0 = vm.Panes[0];
        var p1 = vm.Panes[1];
        var p2 = vm.Panes[2];

        // p1 is already directly to the right of p0 (index 1).
        // If user drags p1 and drops it to the Right of p0, it swaps p0 and p1 so the move always produces an effect!
        vm.DockPane(p1, p0, DockEdge.Right);

        Assert.Equal(p1, vm.Panes[0]);
        Assert.Equal(p0, vm.Panes[1]);
        Assert.Equal(p2, vm.Panes[2]);
        Assert.Same(p1, vm.ActivePane);
    }

    [Fact]
    public void SplitPanes_SupportsUpTo16Panes()
    {
        var vm = new MainViewModel(_dirService, _iconService, _settingsService, _fs);

        // Split up to 16 panes
        for (int i = vm.Panes.Count; i < 16; i++)
        {
            vm.SplitVerticalCommand.Execute(null);
        }

        Assert.Equal(16, vm.Panes.Count);

        // Splitting again when at 16 does nothing
        vm.SplitVerticalCommand.Execute(null);
        Assert.Equal(16, vm.Panes.Count);

        vm.SplitHorizontalCommand.Execute(null);
        Assert.Equal(16, vm.Panes.Count);
    }
}