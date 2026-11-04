using System.IO;

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

        string currentNorm = NormalizePathForComparison(CurrentPath);
        string newNorm = NormalizePathForComparison(newPath);

        if (!string.IsNullOrEmpty(currentNorm) && string.Equals(currentNorm, newNorm, StringComparison.OrdinalIgnoreCase))
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
    /// Creates an independent snapshot clone of this navigation history.
    /// </summary>
    public NavigationHistory Clone()
    {
        var copy = new NavigationHistory(CurrentPath);
        var backItems = _backStack.ToArray();
        for (int i = backItems.Length - 1; i >= 0; i--)
        {
            copy._backStack.Push(backItems[i]);
        }
        var forwardItems = _forwardStack.ToArray();
        for (int i = forwardItems.Length - 1; i >= 0; i--)
        {
            copy._forwardStack.Push(forwardItems[i]);
        }
        return copy;
    }

    /// <summary>
    /// Returns the parent directory path of <see cref="CurrentPath"/>, or "thispc://" when at a drive root,
    /// or null if already at top or path is invalid.
    /// </summary>
    public string? GetParentPath()
    {
        if (string.IsNullOrWhiteSpace(CurrentPath)) return null;

        if (CurrentPath.Equals("thispc://", StringComparison.OrdinalIgnoreCase) ||
            CurrentPath.Equals("recyclebin://", StringComparison.OrdinalIgnoreCase))
        {
            return null; // Top virtual view
        }

        try
        {
            var parent = Directory.GetParent(CurrentPath);
            if (parent != null)
            {
                return parent.FullName;
            }

            // At drive root (e.g. C:\) -> parent is "This PC"
            string root = Path.GetPathRoot(CurrentPath) ?? string.Empty;
            if (!string.IsNullOrEmpty(root) && CurrentPath.TrimEnd('\\').Equals(root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                return "thispc://";
            }

            return null;
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

    private static string NormalizePathForComparison(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;

        if (path.StartsWith("thispc://", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("recyclebin://", StringComparison.OrdinalIgnoreCase))
        {
            return path.TrimEnd('/').ToLowerInvariant();
        }

        try
        {
            return Path.GetFullPath(path).TrimEnd('\\');
        }
        catch
        {
            return path.TrimEnd('\\');
        }
    }
}
