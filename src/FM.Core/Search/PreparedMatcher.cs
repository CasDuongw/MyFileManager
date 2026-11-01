using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FM.Core.Search;

public record MatchScoreResult(double Score, bool MatchedOnName, bool MatchedOnAncestor);

/// <summary>
/// High-performance matcher executing PreparedQuery against candidates.
/// Supports bitmask pre-filtering, optimal sequence alignment, multi-token AND matching,
/// ancestor segment matching, initials recognition, and parallel top-K selection.
/// </summary>
public static class PreparedMatcher
{
    private static readonly char[] PathDelimiters = ['/', '\\'];

    /// <summary>
    /// Matches a prepared query against a candidate folder name and optional parent path.
    /// Returns score (> 0) if matched, or -1.0 if not matched.
    /// </summary>
    public static double Match(
        PreparedQuery query,
        string rawName,
        string normName,
        ulong candidateBitmask,
        string? rawParentPath = null,
        string? normParentPath = null)
    {
        if (query.IsEmpty) return 0.0;
        if (string.IsNullOrEmpty(normName)) return -1.0;

        // Path-segment query (e.g., 'proj/web' or 'proj\web')
        if (query.HasPathSeparators && query.PathSegments.Count > 0)
        {
            return MatchPathSegments(query, rawName, normName, candidateBitmask, rawParentPath, normParentPath);
        }

        // Multi-token query (e.g. 'proj web' or 'doc')
        var tokens = query.Tokens;
        if (tokens.Count == 1)
        {
            return MatchSingleToken(tokens[0], rawName, normName, candidateBitmask, rawParentPath, normParentPath);
        }

        double totalScore = 0.0;
        bool anyMatchedName = false;

        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            double tokenScore = MatchSingleToken(t, rawName, normName, candidateBitmask, rawParentPath, normParentPath, out bool onName);
            if (tokenScore < 0)
            {
                return -1.0; // Every token must match (AND condition)
            }

            totalScore += tokenScore;
            if (onName) anyMatchedName = true;
        }

        // Candidates matching at least one token directly on the folder name get a boost
        if (anyMatchedName)
        {
            totalScore += 200.0;
        }

