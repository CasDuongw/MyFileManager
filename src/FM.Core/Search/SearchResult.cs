using FM.Core.Models;

namespace FM.Core.Search;

/// <summary>
/// Represents a match found during search, referencing the original FileEntry,
/// its containing parent directory, and relevance score.
/// </summary>
/// <param name="Entry">The underlying file system entry.</param>
/// <param name="ParentDirectory">The absolute directory containing this item.</param>
/// <param name="Score">Relevance score (higher is more relevant).</param>
public record SearchResult(
    FileEntry Entry,
    string ParentDirectory,
    double Score);

