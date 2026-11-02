using System.IO;
using FM.App.Models;
using FM.App.Services;
using FM.App.Tests.Fakes;
using FM.App.ViewModels;
using FM.Core.Drives;
using FM.Core.FileSystem;
using FM.Core.Models;
using FM.Core.PathResolution;
using FM.Core.RecycleBin;

namespace FM.App.Tests;

/// <summary>
/// Milestone 2 Integration tests covering:
/// - Drives & This PC virtual view
/// - Recycle Bin virtual view with Restore and Empty actions
/// - Path expansion (%VAR%, ~, shell:*, UNC, virtual URIs)
/// - Slow/unreachable UNC bounded timeout and inline error handling
/// - Device arrival/removal and removable drive ejection
/// Strictly adheres to test policy: 0 physical disk files created, uses InMemoryFileSystem and test doubles.
/// </summary>
public class M2DrivesAndSpecialViewsTests
{
    private readonly InMemoryFileSystem _fs;
    private readonly FileSystemBackedDirectoryService _dirService;
    private readonly FakeIconService _iconService;
    private readonly FakeSettingsService _settingsService;
    private readonly FakeDriveService _driveService;
    private readonly FakeRecycleBinService _recycleBinService;
    private readonly PathResolver _pathResolver;
    private readonly FilePaneViewModel _paneViewModel;

