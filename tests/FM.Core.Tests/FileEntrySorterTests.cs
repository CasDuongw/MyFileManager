using FM.Core.Helpers;
using FM.Core.Models;

namespace FM.Core.Tests;

public class FileEntrySorterTests
{
    private readonly List<FileEntry> _sampleEntries = new()
    {
        new FileEntry("zebra.txt", @"C:\zebra.txt", IsDirectory: false, Size: 1000, Modified: DateTime.Now.AddDays(-1)),
        new FileEntry("alpha_folder", @"C:\alpha_folder", IsDirectory: true, Size: 0, Modified: DateTime.Now.AddDays(-5)),
        new FileEntry("beta_folder", @"C:\beta_folder", IsDirectory: true, Size: 0, Modified: DateTime.Now.AddDays(-2)),
        new FileEntry("apple.txt", @"C:\apple.txt", IsDirectory: false, Size: 50, Modified: DateTime.Now),
        new FileEntry("doc.pdf", @"C:\doc.pdf", IsDirectory: false, Size: 500, Modified: DateTime.Now.AddDays(-10))
    };

    [Fact]
    public void OrderEntries_NameAscending_PutsFoldersFirstThenAlphabetical()
    {
        var sorted = _sampleEntries.OrderEntries(FileSortColumn.Name, ascending: true).ToList();

        // Folders come first
        Assert.True(sorted[0].IsDirectory);
        Assert.True(sorted[1].IsDirectory);
        Assert.Equal("alpha_folder", sorted[0].Name);
        Assert.Equal("beta_folder", sorted[1].Name);

        // Files come after folders
        Assert.False(sorted[2].IsDirectory);
        Assert.False(sorted[3].IsDirectory);
        Assert.False(sorted[4].IsDirectory);
        Assert.Equal("apple.txt", sorted[2].Name);
        Assert.Equal("doc.pdf", sorted[3].Name);
        Assert.Equal("zebra.txt", sorted[4].Name);
    }

    [Fact]
    public void OrderEntries_NameDescending_PutsFoldersFirstThenReverseAlphabetical()
    {
        var sorted = _sampleEntries.OrderEntries(FileSortColumn.Name, ascending: false).ToList();

        // Folders STILL come first
        Assert.True(sorted[0].IsDirectory);
        Assert.True(sorted[1].IsDirectory);
        Assert.Equal("beta_folder", sorted[0].Name);
        Assert.Equal("alpha_folder", sorted[1].Name);

        // Files come after
        Assert.Equal("zebra.txt", sorted[2].Name);
        Assert.Equal("doc.pdf", sorted[3].Name);
        Assert.Equal("apple.txt", sorted[4].Name);
    }

    [Fact]
    public void OrderEntries_SizeAscending_PutsFoldersFirstThenBySize()
    {
        var sorted = _sampleEntries.OrderEntries(FileSortColumn.Size, ascending: true).ToList();

        Assert.True(sorted[0].IsDirectory);
        Assert.True(sorted[1].IsDirectory);

        Assert.Equal("apple.txt", sorted[2].Name); // 50 bytes
        Assert.Equal("doc.pdf", sorted[3].Name);   // 500 bytes
        Assert.Equal("zebra.txt", sorted[4].Name); // 1000 bytes
    }
}
