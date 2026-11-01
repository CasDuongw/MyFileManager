using System;
using System.Collections.Generic;

namespace FM.Core.Search;

/// <summary>
/// Optimal sequence alignment scorer (Smith-Waterman style DP) for fuzzy string matching.
/// Rewards consecutive matches, word boundaries, camelCase transitions, and position 0 matches.
/// Penalizes gaps and late first match. Computes the globally best alignment (not greedy leftmost).
/// Allocation-free hot path using stackalloc buffers.
/// </summary>
public static class OptimalAlignmentScorer
{
    public const double ExactMatchBonus = 500.0;
    public const double BoundaryWordStartBonus = 80.0;
    public const double BoundaryDelimiterBonus = 60.0;
    public const double BoundaryCamelHumpBonus = 50.0;
    public const double BoundaryDigitBonus = 40.0;
    public const double ConsecutiveMatchBonus = 35.0;
    public const double BaseCharMatchScore = 15.0;
    public const double GapStartPenalty = 6.0;
    public const double GapExtendPenalty = 3.0;

    /// <summary>
    /// Computes the optimal alignment score of token in candidate.
    /// Returns negative score if token cannot be aligned.
    /// </summary>
    public static double Score(
        ReadOnlySpan<char> rawCandidate,
        ReadOnlySpan<char> normCandidate,
        ReadOnlySpan<char> normToken)
    {
        if (normToken.IsEmpty) return 0.0;
        int tLen = normToken.Length;
        int cLen = normCandidate.Length;
        if (cLen < tLen) return -1.0;

        // Fast path 1: Exact match
        if (normCandidate.SequenceEqual(normToken))
        {
            return 1000.0 + ExactMatchBonus - (cLen * 0.1);
        }

        // Fast path 2: Prefix match
        if (normCandidate.StartsWith(normToken))
        {
            double prefixScore = 800.0 + (tLen * 30.0) - ((cLen - tLen) * 0.5);
            return prefixScore;
        }

        // Fast path 3: Contiguous match at a word boundary
        int wordStartIdx = FindWordStartContiguous(rawCandidate, normCandidate, normToken);
        if (wordStartIdx >= 0)
        {
            double wsScore = 650.0 + (tLen * 25.0) - (wordStartIdx * 2.0) - ((cLen - tLen) * 0.5);
            return wsScore;
        }

        // Optimal Alignment DP
        // Max token length handled by stackalloc: 64, candidate: 256
        if (tLen > 64 || cLen > 256)
        {
            return FallbackSimpleScore(normCandidate, normToken);
        }

        Span<double> prevD = stackalloc double[cLen];
        Span<double> prevM = stackalloc double[cLen];
        Span<double> currD = stackalloc double[cLen];
        Span<double> currM = stackalloc double[cLen];

        // First token character (row 0)
        char t0 = normToken[0];
        double rowMax = double.NegativeInfinity;

        for (int j = 0; j < cLen; j++)
        {
            if (normCandidate[j] == t0)
            {
                double bonus = GetBoundaryBonus(rawCandidate, j);
                double score = BaseCharMatchScore + bonus - (j * 1.5);
                prevD[j] = score;
            }
            else
            {
                prevD[j] = double.NegativeInfinity;
            }

            rowMax = Math.Max(rowMax, prevD[j]);
            prevM[j] = rowMax;
        }

        if (rowMax <= double.NegativeInfinity)
        {
            return -1.0; // First character not found
        }

        // Subsequent token characters
        for (int i = 1; i < tLen; i++)
        {
            char ti = normToken[i];
            rowMax = double.NegativeInfinity;

            for (int j = 0; j < cLen; j++)
            {
                if (normCandidate[j] == ti && j >= i)
                {
                    double bonus = GetBoundaryBonus(rawCandidate, j);
                    double prevConsec = (j > 0 && prevD[j - 1] > double.NegativeInfinity)
                        ? prevD[j - 1] + ConsecutiveMatchBonus
                        : double.NegativeInfinity;

                    double prevAny = (j > 0 && prevM[j - 1] > double.NegativeInfinity)
                        ? prevM[j - 1] - GapStartPenalty - ((j - i) * GapExtendPenalty)
                        : double.NegativeInfinity;

                    double bestPrev = Math.Max(prevConsec, prevAny);
                    currD[j] = (bestPrev > double.NegativeInfinity)
                        ? bestPrev + BaseCharMatchScore + bonus
                        : double.NegativeInfinity;
                }
                else
                {
                    currD[j] = double.NegativeInfinity;
                }

                rowMax = Math.Max(rowMax, currD[j]);
                currM[j] = rowMax;
            }

            if (rowMax <= double.NegativeInfinity)
            {
                return -1.0; // Token cannot be aligned
            }

            currD.CopyTo(prevD);
            currM.CopyTo(prevM);
        }

        double finalScore = prevM[cLen - 1];
        if (finalScore > 0)
        {
            finalScore -= (cLen - tLen) * 0.5; // slight length penalty for noise
        }

        return finalScore;
    }