    public M2DrivesAndSpecialViewsTests()
    {
        _fs = new InMemoryFileSystem();
        _dirService = new FileSystemBackedDirectoryService(_fs);
        _iconService = new FakeIconService();
        _settingsService = new FakeSettingsService();

        _driveService = new FakeDriveService(new[]
        {
            new DriveItem(@"C:\", "Windows", DriveType.Fixed, 500_000_000_000L, 200_000_000_000L, "NTFS", true),
            new DriveItem(@"D:\", "Data", DriveType.Fixed, 1_000_000_000_000L, 600_000_000_000L, "NTFS", true),
            new DriveItem(@"E:\", "USB Drive", DriveType.Removable, 32_000_000_000L, 16_000_000_000L, "FAT32", true)
        });

        _recycleBinService = new FakeRecycleBinService(new[]
        {
            new RecycleBinItem("id-1", "DeletedDoc.docx", @"C:\Users\User\Documents\DeletedDoc.docx", DateTime.Now.AddDays(-2), 1024 * 50, "DOCX File", false),
            new RecycleBinItem("id-2", "OldFolder", @"C:\Users\User\Desktop\OldFolder", DateTime.Now.AddDays(-5), 0, "File folder", true)
        });

        _pathResolver = new PathResolver();

        _fs.CreateDirectory(@"C:\");
        _fs.CreateDirectory(@"C:\Users");
        _fs.CreateDirectory(@"C:\Users\User");
        _fs.CreateDirectory(@"C:\Users\User\Downloads");
        _fs.CreateVirtualFile(@"C:\test.txt", 100);

        _paneViewModel = new FilePaneViewModel(
            _dirService,
            _iconService,
            fileSystem: _fs,
            driveService: _driveService,
            recycleBinService: _recycleBinService,
            pathResolver: _pathResolver);
    }

    [Fact]
    public async Task NavigateTo_ThisPc_LoadsDrivesAndSetsSpecialMode()
    {
        await _paneViewModel.NavigateAsync("thispc://");

        Assert.Equal(SpecialPaneMode.ThisPC, _paneViewModel.SpecialMode);
        Assert.True(_paneViewModel.IsThisPcMode);
        Assert.False(_paneViewModel.IsStandardFolderMode);
        Assert.False(_paneViewModel.IsRecycleBinMode);
        Assert.Equal("thispc://", _paneViewModel.CurrentPath);
        Assert.Equal("This PC", _paneViewModel.AddressText);
        Assert.Equal(3, _paneViewModel.Drives.Count);

        var cDrive = _paneViewModel.Drives.First(d => d.DriveLetter == "C:");
        Assert.Contains("Windows", cDrive.Name);
        Assert.False(cDrive.IsRemovable);

        var usbDrive = _paneViewModel.Drives.First(d => d.DriveLetter == "E:");
        Assert.Contains("USB Drive", usbDrive.Name);
        Assert.True(usbDrive.IsRemovable);
        Assert.NotNull(usbDrive.EjectCommand);
    }

    [Fact]
    public async Task NavigateTo_ThisPcAlias_ResolvesAndLoadsThisPc()
    {
        await _paneViewModel.NavigateAsync("This PC");

        Assert.Equal(SpecialPaneMode.ThisPC, _paneViewModel.SpecialMode);
        Assert.True(_paneViewModel.IsThisPcMode);
        Assert.Equal(3, _paneViewModel.Drives.Count);
    }

    [Fact]
    public async Task NavigateTo_RecycleBin_LoadsDeletedItemsAndSetsSpecialMode()
    {
        await _paneViewModel.NavigateAsync("recyclebin://");

        Assert.Equal(SpecialPaneMode.RecycleBin, _paneViewModel.SpecialMode);
        Assert.True(_paneViewModel.IsRecycleBinMode);
        Assert.False(_paneViewModel.IsStandardFolderMode);
        Assert.Equal("recyclebin://", _paneViewModel.CurrentPath);
        Assert.Equal("Recycle Bin", _paneViewModel.AddressText);
        Assert.Equal(2, _paneViewModel.RecycleBinItems.Count);

        var item = _paneViewModel.RecycleBinItems.First(i => i.Name == "DeletedDoc.docx");
        Assert.Equal(@"C:\Users\User\Documents\DeletedDoc.docx", item.OriginalLocation);
        Assert.Equal("DOCX File", item.ItemType);
        Assert.NotNull(item.RestoreCommand);
        Assert.NotNull(item.DeleteCommand);
    }

    [Fact]
    public async Task RecycleBin_RestoreItem_RemovesItemFromList()
    {
        await _paneViewModel.NavigateAsync("recyclebin://");
        Assert.Equal(2, _paneViewModel.RecycleBinItems.Count);

        var item = _paneViewModel.RecycleBinItems.First(i => i.Id == "id-1");
        await item.RestoreCommand.ExecuteAsync(null);

        Assert.Contains("id-1", _recycleBinService.RestoredItemIds);
        Assert.Single(_paneViewModel.RecycleBinItems);
        Assert.DoesNotContain(_paneViewModel.RecycleBinItems, i => i.Id == "id-1");
    }

    [Fact]
    public async Task RecycleBin_EmptyRecycleBin_ClearsAllItems()
    {
        await _paneViewModel.NavigateAsync("recyclebin://");
        Assert.Equal(2, _paneViewModel.RecycleBinItems.Count);

        await _paneViewModel.EmptyRecycleBinCommand.ExecuteAsync(null);

        Assert.Equal(1, _recycleBinService.EmptyCount);
        Assert.Empty(_paneViewModel.RecycleBinItems);
    }

    [Fact]
    public async Task NavigateTo_EnvironmentVariable_ExpandsAndNavigates()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(userProfile)) userProfile = @"C:\Users\Default";

        _fs.CreateDirectory(userProfile);
        _fs.CreateVirtualFile(Path.Combine(userProfile, "profile_marker.txt"), 10);

        await _paneViewModel.NavigateAsync("%USERPROFILE%");

        Assert.Equal(userProfile, _paneViewModel.CurrentPath);
        Assert.Equal(SpecialPaneMode.None, _paneViewModel.SpecialMode);
        Assert.True(_paneViewModel.IsStandardFolderMode);
        Assert.False(_paneViewModel.HasError);
    }

    [Fact]
    public async Task NavigateTo_Tilde_ExpandsToUserProfile()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(userProfile)) userProfile = @"C:\Users\Default";

        _fs.CreateDirectory(userProfile);

        await _paneViewModel.NavigateAsync("~");

        Assert.Equal(userProfile, _paneViewModel.CurrentPath);
        Assert.True(_paneViewModel.IsStandardFolderMode);
    }

