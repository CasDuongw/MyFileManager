using FM.Core.RecycleBin;
using Xunit;

namespace FM.Core.Tests;

public class RecycleBinServiceTests
{
    [Fact]
    public async Task GetItemsAsync_ReturnsDeletedItems()
    {
        var fake = new FakeRecycleBinService(new[]
        {
            new RecycleBinItem("id1", "file1.txt", @"C:\Users\test\Desktop", DateTime.UtcNow, 1024, "Text Document", false)
        });

        var items = await fake.GetItemsAsync();
        Assert.Single(items);
        Assert.Equal("file1.txt", items[0].Name);
        Assert.Equal(@"C:\Users\test\Desktop", items[0].OriginalLocation);
    }

    [Fact]
    public async Task RestoreItemAsync_RemovesFromBinAndRecordsAction()
    {
        var item = new RecycleBinItem("id1", "file1.txt", @"C:\Users\test\Desktop", DateTime.UtcNow, 1024, "Text Document", false);
        var fake = new FakeRecycleBinService(new[] { item });

        bool eventFired = false;
        fake.RecycleBinChanged += (s, e) => eventFired = true;

        bool success = await fake.RestoreItemAsync("id1");

        Assert.True(success);
        Assert.True(eventFired);
        Assert.Contains("id1", fake.RestoredItemIds);

        var remaining = await fake.GetItemsAsync();
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task PermanentlyDeleteItemAsync_RemovesFromBin()
    {
        var item = new RecycleBinItem("id2", "file2.txt", @"C:\Users\test\Desktop", DateTime.UtcNow, 2048, "Text Document", false);
        var fake = new FakeRecycleBinService(new[] { item });

        bool success = await fake.PermanentlyDeleteItemAsync("id2");

        Assert.True(success);
        Assert.Contains("id2", fake.DeletedItemIds);
        Assert.Empty(await fake.GetItemsAsync());
    }

    [Fact]
    public async Task EmptyRecycleBinAsync_ClearsAllItems()
    {
        var fake = new FakeRecycleBinService(new[]
        {
            new RecycleBinItem("id1", "file1.txt", @"C:\loc", DateTime.UtcNow, 100, "File", false),
            new RecycleBinItem("id2", "file2.txt", @"C:\loc", DateTime.UtcNow, 200, "File", false)
        });

        bool success = await fake.EmptyRecycleBinAsync();

        Assert.True(success);
        Assert.Equal(1, fake.EmptyCount);
        Assert.Empty(await fake.GetItemsAsync());
    }
}

