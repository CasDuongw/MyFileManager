using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FM.Core.Operations.Transfer;

public record JournalRecordDto(
    [property: JsonPropertyName("v")] int Version,
    [property: JsonPropertyName("t")] string Type,
    [property: JsonPropertyName("id")] Guid OperationId,
    [property: JsonPropertyName("kind")] TransferKind? Kind = null,
    [property: JsonPropertyName("ts")] DateTime? Timestamp = null,
    [property: JsonPropertyName("owner")] string? OwnerPane = null,
    [property: JsonPropertyName("item")] JournalItemDto? Item = null,
    [property: JsonPropertyName("intent")] JournalIntentDto? Intent = null,
    [property: JsonPropertyName("stagingRoot")] string? StagingRoot = null);

public record JournalIntentDto(
    [property: JsonPropertyName("src")] string SourcePath,
    [property: JsonPropertyName("dst")] string? DestinationPath,
    [property: JsonPropertyName("backup")] string? PlannedBackupPath,
    [property: JsonPropertyName("temp")] string? TempPath);

public record JournalItemDto(
    [property: JsonPropertyName("kind")] TransferKind Kind,
    [property: JsonPropertyName("src")] string SourcePath,
    [property: JsonPropertyName("dst")] string? DestinationPath,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("mtime")] DateTime? Modified,
    [property: JsonPropertyName("outcome")] ItemOutcome Outcome,
    [property: JsonPropertyName("isDir")] bool IsDirectory,
    [property: JsonPropertyName("backup")] string? BackupPath);

public static class JournalCodec
{
    public const int CurrentSchemaVersion = 1;

    public static void WriteRecord(TextWriter writer, JournalRecordDto record)
    {
        string json = JsonSerializer.Serialize(record);
        writer.WriteLine(json);
    }

    public static Dictionary<Guid, PersistentUndoEntry> Replay(TextReader reader, out List<string> corruptedLines)
    {
        corruptedLines = new List<string>();
        var entries = new Dictionary<Guid, (PersistentUndoEntry Entry, List<JournalItemEntry> Items, List<JournalIntentEntry> Intents, List<string> StagingRoots)>();

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            JournalRecordDto? record;
            try
            {
                record = JsonSerializer.Deserialize<JournalRecordDto>(line);
            }
            catch (JsonException)
            {
                // If it's a torn line at the very end of stream, skip gracefully
                if (reader.Peek() == -1)
                {
                    continue;
                }

                corruptedLines.Add(line);
                continue;
            }

            if (record == null) continue;

            // Forward-compatibility: ignore future schema versions
            if (record.Version > CurrentSchemaVersion)
            {
                continue;
            }

            switch (record.Type.ToLowerInvariant())
            {
                case "begin":
                    entries[record.OperationId] = (
                        new PersistentUndoEntry(
                            record.OperationId,
                            record.Version,
                            JournalState.Pending,
                            record.Timestamp ?? DateTime.UtcNow,
                            record.Kind ?? TransferKind.Copy,
                            Array.Empty<JournalItemEntry>(),
                            record.OwnerPane,
                            Array.Empty<JournalIntentEntry>(),
                            Array.Empty<string>()),
                        new List<JournalItemEntry>(),
                        new List<JournalIntentEntry>(),
                        new List<string>());
                    break;

                case "staging_root":
                    if (entries.TryGetValue(record.OperationId, out var stagingTarget) && !string.IsNullOrEmpty(record.StagingRoot))
                    {
                        if (!stagingTarget.StagingRoots.Contains(record.StagingRoot, StringComparer.OrdinalIgnoreCase))
                        {
                            stagingTarget.StagingRoots.Add(record.StagingRoot);
                        }
                    }
                    break;

                case "intent":
                case "inprogress":
                    if (entries.TryGetValue(record.OperationId, out var intentTarget) && record.Intent != null)
                    {
                        intentTarget.Intents.Add(new JournalIntentEntry(
                            record.Intent.SourcePath,
                            record.Intent.DestinationPath,
                            record.Intent.PlannedBackupPath,
                            record.Intent.TempPath));
                    }
                    break;

                case "item":
                    if (entries.TryGetValue(record.OperationId, out var existing) && record.Item != null)
                    {
                        var itemDto = record.Item;
                        existing.Items.Add(new JournalItemEntry(
                            itemDto.Kind,
                            itemDto.SourcePath,
                            itemDto.DestinationPath,
                            itemDto.Size,
                            itemDto.Modified,
                            itemDto.Outcome,
                            itemDto.IsDirectory,
                            itemDto.BackupPath));

                        // Fulfill / resolve matching intent if present
                        existing.Intents.RemoveAll(i =>
                            string.Equals(i.SourcePath, itemDto.SourcePath, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(i.DestinationPath, itemDto.DestinationPath, StringComparison.OrdinalIgnoreCase));
                    }
                    break;

                case "commit":
                    if (entries.TryGetValue(record.OperationId, out var commitTarget))
                    {
                        commitTarget.Intents.Clear();
                        entries[record.OperationId] = (
                            commitTarget.Entry with { State = JournalState.Committed },
                            commitTarget.Items,
                            commitTarget.Intents,
                            commitTarget.StagingRoots);
                    }
                    break;

                case "undone":
                    if (entries.TryGetValue(record.OperationId, out var undoneTarget))
                    {
                        undoneTarget.Intents.Clear();
                        entries[record.OperationId] = (
                            undoneTarget.Entry with { State = JournalState.Undone },
                            undoneTarget.Items,
                            undoneTarget.Intents,
                            undoneTarget.StagingRoots);
                    }
                    break;

                case "abandon":
                    if (entries.TryGetValue(record.OperationId, out var abandonTarget))
                    {
                        abandonTarget.Intents.Clear();
                        entries[record.OperationId] = (
                            abandonTarget.Entry with { State = JournalState.Abandoned },
                            abandonTarget.Items,
                            abandonTarget.Intents,
                            abandonTarget.StagingRoots);
                    }
                    break;

                case "delete":
                    entries.Remove(record.OperationId);
                    break;
            }
        }

        var result = new Dictionary<Guid, PersistentUndoEntry>();
        foreach (var kvp in entries)
        {
            result[kvp.Key] = kvp.Value.Entry with
            {
                Items = kvp.Value.Items,
                Intents = kvp.Value.Intents,
                StagingRoots = kvp.Value.StagingRoots
            };
        }
        return result;
    }

    public static List<PersistentUndoEntry> FilterForCompaction(
        IEnumerable<PersistentUndoEntry> entries,
        TimeSpan maxAge,
        int maxEntries,
        DateTime now)
    {
        return entries
            .Where(e => e.State == JournalState.Committed)
            .Where(e => now - e.Timestamp <= maxAge)
            .OrderByDescending(e => e.Timestamp)
            .Take(maxEntries)
            .OrderBy(e => e.Timestamp)
            .ToList();
    }
}
