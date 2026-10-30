using System.Windows.Media;

namespace FM.App.Services;

/// <summary>
/// Service to retrieve and cache native Windows shell icons asynchronously
/// using a dedicated STA worker thread to protect against apartment threading violations in shell extensions.
/// </summary>
public interface IIconService : IDisposable
{
    /// <summary>
    /// Returns the icon for the specified file, directory, or drive.
    /// Returns immediately from cache if available, or queues for extraction on a dedicated STA thread.
    /// </summary>
    Task<ImageSource?> GetIconAsync(string fullPath, bool isDirectory, bool isDrive = false, CancellationToken ct = default);
}
