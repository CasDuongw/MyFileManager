using System.IO;
using FM.App.Services;
using FM.App.Tests.Fakes;
using FM.App.ViewModels;
using FM.Core.FileSystem;
using FM.Core.Operations;
using FM.Core.RecycleBin;
using FM.Core.ShellIntegration;
using Xunit;

namespace FM.App.Tests;

public class M5aFileOperationsTests
{
    private const string MockRoot = @"C:\MockWorkspace";
    private readonly InMemoryFileSystem _fileSystem;
    private readonly FileSystemBackedDirectoryService _dirService;
    private readonly FakeIconService _iconService;
    private readonly FakeInputDialogService _inputDialog;
    private readonly FakeRecycleBinService _recycleBinService;
    private readonly FakeShellNewService _shellNewService;
    private readonly FakeSendToService _sendToService;
    private readonly ISettingsService _settingsService;
    private readonly FilePaneViewModel _viewModel;

    public M5aFileOperationsTests()
    {
        _fileSystem = new InMemoryFileSystem();
        _fileSystem.CreateDirectory(MockRoot);

        _dirService = new FileSystemBackedDirectoryService(_fileSystem);
        _iconService = new FakeIconService();
        _inputDialog = new FakeInputDialogService();
        _recycleBinService = new FakeRecycleBinService();
        _recycleBinService.FileSystem = _fileSystem;
        _shellNewService = new FakeShellNewService();
        _sendToService = new FakeSendToService();
        _settingsService = new SettingsService(Path.Combine(MockRoot, "settings.json"));

        _viewModel = new FilePaneViewModel(
            _dirService,
            _iconService,
            _fileSystem,
            _inputDialog,
            recycleBinService: _recycleBinService,
            shellNewService: _shellNewService,
            sendToService: _sendToService,
            settingsService: _settingsService);
    }

    [Fact]
    public async Task DeleteToRecycleBin_SupportsRecycleBin_PushesUndoAndRestores()
    {
        string filePath = Path.Combine(MockRoot, "document.pdf");
        using (_fileSystem.CreateFileForWrite(filePath)) { }

        await _viewModel.NavigateInternalAsync(MockRoot, NavigationPhaseType.New);
        Assert.Single(_viewModel.Items);
        _viewModel.UpdateSelectedItems(new[] { _viewModel.Items[0] });

        // Normal Delete -> sends to recycle bin
        await _viewModel.DeleteToRecycleBinCommand.ExecuteAsync(null);

        // File is deleted from disk
        Assert.False(_fileSystem.FileExists(filePath));
        Assert.Empty(_viewModel.Items);

        // Recycle bin has the item
        var binItems = await _recycleBinService.GetItemsAsync();
        Assert.Single(binItems);
        Assert.Equal("document.pdf", binItems[0].Name);

        // Undo is available
        Assert.True(_viewModel.UndoStack.CanUndo);

        // Execute Undo
        await _viewModel.UndoCommand.ExecuteAsync(null);

        // Restored
        Assert.True(_fileSystem.FileExists(filePath));
        Assert.Single(_viewModel.Items);
    }

    [Fact]
    public async Task DeleteToRecycleBin_NoRecycleBinLocation_PromptsForPermanentDelete()
    {
        string uncFolder = @"\\server\share\docs";
        _fileSystem.CreateDirectory(uncFolder);
        string filePath = Path.Combine(uncFolder, "network_file.txt");
        using (_fileSystem.CreateFileForWrite(filePath)) { }

        await _viewModel.NavigateInternalAsync(uncFolder, NavigationPhaseType.New);
        Assert.Single(_viewModel.Items);
        _viewModel.UpdateSelectedItems(new[] { _viewModel.Items[0] });

        // Confirm = true in dialog
        _inputDialog.NextConfirmResult = true;

        await _viewModel.DeleteToRecycleBinCommand.ExecuteAsync(null);

        // Prompts with message indicating no recycle bin support
        Assert.Contains("Recycle Bin", _inputDialog.LastMessage);
        Assert.False(_fileSystem.FileExists(filePath));
        Assert.Empty(_viewModel.Items);

        // Permanent deletion cannot be undone
        Assert.False(_viewModel.UndoStack.CanUndo);
    }

