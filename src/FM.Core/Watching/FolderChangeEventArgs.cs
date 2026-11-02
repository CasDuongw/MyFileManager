namespace FM.Core.Watching;

/// <summary>
/// Provides data for folder change events (Created, Deleted, Renamed, Changed).
/// </summary>
public class FolderChangeEventArgs : EventArgs
{
    public FolderChangeType ChangeType { get; }
    public string FullPath { get; }
    public string? OldFullPath { get; }
    public string Name { get; }
    public string? OldName { get; }

    public FolderChangeEventArgs(FolderChangeType changeType, string fullPath, string? oldFullPath = null)
    {
        ChangeType = changeType;
        FullPath = fullPath ?? string.Empty;
        OldFullPath = oldFullPath;
        Name = Path.GetFileName(FullPath);
        OldName = !string.IsNullOrEmpty(oldFullPath) ? Path.GetFileName(oldFullPath) : null;
    }
}

