using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FM.Core.FileSystem;
using FM.Core.Search;
using Xunit;

namespace FM.Core.Tests;

public class FolderIndexTests
{
    [Fact]
    public void FolderIndex_AddRemoveRename_MaintainsCorrectSnapshot()
    {
        var index = new FolderIndex();
        Assert.Equal(0, index.Count);
        Assert.Equal("Index idle", index.StatusMessage);

        index.Add(@"C:\Users\User\Projects\MyApp");
        Assert.Equal(1, index.Count);
        Assert.Equal("Indexed 1 folders", index.StatusMessage);
        Assert.Equal("MyApp", index.Snapshot()[0].Name);

        index.Rename(@"C:\Users\User\Projects\MyApp", @"C:\Users\User\Projects\MyNewApp");
        Assert.Equal(1, index.Count);
        Assert.Equal("MyNewApp", index.Snapshot()[0].Name);

        index.Remove(@"C:\Users\User\Projects\MyNewApp");
        Assert.Equal(0, index.Count);
    }

    [Fact]
    public void FolderIndex_Search_ReturnsMatchedResultsRanked()
    {
        var index = new FolderIndex();
        index.BulkAdd(new[]
        {
            @"C:\Users\User\Desktop\FPT_student\DU_AN_CA_NHAN",
            @"C:\Users\User\Desktop\FPT_student\ky_1",
            @"C:\Users\User\Desktop\FPT_student\sinh viên",
            @"D:\HoYoPlay\games",
            @"D:\TeraBoxDownload",
            @"D:\Cisco Packet Tracer 9.0.1"
        });

        Assert.Equal(6, index.Count);

        // 1. games -> finds D:\HoYoPlay\games
        var qGames = PreparedQuery.Create("games");
        var resGames = index.Search(qGames, 10);
        Assert.NotEmpty(resGames);
        Assert.Equal("games", resGames[0].Item.Name);

        // 2. tera -> finds D:\TeraBoxDownload
        var qTera = PreparedQuery.Create("tera");
        var resTera = index.Search(qTera, 10);
        Assert.NotEmpty(resTera);
        Assert.Equal("TeraBoxDownload", resTera[0].Item.Name);

        // 3. cisco -> finds Cisco Packet Tracer 9.0.1
        var qCisco = PreparedQuery.Create("cisco");
        var resCisco = index.Search(qCisco, 10);
        Assert.NotEmpty(resCisco);
        Assert.Equal("Cisco Packet Tracer 9.0.1", resCisco[0].Item.Name);
    }

    [Fact]
    public async Task FolderCrawler_CrawlsIteratively_RespectsExcludesAndReparsePoints()
    {
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory(@"C:\Users\User\Projects");
        fs.CreateDirectory(@"C:\Users\User\Projects\ProjectA");
        fs.CreateDirectory(@"C:\Users\User\Projects\ProjectA\src");
        fs.CreateDirectory(@"C:\Users\User\Projects\ProjectA\node_modules"); // Should be excluded!
        fs.CreateDirectory(@"C:\Users\User\Projects\ProjectA\.git");         // Should be excluded!
        fs.CreateDirectory(@"C:\Windows\System32");                         // Should be excluded!

        var index = new FolderIndex();
        var crawler = new FolderCrawler(fs, index);

        await crawler.CrawlAsync(new[] { @"C:\Users\User\Projects", @"C:\Windows" }, batchSize: 2);

        var snapshot = index.Snapshot();
        Assert.Contains(snapshot, e => e.Name == "ProjectA");
        Assert.Contains(snapshot, e => e.Name == "src");
        Assert.DoesNotContain(snapshot, e => e.Name == "node_modules");
        Assert.DoesNotContain(snapshot, e => e.Name == ".git");
        Assert.DoesNotContain(snapshot, e => e.Name == "System32");
        Assert.Equal(FolderIndexState.Ready, index.State);
    }

    [Fact]
    public void FolderIndexCache_SaveAndLoad_RoundTrip()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"fm_idx_test_{Guid.NewGuid():N}.idx");
        try
        {
            var entries = new List<FolderIndexEntry>
            {
                FolderIndexEntry.Create(@"C:\Users\User\Documents"),
                FolderIndexEntry.Create(@"D:\HoYoPlay\games"),
                FolderIndexEntry.Create(@"C:\Users\User\Desktop\FPT_student\DU_AN_CA_NHAN")
            };

            FolderIndexCache.Save(tempFile, entries);
            Assert.True(File.Exists(tempFile));

            bool success = FolderIndexCache.TryLoad(tempFile, out var loaded);
            Assert.True(success);
            Assert.Equal(3, loaded.Count);
            Assert.Equal("Documents", loaded[0].Name);
            Assert.Equal("games", loaded[1].Name);
            Assert.Equal("DU_AN_CA_NHAN", loaded[2].Name);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void FolderIndexCache_CorruptedFile_ReturnsFalseWithoutThrowing()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"fm_idx_corrupt_{Guid.NewGuid():N}.idx");
        try
        {
            File.WriteAllBytes(tempFile, new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 });

            bool success = FolderIndexCache.TryLoad(tempFile, out var loaded);
            Assert.False(success);
            Assert.Empty(loaded);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
