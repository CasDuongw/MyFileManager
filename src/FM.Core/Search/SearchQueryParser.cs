using System.IO;
using System.Text.RegularExpressions;
using FM.Core.Models;

namespace FM.Core.Search;

public record SearchParseResult(
    string CleanQueryText,
    Func<FileEntry, bool>? FilterPredicate,
    IReadOnlyList<string> Warnings,
    string? ErrorMessage,
    bool IsValid)
{
    public static SearchParseResult Valid(string cleanText, Func<FileEntry, bool>? predicate = null, IReadOnlyList<string>? warnings = null) =>
        new(cleanText, predicate, warnings ?? Array.Empty<string>(), null, true);

    public static SearchParseResult Invalid(string errorMessage) =>
        new(string.Empty, null, Array.Empty<string>(), errorMessage, false);
}

/// <summary>
/// Parses search queries containing filter operators (ext:, kind:, size:, modified:).
/// Treats unknown operators as literal search terms with a warning.
/// Produces an optimized FilterPredicate for secondary file matching.
/// Pure Core with zero WPF/UI dependencies.
/// </summary>
public static class SearchQueryParser
{
    private static readonly HashSet<string> KnownOperators = new(StringComparer.OrdinalIgnoreCase)
    {
        "ext:", "extension:",
        "kind:", "type:",
        "size:",
        "modified:", "date:"
    };

    private static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg", ".ico", ".tiff" };

    private static readonly HashSet<string> VideoExts = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v" };

    private static readonly HashSet<string> AudioExts = new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a", ".wma" };

    private static readonly HashSet<string> DocumentExts = new(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".doc", ".docx", ".txt", ".rtf", ".odt", ".xls", ".xlsx", ".ppt", ".pptx", ".csv", ".md" };

    private static readonly HashSet<string> ArchiveExts = new(StringComparer.OrdinalIgnoreCase)
        { ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".iso" };

    public static SearchParseResult Parse(string? rawQuery, TimeProvider? timeProvider = null)
    {
        if (string.IsNullOrWhiteSpace(rawQuery))
        {
            return SearchParseResult.Valid(string.Empty);
        }

        var time = timeProvider ?? TimeProvider.System;
        var now = time.GetLocalNow();

        var tokens = rawQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var textTokens = new List<string>();
        var warnings = new List<string>();
        var predicates = new List<Func<FileEntry, bool>>();

        foreach (var token in tokens)
        {
            int colonIdx = token.IndexOf(':');
            if (colonIdx > 0)
            {
                string op = token[..(colonIdx + 1)];
                string val = token[(colonIdx + 1)..].Trim();

                if (KnownOperators.Contains(op))
                {
                    if (string.IsNullOrEmpty(val))
                    {
                        warnings.Add($"Toán tử '{op}' thiếu giá trị lọc.");
                        continue;
                    }

                    if (op.Equals("ext:", StringComparison.OrdinalIgnoreCase) ||
                        op.Equals("extension:", StringComparison.OrdinalIgnoreCase))
                    {
                        var exts = val.Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(e => e.StartsWith('.') ? e : "." + e)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);

                        predicates.Add(entry => !entry.IsDirectory && exts.Contains(Path.GetExtension(entry.Name)));
                    }
                    else if (op.Equals("kind:", StringComparison.OrdinalIgnoreCase) ||
                             op.Equals("type:", StringComparison.OrdinalIgnoreCase))
                    {
                        string k = val.ToLowerInvariant();
                        if (k is "folder" or "dir" or "directory")
                        {
                            predicates.Add(entry => entry.IsDirectory);
                        }
                        else if (k is "image" or "picture" or "photo")
                        {
                            predicates.Add(entry => !entry.IsDirectory && ImageExts.Contains(Path.GetExtension(entry.Name)));
                        }
                        else if (k is "video" or "movie")
                        {
                            predicates.Add(entry => !entry.IsDirectory && VideoExts.Contains(Path.GetExtension(entry.Name)));
                        }
                        else if (k is "audio" or "music" or "sound")
                        {
                            predicates.Add(entry => !entry.IsDirectory && AudioExts.Contains(Path.GetExtension(entry.Name)));
                        }
                        else if (k is "doc" or "document" or "text")
                        {
                            predicates.Add(entry => !entry.IsDirectory && DocumentExts.Contains(Path.GetExtension(entry.Name)));
                        }
                        else if (k is "archive" or "zip" or "compressed")
                        {
                            predicates.Add(entry => !entry.IsDirectory && ArchiveExts.Contains(Path.GetExtension(entry.Name)));
                        }
                        else
                        {
                            warnings.Add($"Loại tệp không nhận diện: 'kind:{val}'.");
                        }
                    }
                    else if (op.Equals("size:", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!TryParseSizePredicate(val, out var sizePred, out var sizeErr))
                        {
                            return SearchParseResult.Invalid(sizeErr ?? $"Giá trị kích thước không hợp lệ: '{val}'");
                        }
                        predicates.Add(sizePred);
                    }
                    else if (op.Equals("modified:", StringComparison.OrdinalIgnoreCase) ||
                             op.Equals("date:", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!TryParseDatePredicate(val, now, out var datePred, out var dateErr))
                        {
                            return SearchParseResult.Invalid(dateErr ?? $"Giá trị ngày tháng không hợp lệ: '{val}'");
                        }
                        predicates.Add(datePred);
                    }
                    continue;
                }
                else
                {
                    // Unknown operator: treat as literal search term with warning
                    warnings.Add($"Toán tử không nhận diện '{op}'. Đã tìm kiếm dưới dạng văn bản.");
                    textTokens.Add(token);
                    continue;
                }
            }

            // Normal text token
            textTokens.Add(token);
        }

