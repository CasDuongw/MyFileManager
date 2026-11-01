using System;

namespace FM.Core.Search;

/// <summary>
/// 64-bit character presence bitmask for ultra-fast candidate filtering.
/// Bits 0..25: 'a' through 'z'
/// Bits 26..35: '0' through '9'
/// Bit 36: other characters
/// </summary>
public static class CharacterBitmask
{
    public static ulong Compute(ReadOnlySpan<char> text)
    {
        ulong mask = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c >= 'a' && c <= 'z')
            {
                mask |= 1UL << (c - 'a');
            }
            else if (c >= 'A' && c <= 'Z')
            {
                mask |= 1UL << (c - 'A');
            }
            else if (c >= '0' && c <= '9')
            {
                mask |= 1UL << (26 + (c - '0'));
            }
            else if (c != ' ' && c != '-' && c != '_' && c != '.' && c != '/' && c != '\\')
            {
                mask |= 1UL << 36;
            }
        }
        return mask;
    }

    /// <summary>
    /// Returns true if all characters in the query bitmask are present in the candidate bitmask.
    /// </summary>
    public static bool IsSubset(ulong queryMask, ulong candidateMask)
    {
        return (candidateMask & queryMask) == queryMask;
    }
}

