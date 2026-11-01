using FM.Core.Search;
using Xunit;

namespace FM.Core.Tests;

public class RecentFolderListTests
{
    [Fact]
    public void Add_NormalizesPaths_StripsTrailingSlashesExceptDriveRoots()
    {
        var list = new RecentFolderList();

        list.Add(@"C:\Users\PC\Documents\");
        list.Add(@"D:\");
        list.Add(@"C:\Projects\FM");

        var items = list.Items;

        Assert.Equal(3, items.Count);
        Assert.Equal(@"C:\Projects\FM", items[0]);
        Assert.Equal(@"D:\", items[1]);
        Assert.Equal(@"C:\Users\PC\Documents", items[2]);
    }

    [Fact]
    public void Add_DeduplicatesCaseInsensitive_MovesToFront()
    {
        var list = new RecentFolderList();

        list.Add(@"C:\FolderA");
        list.Add(@"C:\FolderB");
        list.Add(@"C:\foldera\"); // Same folder with different casing and trailing slash

        var items = list.Items;

        Assert.Equal(2, items.Count);
        Assert.Equal(@"C:\FolderA", items[0]); // Moved to front
        Assert.Equal(@"C:\FolderB", items[1]);
    }

    [Fact]
    public void Add_EnforcesCapacityCapOfTen()
    {
        var list = new RecentFolderList(capacity: 10);

        for (int i = 0; i < 15; i++)
        {
            list.Add($@"C:\Folder_{i:D2}");
        }

        var items = list.Items;

        Assert.Equal(10, items.Count);
        Assert.Equal(@"C:\Folder_14", items[0]); // Most recent
        Assert.Equal(@"C:\Folder_05", items[9]); // 10th item
    }

    [Fact]
    public void Add_IgnoresNullOrWhitespace()
    {
        var list = new RecentFolderList();

        list.Add(null);
        list.Add("");
        list.Add("   ");

        Assert.Empty(list.Items);
    }
}