    /// <summary>
    /// Computes the optimal alignment and returns matched character indices in the candidate.
    /// Only called for top-K results to highlight matched characters without overhead in the hot loop.
    /// </summary>
    public static List<int> GetMatchedIndices(
        ReadOnlySpan<char> rawCandidate,
        ReadOnlySpan<char> normCandidate,
        ReadOnlySpan<char> normToken)
    {
        var result = new List<int>(normToken.Length);
        if (normToken.IsEmpty || normCandidate.IsEmpty) return result;

        int tLen = normToken.Length;
        int cLen = normCandidate.Length;

        // Prefix match indices
        if (normCandidate.StartsWith(normToken))
        {
            for (int i = 0; i < tLen; i++) result.Add(i);
            return result;
        }

        // Word start contiguous match indices
        int ws = FindWordStartContiguous(rawCandidate, normCandidate, normToken);
        if (ws >= 0)
        {
            for (int i = 0; i < tLen; i++) result.Add(ws + i);
            return result;
        }

        // Backtracking optimal alignment
        if (tLen <= 64 && cLen <= 256)
        {
            double[,] scoreTable = new double[tLen, cLen];
            double[,] maxTable = new double[tLen, cLen];

            for (int j = 0; j < cLen; j++)
            {
                if (normCandidate[j] == normToken[0])
                {
                    double bonus = GetBoundaryBonus(rawCandidate, j);
                    scoreTable[0, j] = BaseCharMatchScore + bonus - (j * 1.5);
                }
                else
                {
                    scoreTable[0, j] = double.NegativeInfinity;
                }
                maxTable[0, j] = (j == 0) ? scoreTable[0, 0] : Math.Max(maxTable[0, j - 1], scoreTable[0, j]);
            }

            for (int i = 1; i < tLen; i++)
            {
                for (int j = 0; j < cLen; j++)
                {
                    if (normCandidate[j] == normToken[i] && j >= i)
                    {
                        double bonus = GetBoundaryBonus(rawCandidate, j);
                        double prevConsec = (j > 0 && scoreTable[i - 1, j - 1] > double.NegativeInfinity)
                            ? scoreTable[i - 1, j - 1] + ConsecutiveMatchBonus
                            : double.NegativeInfinity;

                        double prevAny = (j > 0 && maxTable[i - 1, j - 1] > double.NegativeInfinity)
                            ? maxTable[i - 1, j - 1] - GapStartPenalty - ((j - i) * GapExtendPenalty)
                            : double.NegativeInfinity;

                        double bestPrev = Math.Max(prevConsec, prevAny);
                        scoreTable[i, j] = (bestPrev > double.NegativeInfinity)
                            ? bestPrev + BaseCharMatchScore + bonus
                            : double.NegativeInfinity;
                    }
                    else
                    {
                        scoreTable[i, j] = double.NegativeInfinity;
                    }

                    maxTable[i, j] = (j == 0) ? scoreTable[i, 0] : Math.Max(maxTable[i, j - 1], scoreTable[i, j]);
                }
            }

            // Backtrack from best column in row tLen - 1
            int currCol = -1;
            double bestFinal = double.NegativeInfinity;
            for (int j = tLen - 1; j < cLen; j++)
            {
                if (scoreTable[tLen - 1, j] > bestFinal)
                {
                    bestFinal = scoreTable[tLen - 1, j];
                    currCol = j;
                }
            }

            if (currCol >= 0)
            {
                result.Add(currCol);
                for (int i = tLen - 1; i >= 1; i--)
                {
                    int prevBest = -1;
                    double bestScore = double.NegativeInfinity;
                    for (int p = 0; p < currCol; p++)
                    {
                        if (scoreTable[i - 1, p] > bestScore)
                        {
                            bestScore = scoreTable[i - 1, p];
                            prevBest = p;
                        }
                    }
                    if (prevBest >= 0)
                    {
                        result.Insert(0, prevBest);
                        currCol = prevBest;
                    }
                }
                return result;
            }
        }

        // Fallback greedy
        int cIdx = 0;
        for (int tIdx = 0; tIdx < tLen; tIdx++)
        {
            while (cIdx < cLen)
            {
                if (normCandidate[cIdx] == normToken[tIdx])
                {
                    result.Add(cIdx++);
                    break;
                }
                cIdx++;
            }
        }

        return result;
    }

    public static double GetBoundaryBonus(ReadOnlySpan<char> rawCandidate, int index)
    {
        if (index == 0) return BoundaryWordStartBonus;
        char prev = rawCandidate[index - 1];
        char curr = rawCandidate[index];

        // Delimiter boundary
        if (prev == ' ' || prev == '-' || prev == '_' || prev == '.' || prev == '/' || prev == '\\')
        {
            return BoundaryDelimiterBonus;
        }

        // CamelCase hump (e.g. 'F' in MyFileManager)
        if (char.IsUpper(curr) && char.IsLower(prev))
        {
            return BoundaryCamelHumpBonus;
        }

        // Digit boundary
        if ((char.IsDigit(curr) && !char.IsDigit(prev)) || (!char.IsDigit(curr) && char.IsDigit(prev)))
        {
            return BoundaryDigitBonus;
        }

        return 0.0;
    }

    private static int FindWordStartContiguous(
        ReadOnlySpan<char> rawCandidate,
        ReadOnlySpan<char> normCandidate,
        ReadOnlySpan<char> normToken)
    {
        int limit = normCandidate.Length - normToken.Length;
        for (int i = 1; i <= limit; i++)
        {
            char prev = rawCandidate[i - 1];
            if (prev == ' ' || prev == '-' || prev == '_' || prev == '.' || prev == '/' || prev == '\\' ||
                (char.IsUpper(rawCandidate[i]) && char.IsLower(prev)))
            {
                if (normCandidate.Slice(i, normToken.Length).SequenceEqual(normToken))
                {
                    return i;
                }
            }
        }
        return -1;
    }

    private static double FallbackSimpleScore(ReadOnlySpan<char> normCandidate, ReadOnlySpan<char> normToken)
    {
        int cIdx = 0;
        for (int t = 0; t < normToken.Length; t++)
        {
            char ch = normToken[t];
            int found = -1;
            while (cIdx < normCandidate.Length)
            {
                if (normCandidate[cIdx] == ch)
                {
                    found = cIdx++;
                    break;
                }
                cIdx++;
            }
            if (found < 0) return -1.0;
        }
        return 100.0;
    }
}

