using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FM.Core.Search;

public record FolderVisitRecord(int VisitCount, DateTime LastVisitUtc);

/// <summary>
/// Tracks folder visits and computes frecency scores with recency decay.
/// - Visits within 4 hours: 4x weight
/// - Visits within 24 hours: 2x weight
/// - Visits within 7 days: 1x weight
/// - Older: 0.5x weight
/// Automatically prunes oldest / lowest-scoring folders when exceeding capacity (2,000 items).
/// </summary>
public sealed class FolderVisitStore
{
    public const int DefaultCapacity = 2000;
    private readonly int _capacity;
    private readonly Dictionary<string, FolderVisitRecord> _records = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public int Count
    {
        get
        {
            lock (_lock) return _records.Count;
        }
    }

    public FolderVisitStore(int capacity = DefaultCapacity)
    {
        _capacity = capacity;
    }

    public static string GetDefaultFilePath()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "MyFileManager", "index", "frecency.jsonl");
    }

    public void RecordVisit(string path, int additionalVisits = 1, DateTime? timestamp = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        DateTime now = timestamp ?? DateTime.UtcNow;

        lock (_lock)
        {
            if (_records.TryGetValue(path, out var existing))
            {
                _records[path] = new FolderVisitRecord(existing.VisitCount + additionalVisits, now);
            }
            else
            {
                if (_records.Count >= _capacity)
                {
                    PruneLowestInternal(now);
                }
                _records[path] = new FolderVisitRecord(additionalVisits, now);
            }
        }
    }

    public double GetFrecencyScore(string path, DateTime? now = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return 0.0;
        DateTime current = now ?? DateTime.UtcNow;

        lock (_lock)
        {
            if (!_records.TryGetValue(path, out var record)) return 0.0;
            return CalculateScore(record, current);
        }
    }

    public IReadOnlyList<string> GetTopFrecent(int maxCount, DateTime? now = null)
    {
        DateTime current = now ?? DateTime.UtcNow;
        lock (_lock)
        {
            return _records
                .Select(kvp => new { Path = kvp.Key, Score = CalculateScore(kvp.Value, current) })
                .OrderByDescending(x => x.Score)
                .Take(maxCount)
                .Select(x => x.Path)
                .ToList();
        }
    }

    private static double CalculateScore(FolderVisitRecord record, DateTime now)
    {
        TimeSpan age = now - record.LastVisitUtc;
        double multiplier;

        if (age < TimeSpan.FromHours(4))
        {
            multiplier = 4.0;
        }
        else if (age < TimeSpan.FromHours(24))
        {
            multiplier = 2.0;
        }
        else if (age < TimeSpan.FromDays(7))
        {
            multiplier = 1.0;
        }
        else
        {
            multiplier = 0.5;
        }

        return Math.Min(60.0, record.VisitCount * multiplier);
    }

    private void PruneLowestInternal(DateTime now)
    {
        int removeCount = Math.Max(1, _capacity / 10); // Prune bottom 10%
        var toRemove = _records
            .Select(kvp => new { Path = kvp.Key, Score = CalculateScore(kvp.Value, now) })
            .OrderBy(x => x.Score)
            .Take(removeCount)
            .Select(x => x.Path)
            .ToList();

        foreach (var p in toRemove)
        {
            _records.Remove(p);
        }
    }

    public void Load(string filePath)
    {
        if (!File.Exists(filePath)) return;
        lock (_lock)
        {
            _records.Clear();
            try
            {
                foreach (string line in File.ReadLines(filePath))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var parts = line.Split('\t');
                    if (parts.Length >= 3 &&
                        int.TryParse(parts[1], out int count) &&
                        long.TryParse(parts[2], out long ticks))
                    {
                        string path = parts[0];
                        _records[path] = new FolderVisitRecord(count, new DateTime(ticks, DateTimeKind.Utc));
                    }
                }
            }
            catch
            {
                // Ignore corrupted file
            }
        }
    }

    public void Save(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;
        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string tempPath = filePath + ".tmp";
        lock (_lock)
        {
            try
            {
                using (var writer = new StreamWriter(tempPath, false, System.Text.Encoding.UTF8))
                {
                    foreach (var kvp in _records)
                    {
                        writer.WriteLine($"{kvp.Key}\t{kvp.Value.VisitCount}\t{kvp.Value.LastVisitUtc.Ticks}");
                    }
                }
                File.Move(tempPath, filePath, overwrite: true);
            }
            catch
            {
                try
                {
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                }
                catch
                {
                    // Best effort cleanup
                }
            }
        }
    }
}