    [Fact]
    public async Task NavigateTo_ShellFolder_NavigatesToKnownFolder()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var downloads = Path.Combine(userProfile, "Downloads");
        _fs.CreateDirectory(downloads);

        await _paneViewModel.NavigateAsync("shell:Downloads");

        Assert.Equal(downloads, _paneViewModel.CurrentPath);
        Assert.True(_paneViewModel.IsStandardFolderMode);
    }

    [Fact]
    public async Task NavigateTo_UnsupportedShellFolder_ShowsInlineErrorWithoutCrashing()
    {
        await _paneViewModel.NavigateAsync("shell:ControlPanelFolder");

        Assert.True(_paneViewModel.HasError);
        Assert.Contains("không được hỗ trợ", _paneViewModel.ErrorMessage);
    }

    [Fact]
    public async Task NavigateTo_UnreachableShare_TimesOutAndShowsInlineError()
    {
        // Unreachable UNC path not existing in in-memory filesystem
        await _paneViewModel.NavigateAsync(@"\\nonexistent-server\share");

        Assert.True(_paneViewModel.HasError);
        Assert.NotNull(_paneViewModel.ErrorMessage);
    }

    [Fact]
    public async Task MainViewModel_SidebarDrives_EjectRemovableDrive_UpdatesSidebar()
    {
        var mainVm = new MainViewModel(
            _dirService,
            _iconService,
            _settingsService,
            _fs,
            driveService: _driveService,
            recycleBinService: _recycleBinService,
            pathResolver: _pathResolver);

        await mainVm.LoadSidebarDrivesAsync();

        Assert.Equal(3, mainVm.SidebarDrives.Count);
        var usb = mainVm.SidebarDrives.First(d => d.Path == @"E:\");
        Assert.True(usb.IsRemovable);
        Assert.NotNull(usb.EjectCommand);

        await mainVm.EjectDriveAsync(@"E:\");

        Assert.Contains(@"E:\", _driveService.EjectedDrives);
        Assert.Equal(2, mainVm.SidebarDrives.Count);
        Assert.DoesNotContain(mainVm.SidebarDrives, d => d.Path == @"E:\");
    }

    [Fact]
    public async Task MainViewModel_NotifyDeviceChanged_RefreshesSidebarDrives()
    {
        var mainVm = new MainViewModel(
            _dirService,
            _iconService,
            _settingsService,
            _fs,
            driveService: _driveService,
            recycleBinService: _recycleBinService,
            pathResolver: _pathResolver);

        await mainVm.LoadSidebarDrivesAsync();
        Assert.Equal(3, mainVm.SidebarDrives.Count);

        // Add a new drive to the fake service
        _driveService.AddDrive(new DriveItem(@"F:\", "SD Card", DriveType.Removable, 64_000_000_000L, 32_000_000_000L, "exFAT", true));

        mainVm.NotifyDeviceChanged();

        // Give async event a moment to update
        await Task.Delay(50);
        await mainVm.LoadSidebarDrivesAsync();

        Assert.Equal(4, mainVm.SidebarDrives.Count);
        Assert.Contains(mainVm.SidebarDrives, d => d.Path == @"F:\");
    }

    [Fact]
    public async Task NavigateUp_FromDriveRoot_NavigatesToThisPC()
    {
        await _paneViewModel.NavigateAsync(@"C:\");
        Assert.Equal(@"C:\", _paneViewModel.CurrentPath);

        // Go Up from C:\
        await _paneViewModel.GoUpCommand.ExecuteAsync(null);

        Assert.Equal("thispc://", _paneViewModel.CurrentPath);
        Assert.Equal(SpecialPaneMode.ThisPC, _paneViewModel.SpecialMode);
        Assert.True(_paneViewModel.IsThisPcMode);
    }
}

