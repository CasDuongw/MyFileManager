using System;
using System.Collections.Generic;
using System.Linq;
using FM.App.Models;
using FM.App.Services;
using FM.App.Services.QuickOpen;
using FM.App.Tests.Fakes;
using FM.App.ViewModels;
using FM.Core.FileSystem;
using FM.Core.Services;
using Xunit;

namespace FM.App.Tests;

public class CommandPaletteTests
{
    private readonly InMemoryFileSystem _fileSystem;
    private readonly IDirectoryService _directoryService;
    private readonly FakeIconService _iconService;
    private readonly FakeSettingsService _settingsService;
    private readonly MainViewModel _mainViewModel;

    public CommandPaletteTests()
    {
        _fileSystem = new InMemoryFileSystem();
        _fileSystem.CreateDirectory(@"C:\FolderA");
        _fileSystem.CreateFile(@"C:\FolderA\file1.txt", new byte[10]);

        _directoryService = new FileSystemBackedDirectoryService(_fileSystem);
        _iconService = new FakeIconService();
        _settingsService = new FakeSettingsService();

        _mainViewModel = new MainViewModel(
            _directoryService,
            _iconService,
            _settingsService,
            _fileSystem);
    }

    private QuickOpenViewModel CreateViewModel()
    {
        var provider = new QuickOpenSourceProvider(_mainViewModel, _fileSystem);
        return new QuickOpenViewModel(provider, uiDispatcher: a => a());
    }

    [Fact]
    public void ModeSwitching_TypingChevron_SwitchesToCommandMode_AndReverts()
    {
        var vm = CreateViewModel();

        vm.Open();
        Assert.Equal(QuickOpenMode.Places, vm.Mode);

        vm.QueryText = ">";
        Assert.Equal(QuickOpenMode.Commands, vm.Mode);
        Assert.Equal(QuickOpenViewModel.CommandPlaceholder, vm.PlaceholderText);

        vm.QueryText = "doc";
        Assert.Equal(QuickOpenMode.Places, vm.Mode);
        Assert.Equal(QuickOpenViewModel.DefaultPlaceholder, vm.PlaceholderText);
    }

    [Fact]
    public void CommandCatalog_AllIdsAreUnique_AndNonEmpty()
    {
        var catalog = new CommandCatalog(_mainViewModel);
        var commands = catalog.GetCommands();

        Assert.NotEmpty(commands);

        var idSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cmd in commands)
        {
            Assert.False(string.IsNullOrWhiteSpace(cmd.Id), "Command ID must not be empty");
            Assert.False(string.IsNullOrWhiteSpace(cmd.Title), $"Title for {cmd.Id} must not be empty");
            Assert.False(string.IsNullOrWhiteSpace(cmd.Category), $"Category for {cmd.Id} must not be empty");
            Assert.True(idSet.Add(cmd.Id), $"Duplicate command ID found: {cmd.Id}");
        }
    }

    [Fact]
    public void CommandMode_ExcludesDisabledCommands()
    {
        var vm = CreateViewModel();

        // With no selection in active pane, Copy and Delete should be disabled
        _mainViewModel.ActivePane.UpdateSelectedItems(Array.Empty<FileItemViewModel>());

        vm.Open(">");

        var copyItem = vm.Results.FirstOrDefault(r => r.Title == "Copy");
        var deleteItem = vm.Results.FirstOrDefault(r => r.Title == "Delete to Recycle Bin");

        Assert.Null(copyItem);
        Assert.Null(deleteItem);

        // Now select an item in active pane
        var testItem = new FileItemViewModel(new FM.Core.Models.FileEntry(
            "file1.txt",
            @"C:\FolderA\file1.txt",
            false,
            10,
            DateTime.UtcNow), _iconService);

        _mainViewModel.ActivePane.UpdateSelectedItems(new[] { testItem });

        vm.QueryText = ">copy";
        copyItem = vm.Results.FirstOrDefault(r => r.Title == "Copy");

        Assert.NotNull(copyItem);
    }

    [Fact]
    public void CommandMode_FuzzyRanking_RanksContiguousMatchHigher()
    {
        var vm = CreateViewModel();

        // Querying '>icon' should rank 'View: Icons' higher than loose subsequence matches
        vm.Open(">icon");

        Assert.NotEmpty(vm.Results);
        Assert.Equal("View: Icons", vm.Results[0].Title);
        Assert.Equal("View", vm.Results[0].Subtitle);
    }

    [Fact]
    public void ExecuteSelected_Command_ExecutesActionAndClosesOverlay()
    {
        var vm = CreateViewModel();
        bool initialHidden = _mainViewModel.ShowHiddenFiles;

        vm.Open(">hidden");

        var toggleCmd = vm.Results.FirstOrDefault(r => r.Title == "Toggle Hidden Files");
        Assert.NotNull(toggleCmd);

        vm.SelectedItem = toggleCmd;
        vm.ExecuteSelected();

        Assert.False(vm.IsOpen);
        Assert.NotEqual(initialHidden, _mainViewModel.ShowHiddenFiles);
    }
}
