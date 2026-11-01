using System.Globalization;
using System.Text;

namespace FM.Core.Search;

/// <summary>
/// High-performance diacritic-insensitive, case-insensitive string matcher and ranker.
/// Pure logic with zero UI/WPF dependencies.
/// </summary>
public static class NameMatcher
{
    private static readonly char[] WordDelimiters = [' ', '-', '_', '.', '/', '\\'];

    /// <summary>
    /// Removes diacritics (accents) from the given text (e.g., 'tài liệu' -> 'tai lieu').
    /// </summary>
    public static string RemoveDiacritics(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        string normalizedString = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalizedString.Length);

        for (int i = 0; i < normalizedString.Length; i++)
        {
            char c = normalizedString[i];
            if (c == 'đ')
            {
                sb.Append('d');
            }
            else if (c == 'Đ')
            {
                sb.Append('D');
            }
            else if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Normalizes text by removing diacritics and converting to lowercase invariant.
    /// </summary>
    public static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        return RemoveDiacritics(text).Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Evaluates if candidate string satisfies all whitespace-delimited tokens in filterText (AND match),
    /// ignoring case and diacritics.
    /// </summary>
    public static bool Matches(string? candidate, string? filterText)
    {
        if (string.IsNullOrWhiteSpace(filterText)) return true;
        if (string.IsNullOrEmpty(candidate)) return false;

        string normCandidate = Normalize(candidate);
        string normFilter = Normalize(filterText);

        var tokens = normFilter.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return true;

        for (int i = 0; i < tokens.Length; i++)
        {
            if (!normCandidate.Contains(tokens[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Evaluates match quality between candidate and query.
    /// Order: Exact (100) > Prefix (80) > Word-boundary (60) > Substring (40) > Ordered-subsequence (20) > None (0).
    /// </summary>
    public static double Score(string? candidate, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return 0;
        if (string.IsNullOrEmpty(candidate)) return 0;

        string normCandidate = Normalize(candidate);
        string normQuery = Normalize(query);

        if (string.IsNullOrEmpty(normCandidate) || string.IsNullOrEmpty(normQuery)) return 0;

        // 1. Exact match
        if (normCandidate.Equals(normQuery, StringComparison.Ordinal))
        {
            return 100.0;
        }

        // 2. Prefix match
        if (normCandidate.StartsWith(normQuery, StringComparison.Ordinal))
        {
            return 80.0 + (double)normQuery.Length / normCandidate.Length;
        }

        // 3. Word-boundary match (matches at start of any word segment)
        if (IsWordBoundaryMatch(normCandidate, normQuery))
        {
            return 60.0 + (double)normQuery.Length / normCandidate.Length;
        }

        // 4. Substring match
        int idx = normCandidate.IndexOf(normQuery, StringComparison.Ordinal);
        if (idx >= 0)
        {
            return 40.0 + (double)normQuery.Length / (normCandidate.Length + idx);
        }

        // 5. Ordered subsequence fuzzy match
        if (IsSubsequenceMatch(normCandidate, normQuery))
        {
            return 20.0 + (double)normQuery.Length / normCandidate.Length;
        }

        return 0;
    }

    private static bool IsWordBoundaryMatch(string candidate, string query)
    {
        for (int i = 0; i < candidate.Length; i++)
        {
            if (i > 0 && WordDelimiters.Contains(candidate[i - 1]))
            {
                if (candidate.Length - i >= query.Length &&
                    candidate.AsSpan(i, query.Length).SequenceEqual(query.AsSpan()))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static bool IsSubsequenceMatch(string candidate, string query)
    {
        int qIdx = 0;
        for (int cIdx = 0; cIdx < candidate.Length && qIdx < query.Length; cIdx++)
        {
            if (candidate[cIdx] == query[qIdx])
            {
                qIdx++;
            }
        }
        return qIdx == query.Length;
    }
}

