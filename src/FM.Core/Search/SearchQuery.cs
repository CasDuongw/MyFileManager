using FM.Core.Models;

namespace FM.Core.Search;

/// <summary>
/// Immutable parameters for a file search execution.
/// </summary>
/// <param name="QueryText">Text to match against item names.</param>
/// <param name="Scope">Scope enum (CurrentFolder, OpenPanes, ThisPC).</param>
/// <param name="RootPaths">List of absolute root folder paths to search.</param>
/// <param name="MaxResults">Maximum allowed results before capping (default 10,000).</param>
/// <param name="FilterPredicate">Optional secondary filter predicate (for Phase 4 operator filters).</param>
public record SearchQuery(
    string QueryText,
    SearchScope Scope,
    IReadOnlyList<string> RootPaths,
    int MaxResults = 10000,
    Func<FileEntry, bool>? FilterPredicate = null);