    [Fact]
    public async Task DeleteToRecycleBin_ConfirmNormalDeleteSetting_PromptsWhenTrue()
    {
        var settings = _settingsService.LoadSettings();
        settings.ConfirmNormalDelete = true;
        _settingsService.SaveSettings(settings);

        string filePath = Path.Combine(MockRoot, "test_confirm.txt");
        using (_fileSystem.CreateFileForWrite(filePath)) { }

        await _viewModel.NavigateInternalAsync(MockRoot, NavigationPhaseType.New);
        _viewModel.UpdateSelectedItems(new[] { _viewModel.Items[0] });

        _inputDialog.NextConfirmResult = false; // User cancels dialog
        await _viewModel.DeleteToRecycleBinCommand.ExecuteAsync(null);

        // File should NOT be deleted
        Assert.True(_fileSystem.FileExists(filePath));
        Assert.Single(_viewModel.Items);

        // Now user accepts
        _inputDialog.NextConfirmResult = true;
        await _viewModel.DeleteToRecycleBinCommand.ExecuteAsync(null);
        Assert.False(_fileSystem.FileExists(filePath));
    }

    [Fact]
    public async Task DeletePermanently_ShiftDelete_PromptsWithClearConfirmation()
    {
        string filePath = Path.Combine(MockRoot, "permanent.txt");
        using (_fileSystem.CreateFileForWrite(filePath)) { }

        await _viewModel.NavigateInternalAsync(MockRoot, NavigationPhaseType.New);
        _viewModel.UpdateSelectedItems(new[] { _viewModel.Items[0] });

        _inputDialog.NextConfirmResult = true;
        await _viewModel.DeletePermanentlyCommand.ExecuteAsync(null);

        Assert.Contains("permanently delete", _inputDialog.LastMessage);
        Assert.Contains("cannot be undone", _inputDialog.LastMessage);
        Assert.False(_fileSystem.FileExists(filePath));
        // Permanent delete is NOT pushed to UndoStack
        Assert.False(_viewModel.UndoStack.CanUndo);
    }

    [Fact]
    public async Task NewTextDocument_CreatesTxtFile_AndAvoidsCollision()
    {
        await _viewModel.NavigateInternalAsync(MockRoot, NavigationPhaseType.New);

        await _viewModel.NewTextDocumentCommand.ExecuteAsync(null);
        string firstDoc = Path.Combine(MockRoot, "New Text Document.txt");
        Assert.True(_fileSystem.FileExists(firstDoc));

        await _viewModel.NewTextDocumentCommand.ExecuteAsync(null);
        string secondDoc = Path.Combine(MockRoot, "New Text Document (2).txt");
        Assert.True(_fileSystem.FileExists(secondDoc));
    }

    [Fact]
    public async Task NewShortcut_And_CreateShortcutForSelected()
    {
        string targetFile = Path.Combine(MockRoot, "app.exe");
        using (_fileSystem.CreateFileForWrite(targetFile)) { }

        await _viewModel.NavigateInternalAsync(MockRoot, NavigationPhaseType.New);
        _viewModel.UpdateSelectedItems(new[] { _viewModel.Items[0] });

        // Create shortcut for selected
        _viewModel.CreateShortcutForSelectedCommand.Execute(null);
        string expectedLnk = Path.Combine(MockRoot, "app - Shortcut.lnk");
        Assert.True(_fileSystem.FileExists(expectedLnk));

        // Create a blank/new shortcut
        _inputDialog.NextResult = "CustomShortcut";
        await _viewModel.NewShortcutCommand.ExecuteAsync(null);
        string customLnk = Path.Combine(MockRoot, "CustomShortcut.lnk");
        Assert.True(_fileSystem.FileExists(customLnk));
    }

    [Fact]
    public async Task ShellNew_CreateFromTemplate_NullFileAndData()
    {
        _shellNewService.Templates.Add(new ShellNewTemplate(".docx", "Microsoft Word Document", ShellNewKind.NullFile));
        _shellNewService.Templates.Add(new ShellNewTemplate(".reg", "Registration Entries", ShellNewKind.Data, TemplateData: new byte[] { 0x31, 0x32, 0x33 }));

        await _viewModel.NavigateInternalAsync(MockRoot, NavigationPhaseType.New);

        var docxTmpl = _viewModel.NewTemplates.First(t => t.Extension == ".docx");
        await _viewModel.CreateFromTemplateCommand.ExecuteAsync(docxTmpl);
        Assert.True(_fileSystem.FileExists(Path.Combine(MockRoot, "New Microsoft Word Document.docx")));

        var regTmpl = _viewModel.NewTemplates.First(t => t.Extension == ".reg");
        await _viewModel.CreateFromTemplateCommand.ExecuteAsync(regTmpl);
        Assert.True(_fileSystem.FileExists(Path.Combine(MockRoot, "New Registration Entries.reg")));
    }

