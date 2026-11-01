using System;
using System.Collections.Generic;

namespace FM.Core.Search;

public record PreparedToken(
    string Raw,
    string Normalized,
    ulong Bitmask);

/// <summary>
/// Immutable prepared query holding pre-normalized tokens, bitmasks, and path segment parsing.
/// Prepared once per keystroke and reused across all index candidates without allocations.
/// </summary>
public sealed class PreparedQuery
{
    public string Raw { get; }
    public string Normalized { get; }
    public bool IsEmpty { get; }
    public bool HasPathSeparators { get; }
    public IReadOnlyList<PreparedToken> Tokens { get; }
    public IReadOnlyList<PreparedToken> PathSegments { get; }

    public PreparedQuery(string query)
    {
        Raw = query ?? string.Empty;
        Normalized = NameMatcher.Normalize(Raw);
        IsEmpty = string.IsNullOrWhiteSpace(Normalized);

        HasPathSeparators = Raw.Contains('/') || Raw.Contains('\\');

        if (IsEmpty)
        {
            Tokens = Array.Empty<PreparedToken>();
            PathSegments = Array.Empty<PreparedToken>();
            return;
        }

        // Multi-token parsing split by whitespace
        var rawTokens = Raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var tokenList = new List<PreparedToken>(rawTokens.Length);
        foreach (var t in rawTokens)
        {
            string norm = NameMatcher.Normalize(t);
            if (!string.IsNullOrEmpty(norm))
            {
                tokenList.Add(new PreparedToken(t, norm, CharacterBitmask.Compute(norm)));
            }
        }
        Tokens = tokenList;

        // Path-segment query parsing (e.g., 'proj/web' or 'proj\web')
        if (HasPathSeparators)
        {
            var rawSegments = Raw.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
            var segList = new List<PreparedToken>(rawSegments.Length);
            foreach (var s in rawSegments)
            {
                string norm = NameMatcher.Normalize(s);
                if (!string.IsNullOrEmpty(norm))
                {
                    segList.Add(new PreparedToken(s, norm, CharacterBitmask.Compute(norm)));
                }
            }
            PathSegments = segList;
        }
        else
        {
            PathSegments = Array.Empty<PreparedToken>();
        }
    }

    public static PreparedQuery Create(string query) => new(query);
}

