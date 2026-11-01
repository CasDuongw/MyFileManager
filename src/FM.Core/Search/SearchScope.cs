namespace FM.Core.Search;

/// <summary>
/// Defines the target scope for recursive search operations.
/// </summary>
public enum SearchScope
{
    /// <summary>
    /// Search within the current active folder and all descendant subfolders.
    /// </summary>
    CurrentFolder,

    /// <summary>
    /// Search across the union of all currently open panes (deduplicated).
    /// </summary>
    OpenPanes,

    /// <summary>
    /// Search across approved PC locations (Phase 5).
    /// </summary>
    ThisPC
}