    [Fact]
    public async Task SendTo_ExecutesTargetAction()
    {
        string sendDest = @"C:\MockExternalDrive";
        _fileSystem.CreateDirectory(sendDest);

        var folderTarget = new SendToTarget("USB Drive (E:)", "E:\\", ResolvedDirectory: sendDest);
        _sendToService.Targets.Add(folderTarget);

        string sourceFile = Path.Combine(MockRoot, "file_to_send.txt");
        using (_fileSystem.CreateFileForWrite(sourceFile)) { }

        await _viewModel.NavigateInternalAsync(MockRoot, NavigationPhaseType.New);
        var fileItem = _viewModel.Items.First(i => i.Name == "file_to_send.txt");
        _viewModel.UpdateSelectedItems(new[] { fileItem });

        await _viewModel.SendToCommand.ExecuteAsync(folderTarget);
        Assert.True(_fileSystem.FileExists(Path.Combine(sendDest, "file_to_send.txt")));
    }

    [Fact]
    public async Task Selection_SelectAll_Invert_SelectByPattern()
    {
        using (_fileSystem.CreateFileForWrite(Path.Combine(MockRoot, "apple.txt"))) { }
        using (_fileSystem.CreateFileForWrite(Path.Combine(MockRoot, "banana.doc"))) { }
        using (_fileSystem.CreateFileForWrite(Path.Combine(MockRoot, "cherry.txt"))) { }

        await _viewModel.NavigateInternalAsync(MockRoot, NavigationPhaseType.New);
        Assert.Equal(3, _viewModel.Items.Count);

        // Select All
        _viewModel.SelectAllCommand.Execute(null);
        Assert.Equal(3, _viewModel.SelectedItems.Count);

        // Select only apple.txt, then Invert
        _viewModel.UpdateSelectedItems(new[] { _viewModel.Items.First(i => i.Name == "apple.txt") });
        Assert.Single(_viewModel.SelectedItems);

        _viewModel.InvertSelectionCommand.Execute(null);
        Assert.Equal(2, _viewModel.SelectedItems.Count);
        Assert.DoesNotContain(_viewModel.SelectedItems, i => i.Name == "apple.txt");

        // Select By Pattern
        _inputDialog.NextResult = "*.txt";
        _viewModel.SelectByPatternCommand.Execute(null);
        Assert.Equal(2, _viewModel.SelectedItems.Count);
        Assert.Contains(_viewModel.SelectedItems, i => i.Name == "apple.txt");
        Assert.Contains(_viewModel.SelectedItems, i => i.Name == "cherry.txt");
    }

    [Fact]
    public async Task CopyPath_Variants_ExecuteWithoutErrors()
    {
        string subFolder = Path.Combine(MockRoot, "sub");
        _fileSystem.CreateDirectory(subFolder);
        string file = Path.Combine(subFolder, "test.txt");
        using (_fileSystem.CreateFileForWrite(file)) { }

        await _viewModel.NavigateInternalAsync(MockRoot, NavigationPhaseType.New);
        var subItem = _viewModel.Items.First(i => i.Name == "sub");
        _viewModel.UpdateSelectedItems(new[] { subItem });

        // Verify commands run safely
        _viewModel.CopyPathCommand.Execute(null);
        _viewModel.CopyDirectoryPathCommand.Execute(null);
        _viewModel.CopyFileNameCommand.Execute(null);
        _viewModel.CopyRelativePathCommand.Execute(null);

        // Verify relative path calculation
        string relative = Path.GetRelativePath(MockRoot, file);
        Assert.Equal(Path.Combine("sub", "test.txt"), relative);
    }

    [Fact]
    public async Task SettingsService_PersistSettings_PreservesConfirmNormalDelete()
    {
        var settings = _settingsService.LoadSettings();
        settings.ConfirmNormalDelete = true;
        _settingsService.SaveSettings(settings);

        var reloaded = _settingsService.LoadSettings();
        Assert.True(reloaded.ConfirmNormalDelete);
    }

    [Fact]
    public void WindowsSendToService_GetTargets_DoesNotThrow()
    {
        var service = new WindowsSendToService();
        var targets = service.GetTargets();
        Assert.NotNull(targets);
    }

    [Fact]
    public void WindowsSendToService_CreateAndResolveShortcut_Succeeds()
    {
        if (!OperatingSystem.IsWindows()) return;
        string tempDir = Path.Combine(Path.GetTempPath(), "FMTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string targetFile = Path.Combine(tempDir, "target.txt");
            File.WriteAllText(targetFile, "content");
            string lnkPath = Path.Combine(tempDir, "target.lnk");

            WindowsSendToService.CreateShortcutFile(targetFile, lnkPath);
            Assert.True(File.Exists(lnkPath));

            string? resolved = WindowsSendToService.ResolveShortcutTarget(lnkPath);
            Assert.NotNull(resolved);
            Assert.Equal(Path.GetFullPath(targetFile), Path.GetFullPath(resolved!));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}
