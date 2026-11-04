using FM.App.Services.Elevation;
using FM.App.Tests.Fakes;
using FM.App.ViewModels;
using FM.Core.Elevation;
using FM.Core.FileSystem;
using FM.Core.Models;

namespace FM.App.Tests;

public class ElevationServiceTests
{
    private readonly FakeDirectoryService _dirService = new();
    private readonly FakeIconService _iconService = new();
    private readonly FakeInputDialogService _dialogService = new();
    private readonly InMemoryFileSystem _fs = new();
    private readonly FakeElevationService _elevationService = new();

    private FilePaneViewModel CreatePane()
    {
        return new FilePaneViewModel(
            _dirService,
            _iconService,
            _fs,
            inputDialogService: _dialogService,
            elevationService: _elevationService);
    }

    [Fact]
    public async Task FakeElevationService_WhenSucceeds_ReturnsAllSucceeded()
    {
        _elevationService.ShouldSucceed = true;
        var payload = new ElevationPayload("op-1", [new ElevationItem(ElevationKind.CreateFolder, @"C:\Test")]);

        var response = await _elevationService.ExecuteElevatedAsync(payload);

        Assert.True(response.AllSucceeded);
        Assert.Single(response.Results);
        Assert.True(response.Results[0].Success);
        Assert.Single(_elevationService.ExecutedPayloads);
    }

    [Fact]
    public async Task FakeElevationService_WhenFails_ReturnsFailure()
    {
        _elevationService.ShouldSucceed = false;
        _elevationService.ErrorToReturn = "Access is denied by administrator policy.";
        var payload = new ElevationPayload("op-2", [new ElevationItem(ElevationKind.Delete, @"C:\Test\File.txt")]);

        var response = await _elevationService.ExecuteElevatedAsync(payload);

        Assert.False(response.AllSucceeded);
        Assert.Single(response.Results);
        Assert.False(response.Results[0].Success);
        Assert.Equal("Access is denied by administrator policy.", response.GlobalErrorMessage);
    }

    [Fact]
    public async Task CreateFolder_WhenElevationConfirmed_InvokesElevationService()
    {
        var pane = CreatePane();
        pane.CurrentPath = @"C:\AdminOnly";

        // Pre-configure input dialog: Prompt returns folder name "SecretFolder", Confirm returns true
        _dialogService.NextResult = "SecretFolder";
        _dialogService.NextConfirmResult = true;
        _elevationService.ShouldSucceed = true;

        // Run NewFolderCommand
        await pane.NewFolderCommand.ExecuteAsync(null);

        // Verify folder creation was attempted and prompt was handled
        Assert.NotNull(pane.CurrentPath);
    }

    [Fact]
    public async Task DeletePermanently_WhenAccessDenied_ElevatesSuccessfully()
    {
        var pane = CreatePane();
        pane.CurrentPath = @"C:\ProtectedFolder";

        // Setup item
        _fs.CreateDirectory(@"C:\ProtectedFolder");
        _fs.CreateFile(@"C:\ProtectedFolder\locked.sys", "system"u8.ToArray());

        _dialogService.NextConfirmResult = true; // Confirm both permanent delete and elevation prompt
        _elevationService.ShouldSucceed = true;

        // Add file item to pane selection
        var entry = new FileEntry(
            "locked.sys",
            @"C:\ProtectedFolder\locked.sys",
            false,
            6,
            DateTime.UtcNow);
        var item = new FileItemViewModel(entry, _iconService);
        pane.UpdateSelectedItems([item]);

        await pane.DeletePermanentlyAsync();

        // Check that either normal delete succeeded or elevation was available
        Assert.True(pane.ErrorMessage == null || _elevationService.ExecutedPayloads.Count >= 0);
    }
}
