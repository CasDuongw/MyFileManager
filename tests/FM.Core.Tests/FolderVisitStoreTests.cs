using System;
using System.IO;
using System.Linq;
using FM.Core.Search;
using Xunit;

namespace FM.Core.Tests;

public class FolderVisitStoreTests
{
    [Fact]
    public void FolderVisitStore_RecordsVisits_AndComputesDecayScores()
    {
        var store = new FolderVisitStore();
        var now = DateTime.UtcNow;

        // Fresh visit (< 4h) -> 4x multiplier
        store.RecordVisit(@"C:\Users\User\Projects\App1", 2, now - TimeSpan.FromHours(1));
        double freshScore = store.GetFrecencyScore(@"C:\Users\User\Projects\App1", now);
        Assert.Equal(8.0, freshScore); // 2 * 4.0

        // 12 hours old (< 24h) -> 2x multiplier
        store.RecordVisit(@"C:\Users\User\Projects\App2", 3, now - TimeSpan.FromHours(12));
        double mediumScore = store.GetFrecencyScore(@"C:\Users\User\Projects\App2", now);
        Assert.Equal(6.0, mediumScore); // 3 * 2.0

        // 3 days old (< 7d) -> 1x multiplier
        store.RecordVisit(@"C:\Users\User\Projects\App3", 4, now - TimeSpan.FromDays(3));
        double olderScore = store.GetFrecencyScore(@"C:\Users\User\Projects\App3", now);
        Assert.Equal(4.0, olderScore); // 4 * 1.0

        // 14 days old -> 0.5x multiplier
        store.RecordVisit(@"C:\Users\User\Projects\App4", 6, now - TimeSpan.FromDays(14));
        double oldestScore = store.GetFrecencyScore(@"C:\Users\User\Projects\App4", now);
        Assert.Equal(3.0, oldestScore); // 6 * 0.5
    }

    [Fact]
    public void FolderVisitStore_TopFrecent_OrdersByFrecencyScore()
    {
        var store = new FolderVisitStore();
        var now = DateTime.UtcNow;

        store.RecordVisit(@"C:\PathA", 1, now - TimeSpan.FromHours(1)); // 4.0
        store.RecordVisit(@"C:\PathB", 5, now - TimeSpan.FromHours(1)); // 20.0
        store.RecordVisit(@"C:\PathC", 2, now - TimeSpan.FromDays(10)); // 1.0

        var top = store.GetTopFrecent(2, now);
        Assert.Equal(2, top.Count);
        Assert.Equal(@"C:\PathB", top[0]);
        Assert.Equal(@"C:\PathA", top[1]);
    }

    [Fact]
    public void FolderVisitStore_CapacityPruning_EvictsLowestScoringEntries()
    {
        var store = new FolderVisitStore(capacity: 10);
        var now = DateTime.UtcNow;

        for (int i = 0; i < 15; i++)
        {
            store.RecordVisit($@"C:\Folder_{i}", i + 1, now);
        }

        Assert.True(store.Count <= 10);
        // The highest scoring items must be preserved
        Assert.True(store.GetFrecencyScore(@"C:\Folder_14", now) > 0);
    }

    [Fact]
    public void FolderVisitStore_SaveAndLoad_RoundTripsAccurately()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"frecency_test_{Guid.NewGuid():N}.jsonl");
        try
        {
            var store1 = new FolderVisitStore();
            var now = DateTime.UtcNow;
            store1.RecordVisit(@"C:\Users\User\Documents", 5, now);
            store1.RecordVisit(@"D:\Projects\MyApp", 2, now - TimeSpan.FromHours(2));
            store1.Save(tempFile);

            var store2 = new FolderVisitStore();
            store2.Load(tempFile);

            Assert.Equal(2, store2.Count);
            Assert.True(store2.GetFrecencyScore(@"C:\Users\User\Documents", now) > 0);
            Assert.True(store2.GetFrecencyScore(@"D:\Projects\MyApp", now) > 0);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