        string cleanText = string.Join(" ", textTokens);
        Func<FileEntry, bool>? compositePredicate = null;
        if (predicates.Count > 0)
        {
            compositePredicate = entry =>
            {
                for (int i = 0; i < predicates.Count; i++)
                {
                    if (!predicates[i](entry)) return false;
                }
                return true;
            };
        }

        return SearchParseResult.Valid(cleanText, compositePredicate, warnings);
    }

    private static bool TryParseSizePredicate(string val, out Func<FileEntry, bool> predicate, out string? error)
    {
        predicate = _ => true;
        error = null;

        // Range syntax: 10kb..50mb
        if (val.Contains(".."))
        {
            var parts = val.Split("..", StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && TryParseBytes(parts[0], out long min) && TryParseBytes(parts[1], out long max))
            {
                predicate = entry => !entry.IsDirectory && entry.Size >= min && entry.Size <= max;
                return true;
            }
            error = $"Dải kích thước không hợp lệ: '{val}'";
            return false;
        }

        string op = ">=";
        string numStr = val;

        if (val.StartsWith(">=")) { op = ">="; numStr = val[2..]; }
        else if (val.StartsWith("<=")) { op = "<="; numStr = val[2..]; }
        else if (val.StartsWith('>')) { op = ">"; numStr = val[1..]; }
        else if (val.StartsWith('<')) { op = "<"; numStr = val[1..]; }
        else if (val.StartsWith('=')) { op = "="; numStr = val[1..]; }

        if (!TryParseBytes(numStr, out long targetBytes))
        {
            error = $"Kích thước không hợp lệ: '{val}'";
            return false;
        }

        predicate = op switch
        {
            ">=" => entry => !entry.IsDirectory && entry.Size >= targetBytes,
            "<=" => entry => !entry.IsDirectory && entry.Size <= targetBytes,
            ">" => entry => !entry.IsDirectory && entry.Size > targetBytes,
            "<" => entry => !entry.IsDirectory && entry.Size < targetBytes,
            _ => entry => !entry.IsDirectory && entry.Size == targetBytes
        };
        return true;
    }

    private static bool TryParseBytes(string str, out long bytes)
    {
        bytes = 0;
        str = str.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(str)) return false;

        long multiplier = 1;
        if (str.EndsWith("KB") || str.EndsWith('K'))
        {
            multiplier = 1024L;
            str = str.TrimEnd('B').TrimEnd('K');
        }
        else if (str.EndsWith("MB") || str.EndsWith('M'))
        {
            multiplier = 1024L * 1024L;
            str = str.TrimEnd('B').TrimEnd('M');
        }
        else if (str.EndsWith("GB") || str.EndsWith('G'))
        {
            multiplier = 1024L * 1024L * 1024L;
            str = str.TrimEnd('B').TrimEnd('G');
        }
        else if (str.EndsWith('B'))
        {
            multiplier = 1L;
            str = str.TrimEnd('B');
        }

        if (double.TryParse(str, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val))
        {
            bytes = (long)(val * multiplier);
            return true;
        }

        return false;
    }

    private static bool TryParseDatePredicate(string val, DateTimeOffset now, out Func<FileEntry, bool> predicate, out string? error)
    {
        predicate = _ => true;
        error = null;
        string v = val.ToLowerInvariant();

        var todayStart = now.Date;

        switch (v)
        {
            case "today":
                predicate = entry => entry.Modified >= todayStart;
                return true;

            case "yesterday":
                var yesterdayStart = todayStart.AddDays(-1);
                predicate = entry => entry.Modified >= yesterdayStart && entry.Modified < todayStart;
                return true;

            case "thisweek":
                int diff = (7 + (now.DayOfWeek - DayOfWeek.Monday)) % 7;
                var weekStart = todayStart.AddDays(-1 * diff);
                predicate = entry => entry.Modified >= weekStart;
                return true;

            case "thismonth":
                var monthStart = new DateTime(now.Year, now.Month, 1);
                predicate = entry => entry.Modified >= monthStart;
                return true;

            case "thisyear":
                var yearStart = new DateTime(now.Year, 1, 1);
                predicate = entry => entry.Modified >= yearStart;
                return true;

            default:
                if (DateTime.TryParse(val, out var exactDate))
                {
                    var dayStart = exactDate.Date;
                    var dayEnd = dayStart.AddDays(1);
                    predicate = entry => entry.Modified >= dayStart && entry.Modified < dayEnd;
                    return true;
                }
                error = $"Ngày tháng không nhận diện: '{val}' (hỗ trợ: today, yesterday, thisweek, thismonth, thisyear hoặc YYYY-MM-DD)";
                return false;
        }
    }
}

