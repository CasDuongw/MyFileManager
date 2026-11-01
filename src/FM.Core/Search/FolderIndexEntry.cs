using System;
using System.IO;

namespace FM.Core.Search;

/// <summary>
/// Immutable, lightweight entry representing a directory in the folder index.
/// Precomputes and caches normalized name and character bitmask for zero-allocation matching.
/// </summary>
public sealed class FolderIndexEntry
{
    public string FullPath { get; }
    public string Name { get; }
    public string NormName { get; }
    public ulong Bitmask { get; }
    public int Depth { get; }
    public string? RawParent { get; }
    public string? NormParent { get; }

    public FolderIndexEntry(
        string fullPath,
        string name,
        string normName,
        ulong bitmask,
        int depth,
        string? rawParent,
        string? normParent)
    {
        FullPath = fullPath ?? string.Empty;
        Name = name ?? string.Empty;
        NormName = normName ?? string.Empty;
        Bitmask = bitmask;
        Depth = depth;
        RawParent = rawParent;
        NormParent = normParent;
    }

    public static FolderIndexEntry Create(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return new FolderIndexEntry(string.Empty, string.Empty, string.Empty, 0, 0, null, null);
        }

        string trimmed = fullPath.TrimEnd('\\', '/');
        string name = Path.GetFileName(trimmed);
        if (string.IsNullOrEmpty(name))
        {
            name = fullPath; // e.g. "C:\"
        }

        string normName = NameMatcher.Normalize(name);
        ulong bitmask = CharacterBitmask.Compute(normName);
        string? rawParent = Path.GetDirectoryName(trimmed);
        string? normParent = !string.IsNullOrEmpty(rawParent) ? NameMatcher.Normalize(rawParent) : null;

        int depth = 0;
        for (int i = 0; i < fullPath.Length; i++)
        {
            if (fullPath[i] == '\\' || fullPath[i] == '/') depth++;
        }

        return new FolderIndexEntry(fullPath, name, normName, bitmask, depth, rawParent, normParent);
    }
}
