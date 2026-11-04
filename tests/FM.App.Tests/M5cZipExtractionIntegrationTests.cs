using System.IO;
using System.IO.Compression;
using FM.App.Services;
using FM.App.Tests.Fakes;
using FM.App.ViewModels;
using FM.Core.FileSystem;
using FM.Core.Navigation;
using FM.Core.Operations.Transfer;

namespace FM.App.Tests;

public class M5cZipExtractionIntegrationTests
{
    private readonly InMemoryFileSystem _fileSystem;
    private readonly FileSystemBackedDirectoryService _dirService;
    private readonly FakeIconService _iconService;
    private readonly FakeInputDialogService _inputDialog;
    private readonly TransferQueue _transferQueue;
    private readonly FilePaneViewModel _viewModel;
    private const string MockRoot = @"C:\TestFolder";

    public M5cZipExtractionIntegrationTests()
    {
        _fileSystem = new InMemoryFileSystem();
        _fileSystem.CreateDirectory(MockRoot);

        _dirService = new FileSystemBackedDirectoryService(_fileSystem);
        _iconService = new FakeIconService();
        _inputDialog = new FakeInputDialogService();
        _transferQueue = new TransferQueue(_fileSystem);

        _viewModel = new FilePaneViewModel(
            _dirService,
            _iconService,
            _fileSystem,
            _inputDialog,
            transferQueue: _transferQueue);
    }

    private static byte[] CreateZipBytes(params (string EntryName, byte[] Content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name);
                using var entryStream = entry.Open();
                entryStream.Write(content, 0, content.Length);
            }
        }
        return ms.ToArray();
    }

    [Fact]
    public async Task FilePaneViewModel_ZipSelected_EnablesExtractCommands()
    {
        string docPath = Path.Combine(MockRoot, "document.txt");
        string zipPath = Path.Combine(MockRoot, "archive.zip");

        _fileSystem.CreateFile(docPath, "hello"u8.ToArray());
        _fileSystem.CreateFile(zipPath, CreateZipBytes(("inner.txt", "data"u8.ToArray())));

        await _viewModel.NavigateInternalAsync(MockRoot, NavigationPhaseType.New);

        var docItem = _viewModel.Items.First(i => i.Name == "document.txt");
        var zipItem = _viewModel.Items.First(i => i.Name == "archive.zip");

        // When document is selected
        _viewModel.UpdateSelectedItems(new[] { docItem });
        Assert.False(_viewModel.CanExtractZip);
        Assert.False(_viewModel.ExtractAllCommand.CanExecute(null));
        Assert.False(_viewModel.ExtractHereCommand.CanExecute(null));
        Assert.False(_viewModel.ExtractToSubfolderCommand.CanExecute(null));

        // When zip is selected
        _viewModel.UpdateSelectedItems(new[] { zipItem });
        Assert.True(_viewModel.CanExtractZip);
        Assert.True(_viewModel.ExtractAllCommand.CanExecute(null));
        Assert.True(_viewModel.ExtractHereCommand.CanExecute(null));
        Assert.True(_viewModel.ExtractToSubfolderCommand.CanExecute(null));
        Assert.Equal(@"Extract to archive\", _viewModel.ExtractToFolderText);
    }

    [Fact]
    public async Task FilePaneViewModel_ExtractHere_EnqueuesExtractTransferJob()
    {
        string zipPath = Path.Combine(MockRoot, "archive.zip");
        _fileSystem.CreateFile(zipPath, CreateZipBytes(("file1.txt", "content1"u8.ToArray())));

        TransferRequest? enqueuedRequest = null;
        _viewModel.EnqueueTransferAction = (kind, sources, dest) =>
        {
            enqueuedRequest = new TransferRequest(kind, sources, dest, new TransferOptions());
            return null;
        };

        await _viewModel.NavigateInternalAsync(MockRoot, NavigationPhaseType.New);
        var zipItem = _viewModel.Items.First(i => i.Name == "archive.zip");
        _viewModel.UpdateSelectedItems(new[] { zipItem });

        _viewModel.ExtractHereCommand.Execute(null);

        Assert.NotNull(enqueuedRequest);
        Assert.Equal(TransferKind.Extract, enqueuedRequest.Kind);
        Assert.Single(enqueuedRequest.Sources);
        Assert.Equal(zipPath, enqueuedRequest.Sources[0]);
        Assert.Equal(MockRoot, enqueuedRequest.DestinationDirectory);
    }

    [Fact]
    public async Task FilePaneViewModel_ExtractToSubfolder_EnqueuesToTargetSubfolder()
    {
        string zipPath = Path.Combine(MockRoot, "bundle.zip");
        _fileSystem.CreateFile(zipPath, CreateZipBytes(("test.txt", "abc"u8.ToArray())));

        TransferRequest? enqueuedRequest = null;
        _viewModel.EnqueueTransferAction = (kind, sources, dest) =>
        {
            enqueuedRequest = new TransferRequest(kind, sources, dest, new TransferOptions());
            return null;
        };

        await _viewModel.NavigateInternalAsync(MockRoot, NavigationPhaseType.New);
        var zipItem = _viewModel.Items.First(i => i.Name == "bundle.zip");
        _viewModel.UpdateSelectedItems(new[] { zipItem });

        _viewModel.ExtractToSubfolderCommand.Execute(null);

        Assert.NotNull(enqueuedRequest);
        Assert.Equal(TransferKind.Extract, enqueuedRequest.Kind);
        Assert.Equal(Path.Combine(MockRoot, "bundle"), enqueuedRequest.DestinationDirectory);
    }

    [Fact]
    public void TransferJobViewModel_ExtractKind_RendersExtractingTitle()
    {
        var req = new TransferRequest(
            TransferKind.Extract,
            new[] { @"C:\archives\myarchive.zip" },
            @"C:\dest",
            new TransferOptions());

        var queue = new TransferQueue(_fileSystem);
        var job = queue.Enqueue(req);
        var jobVm = new TransferJobViewModel(job);

        Assert.Equal(@"Extracting myarchive.zip → C:\dest", jobVm.Title);
    }

    [Fact]
    public async Task TransferQueue_ExtractJob_RunsThroughDrawerAndReportsProgress()
    {
        string zipPath = Path.Combine(MockRoot, "sample.zip");
        string destFolder = @"C:\ExtractedFolder";
        _fileSystem.CreateDirectory(destFolder);

        _fileSystem.CreateFile(zipPath, CreateZipBytes(
            ("alpha.txt", "AlphaContent"u8.ToArray()),
            ("beta.txt", "BetaContent"u8.ToArray())));

        var queue = new TransferQueue(_fileSystem);
        var drawerVm = new TransferDrawerViewModel(queue);

        var req = new TransferRequest(
            TransferKind.Extract,
            new[] { zipPath },
            destFolder,
            new TransferOptions());

        var job = queue.Enqueue(req);

        // Wait for extraction to complete
        var result = await job.Completion;

        Assert.Equal(TransferState.Completed, result.FinalState);
        Assert.True(_fileSystem.FileExists(Path.Combine(destFolder, "alpha.txt")));
        Assert.True(_fileSystem.FileExists(Path.Combine(destFolder, "beta.txt")));
        Assert.Contains(drawerVm.Jobs, j => j.Id == job.Id);
    }
}