        return totalScore;
    }

    private static double MatchSingleToken(
        PreparedToken token,
        string rawName,
        string normName,
        ulong candidateBitmask,
        string? rawParentPath,
        string? normParentPath)
    {
        return MatchSingleToken(token, rawName, normName, candidateBitmask, rawParentPath, normParentPath, out _);
    }

    private static double MatchSingleToken(
        PreparedToken token,
        string rawName,
        string normName,
        ulong candidateBitmask,
        string? rawParentPath,
        string? normParentPath,
        out bool onName)
    {
        onName = false;

        // 1. Try matching against candidate name
        if (CharacterBitmask.IsSubset(token.Bitmask, candidateBitmask))
        {
            double nameScore = OptimalAlignmentScorer.Score(rawName.AsSpan(), normName.AsSpan(), token.Normalized.AsSpan());
            if (nameScore >= 0)
            {
                onName = true;
                return nameScore;
            }
        }

        // 2. Try matching against ancestor path segments (if available)
        if (!string.IsNullOrEmpty(normParentPath) && !string.IsNullOrEmpty(rawParentPath))
        {
            double ancestorScore = MatchAncestorSegments(token, rawParentPath, normParentPath);
            if (ancestorScore >= 0)
            {
                return ancestorScore * 0.35; // Ancestor matches are discounted relative to name matches
            }
        }

        return -1.0;
    }

    private static double MatchAncestorSegments(PreparedToken token, string rawParentPath, string normParentPath)
    {
        // Split parent path by slashes and check each segment
        var rawSegments = rawParentPath.Split(PathDelimiters, StringSplitOptions.RemoveEmptyEntries);
        var normSegments = normParentPath.Split(PathDelimiters, StringSplitOptions.RemoveEmptyEntries);

        double bestScore = -1.0;
        for (int i = 0; i < normSegments.Length; i++)
        {
            string normSeg = normSegments[i];
            ulong segBitmask = CharacterBitmask.Compute(normSeg);

            if (CharacterBitmask.IsSubset(token.Bitmask, segBitmask))
            {
                string rawSeg = (i < rawSegments.Length) ? rawSegments[i] : normSeg;
                double s = OptimalAlignmentScorer.Score(rawSeg.AsSpan(), normSeg.AsSpan(), token.Normalized.AsSpan());
                if (s > bestScore)
                {
                    bestScore = s;
                }
            }
        }

        return bestScore;
    }

    private static double MatchPathSegments(
        PreparedQuery query,
        string rawName,
        string normName,
        ulong candidateBitmask,
        string? rawParentPath,
        string? normParentPath)
    {
        var segments = query.PathSegments;
        if (segments.Count == 0) return 0.0;

        // The last segment of the path query MUST match the folder name
        var lastSeg = segments[^1];
        if (!CharacterBitmask.IsSubset(lastSeg.Bitmask, candidateBitmask))
        {
            return -1.0;
        }

        double nameScore = OptimalAlignmentScorer.Score(rawName.AsSpan(), normName.AsSpan(), lastSeg.Normalized.AsSpan());
        if (nameScore < 0) return -1.0;

        if (segments.Count == 1) return nameScore;

        if (string.IsNullOrEmpty(normParentPath) || string.IsNullOrEmpty(rawParentPath))
        {
            return -1.0;
        }

        // Preceding segments must match ancestor segments in reverse order
        var parentNormSegments = normParentPath.Split(PathDelimiters, StringSplitOptions.RemoveEmptyEntries);
        var parentRawSegments = rawParentPath.Split(PathDelimiters, StringSplitOptions.RemoveEmptyEntries);

        int pIdx = parentNormSegments.Length - 1;
        double totalAncestorScore = 0.0;

        for (int sIdx = segments.Count - 2; sIdx >= 0; sIdx--)
        {
            var reqSeg = segments[sIdx];
            bool found = false;

            while (pIdx >= 0)
            {
                string pNorm = parentNormSegments[pIdx];
                string pRaw = (pIdx < parentRawSegments.Length) ? parentRawSegments[pIdx] : pNorm;
                pIdx--;

                ulong pBitmask = CharacterBitmask.Compute(pNorm);
                if (CharacterBitmask.IsSubset(reqSeg.Bitmask, pBitmask))
                {
                    double s = OptimalAlignmentScorer.Score(pRaw.AsSpan(), pNorm.AsSpan(), reqSeg.Normalized.AsSpan());
                    if (s >= 0)
                    {
                        totalAncestorScore += s * 0.35;
                        found = true;
                        break;
                    }
                }
            }

            if (!found) return -1.0;
        }

        return nameScore + totalAncestorScore + 300.0;
    }

    /// <summary>
    /// Bounded Top-K selection using a min-heap with deterministic tie-breaking (shorter path, then alphabetical).
    /// Runs in O(N log K) time with zero full-array allocations.
    /// Partitions automatically across CPU cores for large candidate collections (N > 20,000).
    /// </summary>
    public static List<ScoredResult<T>> SelectTopK<T>(
        IReadOnlyList<T> items,
        PreparedQuery query,
        int k,
        Func<T, (string rawName, string normName, ulong bitmask, string? rawParent, string? normParent)> selector,
        Func<T, double>? additionalScoreBoost = null)
    {
        if (items.Count == 0 || k <= 0) return new List<ScoredResult<T>>();

        if (items.Count > 20_000)
        {
            return SelectTopKParallel(items, query, k, selector, additionalScoreBoost);
        }

        var queue = new PriorityQueue<T, CandidatePriority>();

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var (rawName, normName, bitmask, rawParent, normParent) = selector(item);

            double score = Match(query, rawName, normName, bitmask, rawParent, normParent);
            if (score >= 0)
            {
                if (additionalScoreBoost != null)
                {
                    score += additionalScoreBoost(item);
                }

                string fullPath = rawParent != null ? System.IO.Path.Combine(rawParent, rawName) : rawName;
                var priority = new CandidatePriority(score, fullPath);

                if (queue.Count < k)
                {
                    queue.Enqueue(item, priority);
                }
                else if (priority.CompareTo(queue.PeekPriority()) > 0)
                {
                    queue.Dequeue();
                    queue.Enqueue(item, priority);
                }
            }
        }

        var results = new List<ScoredResult<T>>(queue.Count);
        while (queue.TryDequeue(out var item, out var prio))
        {
            results.Add(new ScoredResult<T>(item, prio.Score, prio.FullPath));
        }

        // Sort descending by score, shorter path, then alphabetical
        results.Sort((a, b) =>
        {
            int cmp = b.Score.CompareTo(a.Score);
            if (cmp != 0) return cmp;
            int lenCmp = a.FullPath.Length.CompareTo(b.FullPath.Length);
            if (lenCmp != 0) return lenCmp;
            return string.Compare(a.FullPath, b.FullPath, StringComparison.OrdinalIgnoreCase);
        });

        return results;
    }

    private static List<ScoredResult<T>> SelectTopKParallel<T>(
        IReadOnlyList<T> items,
        PreparedQuery query,
        int k,
        Func<T, (string rawName, string normName, ulong bitmask, string? rawParent, string? normParent)> selector,
        Func<T, double>? additionalScoreBoost)
    {
        var localQueues = new ConcurrentBag<PriorityQueue<T, CandidatePriority>>();

        Parallel.ForEach(
            Partitioner.Create(0, items.Count),
            () => new PriorityQueue<T, CandidatePriority>(),
            (range, loopState, localQueue) =>
            {
                for (int i = range.Item1; i < range.Item2; i++)
                {
                    var item = items[i];
                    var (rawName, normName, bitmask, rawParent, normParent) = selector(item);

                    double score = Match(query, rawName, normName, bitmask, rawParent, normParent);
                    if (score >= 0)
                    {
                        if (additionalScoreBoost != null)
                        {
                            score += additionalScoreBoost(item);
                        }

                        string fullPath = rawParent != null ? System.IO.Path.Combine(rawParent, rawName) : rawName;
                        var priority = new CandidatePriority(score, fullPath);

                        if (localQueue.Count < k)
                        {
                            localQueue.Enqueue(item, priority);
                        }
                        else if (priority.CompareTo(localQueue.PeekPriority()) > 0)
                        {
                            localQueue.Dequeue();
                            localQueue.Enqueue(item, priority);
                        }
                    }
                }
                return localQueue;
            },
            localQueue => localQueues.Add(localQueue));

        var finalQueue = new PriorityQueue<T, CandidatePriority>();
        foreach (var lq in localQueues)
        {
            while (lq.TryDequeue(out var item, out var prio))
            {
                if (finalQueue.Count < k)
                {
                    finalQueue.Enqueue(item, prio);
                }
                else if (prio.CompareTo(finalQueue.PeekPriority()) > 0)
                {
                    finalQueue.Dequeue();
                    finalQueue.Enqueue(item, prio);
                }
            }
        }

        var results = new List<ScoredResult<T>>(finalQueue.Count);
        while (finalQueue.TryDequeue(out var item, out var prio))
        {
            results.Add(new ScoredResult<T>(item, prio.Score, prio.FullPath));
        }

        results.Sort((a, b) =>
        {
            int cmp = b.Score.CompareTo(a.Score);
            if (cmp != 0) return cmp;
            int lenCmp = a.FullPath.Length.CompareTo(b.FullPath.Length);
            if (lenCmp != 0) return lenCmp;
            return string.Compare(a.FullPath, b.FullPath, StringComparison.OrdinalIgnoreCase);
        });

        return results;
    }

    private static CandidatePriority PeekPriority<T>(this PriorityQueue<T, CandidatePriority> queue)
    {
        queue.TryPeek(out _, out var priority);
        return priority;
    }
}

public readonly struct CandidatePriority : IComparable<CandidatePriority>
{
    public readonly double Score;
    public readonly string FullPath;

    public CandidatePriority(double score, string fullPath)
    {
        Score = score;
        FullPath = fullPath ?? string.Empty;
    }

    public int CompareTo(CandidatePriority other)
    {
        int cmp = Score.CompareTo(other.Score);
        if (cmp != 0) return cmp;

        // In min-heap: longer path is considered "smaller" priority (evicted earlier)
        int lenCmp = other.FullPath.Length.CompareTo(FullPath.Length);
        if (lenCmp != 0) return lenCmp;

        // Lexicographically greater is considered "smaller" priority (evicted earlier)
        return string.Compare(other.FullPath, FullPath, StringComparison.OrdinalIgnoreCase);
    }
}

public record ScoredResult<T>(T Item, double Score, string FullPath = "");
