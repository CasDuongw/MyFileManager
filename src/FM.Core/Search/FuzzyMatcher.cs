using System.Globalization;
using System.Text;

namespace FM.Core.Search;

/// <summary>
/// Result of a fuzzy match containing match tier score and matched character indices.
/// </summary>
public record FuzzyMatchResult(double Score, IReadOnlyList<int> MatchedIndices);

/// <summary>
/// High-performance, diacritic- and case-insensitive fuzzy matcher with hierarchical ranking:
/// Exact > Prefix > Word-start > Contiguous Substring > Ordered Subsequence (gap penalized).
/// Pure Core logic with zero WPF/UI dependencies.
/// </summary>
public static class FuzzyMatcher
{
    private static readonly char[] WordDelimiters = [' ', '-', '_', '.', '/', '\\'];

    /// <summary>
    /// Computes a fuzzy match score for the candidate against the query.
    /// Returns null if candidate does not match the query.
    /// Supports multi-token queries split by whitespace (all tokens must match).
    /// </summary>
    public static FuzzyMatchResult? Match(string? candidate, string? query)
    {
        if (string.IsNullOrEmpty(candidate)) return null;
        if (string.IsNullOrWhiteSpace(query))
        {
            return new FuzzyMatchResult(0.0, Array.Empty<int>());
        }

        string normCandidate = NameMatcher.Normalize(candidate);
        string normQuery = NameMatcher.Normalize(query);

        var tokens = normQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return new FuzzyMatchResult(0.0, Array.Empty<int>());
        }

        double totalScore = 0;
        var allMatchedIndices = new List<int>();

        for (int i = 0; i < tokens.Length; i++)
        {
            var tokenResult = MatchSingleToken(normCandidate, tokens[i]);
            if (tokenResult == null)
            {
                return null;
            }

            totalScore += tokenResult.Score;
            allMatchedIndices.AddRange(tokenResult.MatchedIndices);
        }

        // Shorter candidates with earlier matches receive a small precision bonus
        double lengthPenalty = (double)normQuery.Length / Math.Max(normCandidate.Length, normQuery.Length);
        double finalScore = totalScore + lengthPenalty;

        return new FuzzyMatchResult(finalScore, allMatchedIndices.Distinct().OrderBy(x => x).ToList());
    }

    private static FuzzyMatchResult? MatchSingleToken(string candidate, string token)
    {
        if (candidate.Length < token.Length) return null;

        // Tier 1: Exact match
        if (candidate.Equals(token, StringComparison.Ordinal))
        {
            var indices = Enumerable.Range(0, token.Length).ToList();
            return new FuzzyMatchResult(1000.0, indices);
        }

        // Tier 2: Prefix match
        if (candidate.StartsWith(token, StringComparison.Ordinal))
        {
            var indices = Enumerable.Range(0, token.Length).ToList();
            return new FuzzyMatchResult(800.0, indices);
        }

        // Tier 3: Word-start match (matches at start of any word segment)
        int wordStartIdx = FindWordStartMatch(candidate, token);
        if (wordStartIdx >= 0)
        {
            var indices = Enumerable.Range(wordStartIdx, token.Length).ToList();
            return new FuzzyMatchResult(600.0 - (wordStartIdx * 2.0), indices);
        }

        // Tier 4: Contiguous substring match
        int subIdx = candidate.IndexOf(token, StringComparison.Ordinal);
        if (subIdx >= 0)
        {
            var indices = Enumerable.Range(subIdx, token.Length).ToList();
            return new FuzzyMatchResult(400.0 - (subIdx * 2.0), indices);
        }

        // Tier 5: Ordered subsequence match (penalize gaps)
        var subSeqIndices = FindSubsequenceMatch(candidate, token);
        if (subSeqIndices != null)
        {
            int span = subSeqIndices[^1] - subSeqIndices[0] + 1;
            int gapPenalty = span - token.Length;
            double score = 200.0 - (subSeqIndices[0] * 2.0) - (gapPenalty * 3.0);
            return new FuzzyMatchResult(Math.Max(score, 10.0), subSeqIndices);
        }

        return null;
    }

    private static int FindWordStartMatch(string candidate, string token)
    {
        for (int i = 1; i <= candidate.Length - token.Length; i++)
        {
            if (WordDelimiters.Contains(candidate[i - 1]))
            {
                if (candidate.AsSpan(i, token.Length).SequenceEqual(token.AsSpan()))
                {
                    return i;
                }
            }
        }
        return -1;
    }

    private static List<int>? FindSubsequenceMatch(string candidate, string token)
    {
        var indices = new List<int>(token.Length);
        int cIdx = 0;

        for (int tIdx = 0; tIdx < token.Length; tIdx++)
        {
            char targetChar = token[tIdx];
            int foundIdx = -1;

            while (cIdx < candidate.Length)
            {
                if (candidate[cIdx] == targetChar)
                {
                    foundIdx = cIdx;
                    cIdx++;
                    break;
                }
                cIdx++;
            }

            if (foundIdx < 0)
            {
                return null;
            }

            indices.Add(foundIdx);
        }

        return indices;
    }
}

