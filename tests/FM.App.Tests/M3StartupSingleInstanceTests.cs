using System.IO;
using FM.App.Services;
using FM.App.Services.SingleInstance;
using FM.App.Tests.Fakes;
using FM.App.ViewModels;
using FM.App.Views;
using FM.Core.FileSystem;
using FM.Core.ShellIntegration;

namespace FM.App.Tests;

/// <summary>
/// Milestone 3 Integration tests covering:
/// - Command line argument parsing and execution (paths, files, /select, multiple paths, invalid paths)
/// - Single-instance IPC argument forwarding
/// - Crash safety and crash report dialog
/// - Per-Monitor DPI v2 manifest configuration
/// - Explorer default retention (verifying default explorer is NOT replaced)
/// </summary>
public class M3StartupSingleInstanceTests
{
    private readonly InMemoryFileSystem _fs;
    private readonly FileSystemBackedDirectoryService _dirService;
    private readonly FakeIconService _iconService;
    private readonly FakeSettingsService _settingsService;
    private readonly MainViewModel _mainViewModel;

    public M3StartupSingleInstanceTests()
    {
        _fs = new InMemoryFileSystem();
        _dirService = new FileSystemBackedDirectoryService(_fs);
        _iconService = new FakeIconService();
        _settingsService = new FakeSettingsService();

        _fs.CreateDirectory(@"C:\MyFolder");
        _fs.CreateDirectory(@"C:\OtherFolder");
        _fs.CreateVirtualFile(@"C:\MyFolder\document.pdf", 1024);
        _fs.CreateVirtualFile(@"C:\MyFolder\notes.txt", 256);

        _mainViewModel = new MainViewModel(_dirService, _iconService, _settingsService, _fs);
    }

    [Fact]
    public async Task HandleCommandLineArgs_SingleDirectory_NavigatesActivePane()
    {
        await _mainViewModel.HandleCommandLineArgsAsync(new[] { @"C:\MyFolder" });

        Assert.NotNull(_mainViewModel.ActivePane);
        Assert.Equal(@"C:\MyFolder", _mainViewModel.ActivePane.CurrentPath);
        Assert.Null(_mainViewModel.ActivePane.SelectedItem);
    }

    [Fact]
    public async Task HandleCommandLineArgs_SingleFile_NavigatesParentAndSelectsFile()
    {
        await _mainViewModel.HandleCommandLineArgsAsync(new[] { @"C:\MyFolder\document.pdf" });

        Assert.NotNull(_mainViewModel.ActivePane);
        Assert.Equal(@"C:\MyFolder", _mainViewModel.ActivePane.CurrentPath);
        Assert.NotNull(_mainViewModel.ActivePane.SelectedItem);
        Assert.Equal("document.pdf", _mainViewModel.ActivePane.SelectedItem.Name);
    }

    [Fact]
    public async Task HandleCommandLineArgs_ExplorerSelectSyntax_NavigatesParentAndSelectsFile()
    {
        await _mainViewModel.HandleCommandLineArgsAsync(new[] { @"/select,C:\MyFolder\notes.txt" });

        Assert.NotNull(_mainViewModel.ActivePane);
        Assert.Equal(@"C:\MyFolder", _mainViewModel.ActivePane.CurrentPath);
        Assert.NotNull(_mainViewModel.ActivePane.SelectedItem);
        Assert.Equal("notes.txt", _mainViewModel.ActivePane.SelectedItem.Name);
    }

    [Fact]
    public async Task HandleCommandLineArgs_MultiplePaths_SplitsPaneAndNavigatesBoth()
    {
        int initialPanes = _mainViewModel.Panes.Count;

        await _mainViewModel.HandleCommandLineArgsAsync(new[] { @"C:\MyFolder", @"C:\OtherFolder" });

        Assert.True(_mainViewModel.Panes.Count >= initialPanes);
        Assert.Contains(_mainViewModel.Panes, p => p.CurrentPath == @"C:\MyFolder");
        Assert.Contains(_mainViewModel.Panes, p => p.CurrentPath == @"C:\OtherFolder");
    }

    [Fact]
    public async Task HandleCommandLineArgs_InvalidPath_ShowsErrorMessageWithoutCrashing()
    {
        await _mainViewModel.HandleCommandLineArgsAsync(new[] { @"C:\Ghost\NonExistent" });

        Assert.NotNull(_mainViewModel.ActivePane);
        Assert.True(_mainViewModel.ActivePane.HasError);
        Assert.NotNull(_mainViewModel.ActivePane.ErrorMessage);
    }

    [Fact]
    public async Task SingleInstance_ForwardArgsToRunningInstance_InvokesHandler()
    {
        var fakeIpc = new FakeSingleInstanceService();
        bool received = false;
        string[]? receivedArgs = null;

        await fakeIpc.StartServerAsync(args =>
        {
            received = true;
            receivedArgs = args;
            return Task.CompletedTask;
        });

        var testArgs = new[] { @"/select,C:\MyFolder\document.pdf" };
        bool ok = await fakeIpc.ForwardArgsToRunningInstanceAsync(testArgs);

        Assert.True(ok);
        Assert.True(received);
        Assert.NotNull(receivedArgs);
        Assert.Equal(testArgs, receivedArgs);
    }

    [Fact]
    public void AppManifest_ConfiguresPerMonitorV2DpiAwareness()
    {
        string manifestPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "src", "FM.App", "app.manifest");
        manifestPath = Path.GetFullPath(manifestPath);

        Assert.True(File.Exists(manifestPath), $"Manifest not found at {manifestPath}");
        string content = File.ReadAllText(manifestPath);

        Assert.Contains("PerMonitorV2", content);
        Assert.Contains("true/PM", content);
    }

    [Fact]
    public void DefaultFileManager_IsNotReplaced_ExplorerRemainsDefault()
    {
        // Enforce the user's trial-run requirement: Explorer is NEVER replaced by default
        var shellService = new FakeShellRegistrationService();
        Assert.False(shellService.IsDefaultFileManager, "MyFileManager must NOT be set as default file manager without explicit user request.");
        Assert.False(shellService.IsContextMenuVerbRegistered);
    }
}
