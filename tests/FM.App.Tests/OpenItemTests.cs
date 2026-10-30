using System.ComponentModel;
using FM.App.Tests.Fakes;
using FM.App.ViewModels;
using FM.Core.FileSystem;
using FM.Core.Models;

namespace FM.App.Tests;

public class OpenItemTests
{
    private readonly InMemoryFileSystem _fs;
    private readonly FakeDirectoryService _dirService;
    private readonly FakeIconService _iconService;
    private readonly FakeShellLauncher _shellLauncher;
    private readonly FilePaneViewModel _viewModel;

    public OpenItemTests()
    {
        _fs = new InMemoryFileSystem();
        _dirService = new FakeDirectoryService();
        _iconService = new FakeIconService();
        _shellLauncher = new FakeShellLauncher();

        _fs.CreateDirectory(@"C:\Folder");
        _viewModel = new FilePaneViewModel(
            _dirService,
            _iconService,
            _fs,
            shellLauncher: _shellLauncher);
    }

    [Fact]
    public async Task OpenItem_FileDoesNotExistOnDisk_ShowsFileNoLongerExistsAndRefreshesPane()
    {
        string filePath = @"C:\Folder\deleted.txt";
        // Note: filePath is NOT created in _fs

        var entry = new FileEntry("deleted.txt", filePath, IsDirectory: false, Size: 100, Modified: DateTime.UtcNow);
        _dirService.AddDirectory(@"C:\Folder", new[] { entry });

        await _viewModel.NavigateAsync(@"C:\Folder");
        Assert.Single(_viewModel.Items);

        var item = _viewModel.Items[0];
        await _viewModel.OpenItemAsync(item);

        Assert.True(_viewModel.HasError);
        Assert.Equal("File no longer exists", _viewModel.ErrorMessage);
        Assert.Empty(_shellLauncher.LaunchedPaths);
    }

    [Fact]
    public async Task OpenItem_Success_LaunchesFile()
    {
        string filePath = @"C:\Folder\doc.txt";
        _fs.CreateVirtualFile(filePath, 100);

        var entry = new FileEntry("doc.txt", filePath, IsDirectory: false, Size: 100, Modified: DateTime.UtcNow);
        _dirService.AddDirectory(@"C:\Folder", new[] { entry });

        await _viewModel.NavigateAsync(@"C:\Folder");
        var item = _viewModel.Items[0];

        _viewModel.OpenItem(item);

        Assert.False(_viewModel.HasError);
        Assert.Single(_shellLauncher.LaunchedPaths);
        Assert.Equal(filePath, _shellLauncher.LaunchedPaths[0]);
    }

    [Fact]
    public async Task OpenItem_Win32FileNotFound_ShowsFriendlyError()
    {
        string filePath = @"C:\Folder\doc.txt";
        _fs.CreateVirtualFile(filePath, 100);

        var entry = new FileEntry("doc.txt", filePath, IsDirectory: false, Size: 100, Modified: DateTime.UtcNow);
        _dirService.AddDirectory(@"C:\Folder", new[] { entry });
        await _viewModel.NavigateAsync(@"C:\Folder");

        _shellLauncher.ExceptionToThrow = new Win32Exception(2); // ERROR_FILE_NOT_FOUND

        await _viewModel.OpenItemAsync(_viewModel.Items[0]);

        Assert.True(_viewModel.HasError);
        Assert.Equal("File not found", _viewModel.ErrorMessage);
    }

    [Fact]
    public async Task OpenItem_Win32PathNotFound_ShowsFriendlyError()
    {
        string filePath = @"C:\Folder\doc.txt";
        _fs.CreateVirtualFile(filePath, 100);

        var entry = new FileEntry("doc.txt", filePath, IsDirectory: false, Size: 100, Modified: DateTime.UtcNow);
        _dirService.AddDirectory(@"C:\Folder", new[] { entry });
        await _viewModel.NavigateAsync(@"C:\Folder");

        _shellLauncher.ExceptionToThrow = new Win32Exception(3); // ERROR_PATH_NOT_FOUND

        await _viewModel.OpenItemAsync(_viewModel.Items[0]);

        Assert.True(_viewModel.HasError);
        Assert.Equal("File not found", _viewModel.ErrorMessage);
    }

