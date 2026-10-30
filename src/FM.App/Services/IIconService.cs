using System.Windows.Media;

namespace FM.App.Services;

/// <summary>
/// Service to retrieve and cache native Windows shell icons asynchronously.
/// </summary>
public interface IIconService
{
    /// <summary>
    /// Returns the icon for the specified file or directory.
    /// Returns immediately from cache if available, or fetches asynchronously without blocking the UI.
    /// </summary>
    ValueTask<ImageSource?> GetIconAsync(string fullPath, bool isDirectory, CancellationToken ct = default);
}
