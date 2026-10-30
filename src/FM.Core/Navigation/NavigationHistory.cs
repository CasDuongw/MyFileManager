namespace FM.Core.Navigation;

/// <summary>
/// Manages backward and forward navigation history for a file pane.
/// Contains no UI dependencies and maintains stack integrity.
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
    /// Navigates to a new path. Pushes the current path onto the back stack and clears the forward stack.
    /// If the path is identical to <see cref="CurrentPath"/> (case-insensitive on Windows), no history change is recorded.
    /// </summary>
    public bool Navigate(string newPath)
    {
        if (string.IsNullOrWhiteSpace(newPath)) return false;

        // Compare paths using case-insensitive comparison on Windows
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
    /// Navigates back one step in history.
    /// </summary>
    /// <returns>The previous path, or null if cannot go back.</returns>
    public string? GoBack()
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
    /// Navigates forward one step in history.
    /// </summary>
    /// <returns>The forward path, or null if cannot go forward.</returns>
    public string? GoForward()
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
