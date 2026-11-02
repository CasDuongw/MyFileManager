using System.IO;
using FM.App.Converters;
using FM.App.Tests.Fakes;
using FM.App.ViewModels;
using FM.Core.FileSystem;
using FM.Core.Models;
using FM.Core.Search;
using Xunit;

namespace FM.App.Tests;

public class M4FindSearchTests
{
    private readonly InMemoryFileSystem _fileSystem;
    private readonly FakeDirectoryService _fakeDirService;
    private readonly FakeIconService _fakeIconService;
    private readonly SearchService _searchService;

    public M4FindSearchTests()
    {
        _fileSystem = new InMemoryFileSystem();
        _fakeDirService = new FakeDirectoryService();
        _fakeIconService = new FakeIconService();
        _searchService = new SearchService(_fileSystem);
    }

    private FilePaneViewModel CreatePane()
    {
        return new FilePaneViewModel(
            _fakeDirService,
            _fakeIconService,
            _fileSystem,
            searchService: _searchService);
    }

    [Fact]
    public void CtrlF_OpensFindBar_AndSetsProperties()
    {
        var pane = CreatePane();
        bool openedFired = false;
        pane.FindBarOpened += () => openedFired = true;

        pane.OpenFindBar();

        Assert.True(pane.IsFindBarOpen);
        Assert.True(openedFired);

        pane.CloseFindBar();

        Assert.False(pane.IsFindBarOpen);
        Assert.False(pane.IsSearchMode);
    }

    [Fact]
    public async Task Search_FindsDescendants_StreamsResults()
    {
        _fileSystem.CreateDirectory(@"C:\Project");
        _fileSystem.CreateDirectory(@"C:\Project\SubA");
        _fileSystem.CreateFile(@"C:\Project\doc1.txt");
        _fileSystem.CreateFile(@"C:\Project\SubA\doc2.txt");
        _fileSystem.CreateFile(@"C:\Project\image.png");

        var pane = CreatePane();
        pane.CurrentPath = @"C:\Project";

        pane.SearchQueryText = "doc";
        await pane.PerformSearchAsync("doc");

        Assert.True(pane.IsSearchMode);
        Assert.Equal(2, pane.Items.Count);
        Assert.Contains(pane.Items, i => i.Name == "doc1.txt" && i.ParentDirectory == @"C:\Project");
        Assert.Contains(pane.Items, i => i.Name == "doc2.txt" && i.ParentDirectory == @"C:\Project\SubA");
        Assert.Contains("2 items", pane.SearchResultCountText);
    }