    [Fact]
    public async Task OpenItem_Win32AccessDenied_ShowsFriendlyError()
    {
        string filePath = @"C:\Folder\doc.txt";
        _fs.CreateVirtualFile(filePath, 100);

        var entry = new FileEntry("doc.txt", filePath, IsDirectory: false, Size: 100, Modified: DateTime.UtcNow);
        _dirService.AddDirectory(@"C:\Folder", new[] { entry });
        await _viewModel.NavigateAsync(@"C:\Folder");

        _shellLauncher.ExceptionToThrow = new Win32Exception(5); // ERROR_ACCESS_DENIED

        await _viewModel.OpenItemAsync(_viewModel.Items[0]);

        Assert.True(_viewModel.HasError);
        Assert.Equal("Access denied", _viewModel.ErrorMessage);
    }

    [Fact]
    public async Task OpenItem_Win32NoAssociation_ShowsFriendlyError()
    {
        string filePath = @"C:\Folder\doc.unknown";
        _fs.CreateVirtualFile(filePath, 100);

        var entry = new FileEntry("doc.unknown", filePath, IsDirectory: false, Size: 100, Modified: DateTime.UtcNow);
        _dirService.AddDirectory(@"C:\Folder", new[] { entry });
        await _viewModel.NavigateAsync(@"C:\Folder");

        _shellLauncher.ExceptionToThrow = new Win32Exception(1155); // ERROR_NO_ASSOCIATION

        await _viewModel.OpenItemAsync(_viewModel.Items[0]);

        Assert.True(_viewModel.HasError);
        Assert.Equal("No application is associated with this file", _viewModel.ErrorMessage);
    }

    [Fact]
    public async Task OpenItem_Win32UserCancelled_DoesNotSetError()
    {
        string filePath = @"C:\Folder\doc.txt";
        _fs.CreateVirtualFile(filePath, 100);

        var entry = new FileEntry("doc.txt", filePath, IsDirectory: false, Size: 100, Modified: DateTime.UtcNow);
        _dirService.AddDirectory(@"C:\Folder", new[] { entry });
        await _viewModel.NavigateAsync(@"C:\Folder");

        _shellLauncher.ExceptionToThrow = new Win32Exception(1223); // ERROR_CANCELLED

        await _viewModel.OpenItemAsync(_viewModel.Items[0]);

        Assert.False(_viewModel.HasError);
        Assert.Null(_viewModel.ErrorMessage);
    }

    [Fact]
    public async Task OpenItem_Win32OtherCode_IncludesMessageAndErrorCode()
    {
        string filePath = @"C:\Folder\doc.txt";
        _fs.CreateVirtualFile(filePath, 100);

        var entry = new FileEntry("doc.txt", filePath, IsDirectory: false, Size: 100, Modified: DateTime.UtcNow);
        _dirService.AddDirectory(@"C:\Folder", new[] { entry });
        await _viewModel.NavigateAsync(@"C:\Folder");

        _shellLauncher.ExceptionToThrow = new Win32Exception(9999);

        await _viewModel.OpenItemAsync(_viewModel.Items[0]);

        Assert.True(_viewModel.HasError);
        Assert.NotNull(_viewModel.ErrorMessage);
        Assert.Contains("9999", _viewModel.ErrorMessage);
    }

    [Fact]
    public async Task OpenItem_GeneralException_ShowsCannotOpenFile()
    {
        string filePath = @"C:\Folder\doc.txt";
        _fs.CreateVirtualFile(filePath, 100);

        var entry = new FileEntry("doc.txt", filePath, IsDirectory: false, Size: 100, Modified: DateTime.UtcNow);
        _dirService.AddDirectory(@"C:\Folder", new[] { entry });
        await _viewModel.NavigateAsync(@"C:\Folder");

        _shellLauncher.ExceptionToThrow = new InvalidOperationException("Process host failed unexpectedly.");

        await _viewModel.OpenItemAsync(_viewModel.Items[0]);

        Assert.True(_viewModel.HasError);
        Assert.Equal("Cannot open file: Process host failed unexpectedly.", _viewModel.ErrorMessage);
    }
}
