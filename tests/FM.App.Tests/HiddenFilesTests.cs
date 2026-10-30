using System.IO;
using FM.App.Services;
using FM.App.Tests.Fakes;
using FM.App.ViewModels;
using FM.Core.FileSystem;
using FM.Core.Models;

namespace FM.App.Tests;

public class HiddenFilesTests
{
    private readonly InMemoryFileSystem _fs;
    private readonly FakeDirectoryService _dirService;
    private readonly FakeIconService _iconService;
    private readonly FakeSettingsService _settingsService;
    private readonly MainViewModel _mainViewModel;

    public HiddenFilesTests()
    {
        _fs = new InMemoryFileSystem();
        _dirService = new FakeDirectoryService();
        _iconService = new FakeIconService();
        _settingsService = new FakeSettingsService();

        _fs.CreateDirectory(@"C:\Mixed");
        _mainViewModel = new MainViewModel(_dirService, _iconService, _settingsService, _fs);
    }

    [Fact]
    public async Task ShowHiddenFiles_WhenFalse_HidesHiddenAndSystemFiles()
    {
        var normal = new FileEntry("normal.txt", @"C:\Mixed\normal.txt", false, 100, DateTime.UtcNow, IsHidden: false, IsSystem: false);
        var hidden = new FileEntry("hidden.txt", @"C:\Mixed\hidden.txt", false, 100, DateTime.UtcNow, IsHidden: true, IsSystem: false);
        var sys = new FileEntry("sys.txt", @"C:\Mixed\sys.txt", false, 100, DateTime.UtcNow, IsHidden: false, IsSystem: true);
        var sysHidden = new FileEntry("syshidden.txt", @"C:\Mixed\syshidden.txt", false, 100, DateTime.UtcNow, IsHidden: true, IsSystem: true);

        _dirService.AddDirectory(@"C:\Mixed", new[] { normal, hidden, sys, sysHidden });

        _mainViewModel.LeftPane.ShowHiddenFiles = false;
        await _mainViewModel.LeftPane.NavigateAsync(@"C:\Mixed");

        Assert.Single(_mainViewModel.LeftPane.Items);
        Assert.Equal("normal.txt", _mainViewModel.LeftPane.Items[0].Name);
        Assert.Equal(1.0, _mainViewModel.LeftPane.Items[0].Opacity);
    }

    [Fact]
    public async Task ShowHiddenFiles_WhenTrue_IncludesHiddenAndSystemWithDimmedOpacity()
    {
        var normal = new FileEntry("normal.txt", @"C:\Mixed\normal.txt", false, 100, DateTime.UtcNow, IsHidden: false, IsSystem: false);
        var hidden = new FileEntry("hidden.txt", @"C:\Mixed\hidden.txt", false, 100, DateTime.UtcNow, IsHidden: true, IsSystem: false);
        var sys = new FileEntry("sys.txt", @"C:\Mixed\sys.txt", false, 100, DateTime.UtcNow, IsHidden: false, IsSystem: true);

        _dirService.AddDirectory(@"C:\Mixed", new[] { normal, hidden, sys });

        _mainViewModel.LeftPane.ShowHiddenFiles = true;
        await _mainViewModel.LeftPane.NavigateAsync(@"C:\Mixed");

        Assert.Equal(3, _mainViewModel.LeftPane.Items.Count);

        var normalItem = _mainViewModel.LeftPane.Items.First(i => i.Name == "normal.txt");
        Assert.Equal(1.0, normalItem.Opacity);

        var hiddenItem = _mainViewModel.LeftPane.Items.First(i => i.Name == "hidden.txt");
        Assert.Equal(0.5, hiddenItem.Opacity);
        Assert.True(hiddenItem.IsHidden);

        var sysItem = _mainViewModel.LeftPane.Items.First(i => i.Name == "sys.txt");
        Assert.Equal(0.5, sysItem.Opacity);
        Assert.True(sysItem.IsSystem);
    }

    [Fact]
    public async Task ToggleHiddenFilesCommand_TogglesSettingRefreshesPanesAndSetsStatus()
    {
        var normal = new FileEntry("normal.txt", @"C:\Mixed\normal.txt", false, 100, DateTime.UtcNow, IsHidden: false, IsSystem: false);
        var hidden = new FileEntry("hidden.txt", @"C:\Mixed\hidden.txt", false, 100, DateTime.UtcNow, IsHidden: true, IsSystem: false);
        _dirService.AddDirectory(@"C:\Mixed", new[] { normal, hidden });

        _mainViewModel.ShowHiddenFiles = false;
        _mainViewModel.LeftPane.ShowHiddenFiles = false;
        _mainViewModel.RightPane.ShowHiddenFiles = false;

        await _mainViewModel.LeftPane.NavigateAsync(@"C:\Mixed");
        await _mainViewModel.RightPane.NavigateAsync(@"C:\Mixed");

        Assert.Single(_mainViewModel.LeftPane.Items);
        Assert.Single(_mainViewModel.RightPane.Items);

        // Toggle to true
        await _mainViewModel.ToggleHiddenFilesCommand.ExecuteAsync(null);

        Assert.True(_mainViewModel.ShowHiddenFiles);
        Assert.True(_mainViewModel.LeftPane.ShowHiddenFiles);
        Assert.True(_mainViewModel.RightPane.ShowHiddenFiles);
        Assert.Equal("Hidden files: Shown", _mainViewModel.ActivePane.SelectionStatusText);
        Assert.Equal(2, _mainViewModel.LeftPane.Items.Count);
        Assert.Equal(2, _mainViewModel.RightPane.Items.Count);

        // Toggle back to false
        await _mainViewModel.ToggleHiddenFilesCommand.ExecuteAsync(null);

        Assert.False(_mainViewModel.ShowHiddenFiles);
        Assert.False(_mainViewModel.LeftPane.ShowHiddenFiles);
        Assert.False(_mainViewModel.RightPane.ShowHiddenFiles);
        Assert.Equal("Hidden files: Hidden", _mainViewModel.ActivePane.SelectionStatusText);
        Assert.Single(_mainViewModel.LeftPane.Items);
        Assert.Single(_mainViewModel.RightPane.Items);
    }

    [Fact]
    public void SettingsService_PersistsAndLoadsShowHiddenFiles()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"FM_Settings_{Guid.NewGuid():N}.json");
        try
        {
            var service = new SettingsService(tempFile);
            var initial = service.LoadSettings();
            Assert.False(initial.ShowHiddenFiles); // default is false

            initial.ShowHiddenFiles = true;
            service.SaveSettings(initial);

            var reloaded = service.LoadSettings();
            Assert.True(reloaded.ShowHiddenFiles);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { }
            }
        }
    }
}
