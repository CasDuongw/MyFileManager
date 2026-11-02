namespace FM.Core.PathResolution;

/// <summary>
/// Identifies the category of a resolved path in MyFileManager.
/// </summary>
public enum ResolvedPathType
{
    /// <summary>A standard physical or network directory on disk or UNC share.</summary>
    FileSystemDirectory,

    /// <summary>A physical file on disk.</summary>
    FileSystemFile,

    /// <summary>The virtual "This PC" view showing all system drives.</summary>
    ThisPC,

    /// <summary>The virtual "Recycle Bin" view showing deleted items.</summary>
    RecycleBin,

    /// <summary>A shell virtual folder that is not supported or recognized.</summary>
    UnsupportedVirtualFolder,

    /// <summary>An invalid or malformed path string.</summary>
    Invalid
}

