namespace FM.Core.Watching;

/// <summary>
/// Specifies the type of file system change observed in a watched folder.
/// </summary>
public enum FolderChangeType
{
    Created,
    Deleted,
    Renamed,
    Changed
}

