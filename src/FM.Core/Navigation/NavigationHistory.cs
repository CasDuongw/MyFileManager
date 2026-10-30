namespace FM.Core.Navigation;

/// <summary>
/// Manages backward and forward navigation history for a file pane.
/// Supports two-phase navigation (peek target first, commit only after validation)
/// to prevent stack corruption when navigating to deleted or inaccessible folders.
/// </summary>
public class NavigationHistory
{
    private readonly Stack<string> _backStack = new();
    private readonly Stack<string> _forwardStack = new();

    public string? CurrentPath { get; private set; }

    public bool CanGoBack => _backStack.Count > 0;
    public bool CanGoForward => _forwardStack.Count > 0;

    public NavigationHistory(string? initialPath = null)
    {
        CurrentPath = initialPath;
    }

    /// <summary>
    /// Peeks at the next path on the back stack without modifying any stacks.
    /// </summary>
    public string? PeekBack()
    {
        return CanGoBack ? _backStack.Peek() : null;
    }

    /// <summary>
    /// Peeks at the next path on the forward stack without modifying any stacks.
    /// </summary>
    public string? PeekForward()
    {
        return CanGoForward ? _forwardStack.Peek() : null;
    }

    /// <summary>
    /// Commits navigation to a new path. Pushes current path onto back stack and clears forward stack.
    /// </summary>
    public bool CommitNavigate(string newPath)
    {
        if (string.IsNullOrWhiteSpace(newPath)) return false;

        if (CurrentPath != null && string.Equals(Path.GetFullPath(CurrentPath), Path.GetFullPath(newPath), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (CurrentPath != null)
        {
            _backStack.Push(CurrentPath);
        }

        _forwardStack.Clear();
        CurrentPath = newPath;
        return true;
    }

    /// <summary>
    /// Commits moving back in history after the target path has been validated.
    /// </summary>
    public string? CommitBack()
    {
        if (!CanGoBack) return null;

        if (CurrentPath != null)
        {
            _forwardStack.Push(CurrentPath);
        }

        CurrentPath = _backStack.Pop();
        return CurrentPath;
    }

    /// <summary>
    /// Commits moving forward in history after the target path has been validated.
    /// </summary>
    public string? CommitForward()
    {
        if (!CanGoForward) return null;

        if (CurrentPath != null)
        {
            _backStack.Push(CurrentPath);
        }

        CurrentPath = _forwardStack.Pop();
        return CurrentPath;
    }

    /// <summary>
    /// Navigates to a new path in a single step (calls <see cref="CommitNavigate"/>).
    /// </summary>
    public bool Navigate(string newPath) => CommitNavigate(newPath);

    /// <summary>
    /// Navigates back in a single step (calls <see cref="CommitBack"/>).
    /// </summary>
    public string? GoBack() => CommitBack();

    /// <summary>
    /// Navigates forward in a single step (calls <see cref="CommitForward"/>).
    /// </summary>
    public string? GoForward() => CommitForward();

    /// <summary>
    /// Returns the parent directory path of <see cref="CurrentPath"/>, or null if already at root or path is invalid.
    /// </summary>
    public string? GetParentPath()
    {
        if (string.IsNullOrWhiteSpace(CurrentPath)) return null;

        try
        {
            var parent = Directory.GetParent(CurrentPath);
            return parent?.FullName;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Resets the history stacks and optionally sets a new current path.
    /// </summary>
    public void Reset(string? newPath = null)
    {
        _backStack.Clear();
        _forwardStack.Clear();
        CurrentPath = newPath;
    }
}
