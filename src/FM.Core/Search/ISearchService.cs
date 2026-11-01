namespace FM.Core.Search;

/// <summary>
/// Service providing high-performance, asynchronous, recursive file search.
/// Pure Core component with no UI or WPF dependencies.
/// </summary>
public interface ISearchService
{
    /// <summary>
    /// Total count of directories skipped during search due to access denial, I/O errors, or long path failures.
    /// </summary>
    int SkippedFolderCount { get; }

    /// <summary>
    /// True if the search exceeded the MaxResults limit (default 10,000) and was halted early.
    /// </summary>
    bool IsResultsLimited { get; }

    /// <summary>
    /// Asynchronously performs recursive search across the query scope, yielding batches of SearchResult.
    /// First batch arrives quickly (< 50ms) to ensure responsive UI updates.
    /// </summary>
    IAsyncEnumerable<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken ct);
}

