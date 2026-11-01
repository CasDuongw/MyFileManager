using System.IO;
using FM.App.Services;
using FM.App.Tests.Fakes;
using FM.App.ViewModels;
using FM.Core.FileSystem;
using FM.Core.Models;
using Xunit;

namespace FM.App.Tests;

public class FilterAndTypeToSelectTests
{
    private const string TestFolder = @"C:\MockSearchFolder";
    private readonly FakeDirectoryService _fakeDirService;
    private readonly FakeIconService _fakeIconService;
    private readonly InMemoryFileSystem _fileSystem;

    public FilterAndTypeToSelectTests()
    {
        _fakeDirService = new FakeDirectoryService();
        _fakeIconService = new FakeIconService();
        _fileSystem = new InMemoryFileSystem();
    }

    [Fact]
    public async Task Filter_Shallow_DiacriticAndCaseInsensitive()
    {
        var vm = new FilePaneViewModel(_fakeDirService, _fakeIconService, _fileSystem);

        _fakeDirService.AddDirectory(TestFolder, new[]
        {
            new FileEntry("tài liệu quan trọng.txt", Path.Combine(TestFolder, "tài liệu quan trọng.txt"), false, 100, DateTime.Now),
            new FileEntry("Tài Liệu Báo Cáo.docx", Path.Combine(TestFolder, "Tài Liệu Báo Cáo.docx"), false, 200, DateTime.Now),
            new FileEntry("other_file.pdf", Path.Combine(TestFolder, "other_file.pdf"), false, 300, DateTime.Now)
        });

        await vm.NavigateInternalAsync(TestFolder, NavigationPhaseType.New);

        Assert.Equal(3, vm.Items.Count);
        Assert.Equal("3 items", vm.SelectionStatusText);

        // Apply diacritic-free filter: "tai lieu"
        await vm.ApplyFilterImmediateAsync("tai lieu");

        Assert.Equal(2, vm.Items.Count);
        Assert.All(vm.Items, item => Assert.Contains("tài liệu", item.Name, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("2 of 3 items", vm.SelectionStatusText);
    }

    [Fact]
    public async Task Filter_MultiTokenWhitespace_ANDLogic()
    {
        var vm = new FilePaneViewModel(_fakeDirService, _fakeIconService, _fileSystem);

        _fakeDirService.AddDirectory(TestFolder, new[]
        {
            new FileEntry("tài liệu quan trọng.txt", Path.Combine(TestFolder, "tài liệu quan trọng.txt"), false, 100, DateTime.Now),
            new FileEntry("Tài Liệu Báo Cáo.docx", Path.Combine(TestFolder, "Tài Liệu Báo Cáo.docx"), false, 200, DateTime.Now),
            new FileEntry("other_file.pdf", Path.Combine(TestFolder, "other_file.pdf"), false, 300, DateTime.Now)
        });

        await vm.NavigateInternalAsync(TestFolder, NavigationPhaseType.New);

        // Multi-token: "bao cao tai" matches only "Tài Liệu Báo Cáo.docx"
        await vm.ApplyFilterImmediateAsync("bao cao tai");

        Assert.Single(vm.Items);
        Assert.Equal("Tài Liệu Báo Cáo.docx", vm.Items[0].Name);
        Assert.Equal("1 of 3 items", vm.SelectionStatusText);
    }

    [Fact]
    public async Task Filter_PreservesSelectionByFullPath()
    {
        var vm = new FilePaneViewModel(_fakeDirService, _fakeIconService, _fileSystem);

        var file1 = new FileEntry("doc1.txt", Path.Combine(TestFolder, "doc1.txt"), false, 100, DateTime.Now);
        var file2 = new FileEntry("doc2.txt", Path.Combine(TestFolder, "doc2.txt"), false, 200, DateTime.Now);
        var file3 = new FileEntry("other.txt", Path.Combine(TestFolder, "other.txt"), false, 300, DateTime.Now);

        _fakeDirService.AddDirectory(TestFolder, new[] { file1, file2, file3 });
        await vm.NavigateInternalAsync(TestFolder, NavigationPhaseType.New);

        // Select doc2
        var item2 = vm.Items.First(i => i.Name == "doc2.txt");
        vm.UpdateSelectedItems(new[] { item2 });
        Assert.Equal("doc2.txt", vm.SelectedItem?.Name);

        // Apply filter that matches doc1 and doc2
        await vm.ApplyFilterImmediateAsync("doc");

        Assert.Equal(2, vm.Items.Count);
        Assert.Equal("doc2.txt", vm.SelectedItem?.Name);
        Assert.Single(vm.SelectedItems);
        Assert.Equal(item2.FullPath, vm.SelectedItems[0].FullPath);
    }

    [Fact]
    public async Task Filter_NavigationClearsFilter_RefreshPreservesFilter()
    {
        var vm = new FilePaneViewModel(_fakeDirService, _fakeIconService, _fileSystem);
        string folderA = Path.Combine(TestFolder, "A");
        string folderB = Path.Combine(TestFolder, "B");

        _fakeDirService.AddDirectory(folderA, new[] { new FileEntry("testA.txt", Path.Combine(folderA, "testA.txt"), false, 10, DateTime.Now) });
        _fakeDirService.AddDirectory(folderB, new[] { new FileEntry("testB.txt", Path.Combine(folderB, "testB.txt"), false, 10, DateTime.Now) });

        await vm.NavigateInternalAsync(folderA, NavigationPhaseType.New);
        vm.FilterText = "test";
        Assert.Equal("test", vm.FilterText);

        // Navigating to new folder clears filter
        await vm.NavigateInternalAsync(folderB, NavigationPhaseType.New);
        Assert.Empty(vm.FilterText);

        // Refreshing preserves filter
        vm.FilterText = "keepme";
        await vm.NavigateInternalAsync(folderB, NavigationPhaseType.Refresh);
        Assert.Equal("keepme", vm.FilterText);
    }

    [Fact]
    public void MainViewModel_SyncsToolbarSearchTextWithActivePane()
    {
        var settingsService = new SettingsService();
        var mainVm = new MainViewModel(_fakeDirService, _fakeIconService, settingsService, _fileSystem);

        var pane1 = mainVm.ActivePane;
        mainVm.SplitVertical();
        var pane2 = mainVm.Panes.First(p => p != pane1);

        pane1.FilterText = "filter1";
        pane2.FilterText = "filter2";

        mainVm.SetActivePane(pane1);
        Assert.Equal("filter1", mainVm.SearchText);

        mainVm.SetActivePane(pane2);
        Assert.Equal("filter2", mainVm.SearchText);

        // Modifying SearchText updates ActivePane's FilterText
        mainVm.SearchText = "updatedFromToolbar";
        Assert.Equal("updatedFromToolbar", pane2.FilterText);
        // Pane 1 remains unchanged
        Assert.Equal("filter1", pane1.FilterText);
    }

    [Fact]
    public void TypeToSelect_AccumulatesBufferAndMatchesPrefix()
    {
        var vm = new FilePaneViewModel(_fakeDirService, _fakeIconService, _fileSystem);
        var item1 = new FileItemViewModel(new FileEntry("alpha.txt", @"C:\alpha.txt", false, 10, DateTime.Now));
        var item2 = new FileItemViewModel(new FileEntry("alphabet.txt", @"C:\alphabet.txt", false, 20, DateTime.Now));
        var item3 = new FileItemViewModel(new FileEntry("beta.txt", @"C:\beta.txt", false, 30, DateTime.Now));

        vm.Items.AddRange(new[] { item1, item2, item3 });

        var match1 = vm.TypeToSelect('a');
        Assert.NotNull(match1);
        Assert.Equal("alpha.txt", match1.Name);
        Assert.Equal("alpha.txt", vm.SelectedItem?.Name);

        // Continue typing 'l', 'p', 'h', 'a', 'b'
        vm.TypeToSelect('l');
        vm.TypeToSelect('p');
        vm.TypeToSelect('h');
        vm.TypeToSelect('a');
        var match2 = vm.TypeToSelect('b');

        Assert.NotNull(match2);
        Assert.Equal("alphabet.txt", match2.Name);
        Assert.Equal("alphabet.txt", vm.SelectedItem?.Name);
    }

    [Fact]
    public void TypeToSelect_TimeoutResetsBuffer()
    {
        var timeProvider = new TestTimeProvider();
        var vm = new FilePaneViewModel(_fakeDirService, _fakeIconService, _fileSystem)
        {
            TimeProvider = timeProvider
        };

        var item1 = new FileItemViewModel(new FileEntry("alpha.txt", @"C:\alpha.txt", false, 10, DateTime.Now));
        var item2 = new FileItemViewModel(new FileEntry("beta.txt", @"C:\beta.txt", false, 30, DateTime.Now));

        vm.Items.AddRange(new[] { item1, item2 });

        var match1 = vm.TypeToSelect('b');
        Assert.Equal("beta.txt", match1?.Name);

        // Advance time by 1.5 seconds -> buffer resets
        timeProvider.Advance(TimeSpan.FromSeconds(1.5));

        var match2 = vm.TypeToSelect('a');
        Assert.Equal("alpha.txt", match2?.Name);
    }

    [Fact]
    public void TypeToSelect_FallbackToContains()
    {
        var vm = new FilePaneViewModel(_fakeDirService, _fakeIconService, _fileSystem);
        var item1 = new FileItemViewModel(new FileEntry("my_special_document.pdf", @"C:\my_special_document.pdf", false, 10, DateTime.Now));
        var item2 = new FileItemViewModel(new FileEntry("other.txt", @"C:\other.txt", false, 20, DateTime.Now));

        vm.Items.AddRange(new[] { item1, item2 });

        // "spec" does not match any prefix, but matches via contains on item1
        var match = vm.TypeToSelect("spec");
        Assert.NotNull(match);
        Assert.Equal("my_special_document.pdf", match.Name);
        Assert.Equal("my_special_document.pdf", vm.SelectedItem?.Name);
    }
}