    [Fact]
    public void LocationColumn_ParentDirectory_And_BoolToWidthConverter()
    {
        var item = new FileItemViewModel(
            new FileEntry("sample.txt", @"C:\Root\SubFolder\sample.txt", false, 1024, DateTime.Now),
            null);

        Assert.Equal(@"C:\Root\SubFolder", item.ParentDirectory);

        var conv = new BoolToWidthConverter();
        object zero = conv.Convert(false, typeof(double), "220", System.Globalization.CultureInfo.InvariantCulture);
        object width = conv.Convert(true, typeof(double), "220", System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(0.0, (double)zero);
        Assert.Equal(220.0, (double)width);
    }

    [Fact]
    public async Task Search_EscKey_ClosesFindBar_AndRestoresFolderItems()
    {
        _fileSystem.CreateDirectory(@"C:\Project");
        _fileSystem.CreateFile(@"C:\Project\fileA.txt");
        _fileSystem.CreateFile(@"C:\Project\fileB.txt");

        var pane = CreatePane();
        pane.CurrentPath = @"C:\Project";
        // Simulate normal folder items
        pane.SafeReplaceItems(new[]
        {
            new FileItemViewModel(new FileEntry("fileA.txt", @"C:\Project\fileA.txt", false, 10, DateTime.Now), null),
            new FileItemViewModel(new FileEntry("fileB.txt", @"C:\Project\fileB.txt", false, 10, DateTime.Now), null)
        });

        Assert.Equal(2, pane.Items.Count);

        pane.OpenFindBar();
        pane.SearchQueryText = "fileA";
        await pane.PerformSearchAsync("fileA");

        Assert.True(pane.IsSearchMode);
        Assert.Single(pane.Items);
        Assert.Equal("fileA.txt", pane.Items[0].Name);

        // Close Find Bar (simulating Esc key)
        pane.CloseFindBar();

        Assert.False(pane.IsSearchMode);
        Assert.False(pane.IsFindBarOpen);
        Assert.Equal(2, pane.Items.Count);
        Assert.Contains(pane.Items, i => i.Name == "fileA.txt");
        Assert.Contains(pane.Items, i => i.Name == "fileB.txt");
    }

    [Fact]
    public async Task SearchMode_NewFolderAndPaste_AreDisabledWithStatusToast()
    {
        var pane = CreatePane();
        pane.IsSearchMode = true;

        await pane.NewFolderAsync();
        Assert.Contains("Không thể tạo thư mục trong kết quả tìm kiếm", pane.SelectionStatusText);

        await pane.PasteAsync();
        Assert.Contains("Không thể dán tệp vào kết quả tìm kiếm", pane.SelectionStatusText);
    }

    [Fact]
    public async Task SearchMode_ShowInContainingFolder_NavigatesParentAndSelects()
    {
        _fileSystem.CreateDirectory(@"C:\Project");
        _fileSystem.CreateDirectory(@"C:\Project\Deep");
        _fileSystem.CreateFile(@"C:\Project\Deep\target.txt");

        _fakeDirService.AddDirectory(@"C:\Project\Deep", new[]
        {
            new FileEntry("target.txt", @"C:\Project\Deep\target.txt", false, 100, DateTime.Now)
        });

        var pane = CreatePane();
        pane.CurrentPath = @"C:\Project";
        pane.IsSearchMode = true;

        var targetItem = new FileItemViewModel(
            new FileEntry("target.txt", @"C:\Project\Deep\target.txt", false, 100, DateTime.Now),
            null);
        pane.SafeReplaceItems(new[] { targetItem });
        pane.SelectedItem = targetItem;

        await pane.ShowInContainingFolderAsync();

        Assert.False(pane.IsSearchMode);
        Assert.False(pane.IsFindBarOpen);
        Assert.Equal(@"C:\Project\Deep", pane.CurrentPath);
        Assert.NotNull(pane.SelectedItem);
        Assert.Equal("target.txt", pane.SelectedItem?.Name);
    }

    [Fact]
    public async Task Search_ScopeOpenPanes_CombinesAndDeduplicatesRoots()
    {
        _fileSystem.CreateDirectory(@"C:\Root1");
        _fileSystem.CreateDirectory(@"C:\Root1\Nested");
        _fileSystem.CreateDirectory(@"C:\Root2");

        _fileSystem.CreateFile(@"C:\Root1\match1.log");
        _fileSystem.CreateFile(@"C:\Root1\Nested\match2.log");
        _fileSystem.CreateFile(@"C:\Root2\match3.log");

        var pane = CreatePane();
        pane.CurrentPath = @"C:\Root1";
        pane.GetOpenPanePaths = () => new List<string> { @"C:\Root1", @"C:\Root1\Nested", @"C:\Root2" };

        pane.SelectedSearchScope = SearchScope.OpenPanes;
        await pane.PerformSearchAsync("match");

        Assert.Equal(3, pane.Items.Count);
        Assert.Contains(pane.Items, i => i.Name == "match1.log");
        Assert.Contains(pane.Items, i => i.Name == "match2.log");
        Assert.Contains(pane.Items, i => i.Name == "match3.log");
    }

    [Fact]
    public async Task Search_OperatorExtAndSize_FiltersCorrectly()
    {
        _fileSystem.CreateDirectory(@"C:\Store");
        _fileSystem.CreateVirtualFile(@"C:\Store\small.pdf", 100);
        _fileSystem.CreateVirtualFile(@"C:\Store\large.pdf", 2048);
        _fileSystem.CreateVirtualFile(@"C:\Store\large.txt", 2048);

        var pane = CreatePane();
        pane.CurrentPath = @"C:\Store";

        await pane.PerformSearchAsync("ext:pdf size:>1kb");

        Assert.Single(pane.Items);
        Assert.Equal("large.pdf", pane.Items[0].Name);
    }

    [Fact]
    public async Task Search_UnknownOperator_BecomesLiteralWithWarning()
    {
        _fileSystem.CreateDirectory(@"C:\Test");
        _fileSystem.CreateFile(@"C:\Test\foo_bar.txt");

        var pane = CreatePane();
        pane.CurrentPath = @"C:\Test";

        await pane.PerformSearchAsync("unknown:bar");

        Assert.NotNull(pane.SearchWarningText);
        Assert.Contains("không nhận diện", pane.SearchWarningText);
    }
}
