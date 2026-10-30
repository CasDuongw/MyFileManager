using System.IO;
using System.Text;
using FM.Core.Operations.Transfer;

namespace FM.Core.Tests;

public class JournalCodecTests
{
    [Fact]
    public void Replay_BeginItemCommit_ProducesCommittedEntry()
    {
        var opId = Guid.NewGuid();
        var sb = new StringBuilder();
        sb.AppendLine($"{{\"v\":1,\"t\":\"begin\",\"id\":\"{opId}\",\"kind\":0,\"ts\":\"2026-01-01T00:00:00Z\",\"owner\":\"Left\"}}");
        sb.AppendLine($"{{\"v\":1,\"t\":\"item\",\"id\":\"{opId}\",\"item\":{{\"kind\":0,\"src\":\"C:\\\\a.txt\",\"dst\":\"D:\\\\a.txt\",\"size\":100,\"mtime\":\"2026-01-01T00:00:00Z\",\"outcome\":0,\"isDir\":false,\"backup\":null}}}}");
        sb.AppendLine($"{{\"v\":1,\"t\":\"commit\",\"id\":\"{opId}\"}}");

        using var reader = new StringReader(sb.ToString());
        var entries = JournalCodec.Replay(reader, out var corrupted);

        Assert.Empty(corrupted);
        var entry = Assert.Contains(opId, entries);
        Assert.Equal(JournalState.Committed, entry.State);
        Assert.Equal("Left", entry.OwnerPane);
        var item = Assert.Single(entry.Items);
        Assert.Equal(@"C:\a.txt", item.SourcePath);
        Assert.Equal(@"D:\a.txt", item.DestinationPath);
        Assert.Equal(100, item.Size);
    }

    [Fact]
    public void Replay_MarkUndone_ProducesUndoneState()
    {
        var opId = Guid.NewGuid();
        var sb = new StringBuilder();
        sb.AppendLine($"{{\"v\":1,\"t\":\"begin\",\"id\":\"{opId}\",\"kind\":0,\"ts\":\"2026-01-01T00:00:00Z\"}}");
        sb.AppendLine($"{{\"v\":1,\"t\":\"commit\",\"id\":\"{opId}\"}}");
        sb.AppendLine($"{{\"v\":1,\"t\":\"undone\",\"id\":\"{opId}\"}}");

        using var reader = new StringReader(sb.ToString());
        var entries = JournalCodec.Replay(reader, out var corrupted);

        Assert.Empty(corrupted);
        var entry = Assert.Contains(opId, entries);
        Assert.Equal(JournalState.Undone, entry.State);
    }

    [Fact]
    public void Replay_Delete_RemovesEntryEntirely()
    {
        var opId = Guid.NewGuid();
        var sb = new StringBuilder();
        sb.AppendLine($"{{\"v\":1,\"t\":\"begin\",\"id\":\"{opId}\",\"kind\":0,\"ts\":\"2026-01-01T00:00:00Z\"}}");
        sb.AppendLine($"{{\"v\":1,\"t\":\"delete\",\"id\":\"{opId}\"}}");

        using var reader = new StringReader(sb.ToString());
        var entries = JournalCodec.Replay(reader, out var corrupted);

        Assert.Empty(corrupted);
        Assert.DoesNotContain(opId, entries.Keys);
    }

    [Fact]
    public void Replay_HigherVersion_IsIgnoredGracefully()
    {
        var opId = Guid.NewGuid();
        var sb = new StringBuilder();
        sb.AppendLine($"{{\"v\":99,\"t\":\"begin\",\"id\":\"{opId}\",\"kind\":0}}");

        using var reader = new StringReader(sb.ToString());
        var entries = JournalCodec.Replay(reader, out var corrupted);

        Assert.Empty(corrupted);
        Assert.DoesNotContain(opId, entries.Keys);
    }

    [Fact]
    public void Replay_TornLineAtTail_IsSkippedWithoutCorruptFlag()
    {
        var opId = Guid.NewGuid();
        var sb = new StringBuilder();
        sb.AppendLine($"{{\"v\":1,\"t\":\"begin\",\"id\":\"{opId}\",\"kind\":0,\"ts\":\"2026-01-01T00:00:00Z\"}}");
        sb.Append("{\"v\":1,\"t\":\"it"); // Incomplete torn line at tail

        using var reader = new StringReader(sb.ToString());
        var entries = JournalCodec.Replay(reader, out var corrupted);

        Assert.Empty(corrupted);
        Assert.Contains(opId, entries.Keys);
    }

    [Fact]
    public void Replay_CorruptedLineInMiddle_IsReportedInCorruptedList()
    {
        var opId1 = Guid.NewGuid();
        var opId2 = Guid.NewGuid();
        var sb = new StringBuilder();
        sb.AppendLine($"{{\"v\":1,\"t\":\"begin\",\"id\":\"{opId1}\",\"kind\":0}}");
        sb.AppendLine("corrupted line that is not json");
        sb.AppendLine($"{{\"v\":1,\"t\":\"begin\",\"id\":\"{opId2}\",\"kind\":0}}");

        using var reader = new StringReader(sb.ToString());
        var entries = JournalCodec.Replay(reader, out var corrupted);

        Assert.Single(corrupted);
        Assert.Equal("corrupted line that is not json", corrupted[0]);
        Assert.Contains(opId1, entries.Keys);
        Assert.Contains(opId2, entries.Keys);
    }

    [Fact]
    public void FilterForCompaction_RetainsOnlyCommittedWithinAgeAndMaxEntries()
    {
        var now = DateTime.UtcNow;
        var opOld = new PersistentUndoEntry(Guid.NewGuid(), 1, JournalState.Committed, now.AddDays(-10), TransferKind.Copy, Array.Empty<JournalItemEntry>());
        var opUndone = new PersistentUndoEntry(Guid.NewGuid(), 1, JournalState.Undone, now.AddDays(-1), TransferKind.Copy, Array.Empty<JournalItemEntry>());
        var opValid1 = new PersistentUndoEntry(Guid.NewGuid(), 1, JournalState.Committed, now.AddDays(-2), TransferKind.Copy, Array.Empty<JournalItemEntry>());
        var opValid2 = new PersistentUndoEntry(Guid.NewGuid(), 1, JournalState.Committed, now.AddHours(-1), TransferKind.Copy, Array.Empty<JournalItemEntry>());

        var all = new[] { opOld, opUndone, opValid1, opValid2 };
        var compacted = JournalCodec.FilterForCompaction(all, TimeSpan.FromDays(7), maxEntries: 50, now);

        Assert.Equal(2, compacted.Count);
        Assert.Contains(compacted, e => e.OperationId == opValid1.OperationId);
        Assert.Contains(compacted, e => e.OperationId == opValid2.OperationId);
        Assert.DoesNotContain(compacted, e => e.OperationId == opOld.OperationId);
        Assert.DoesNotContain(compacted, e => e.OperationId == opUndone.OperationId);
    }
}
