using System.Windows.Media;

namespace FM.App.Services;

/// <summary>
/// Service providing high-quality file and folder thumbnails on a dedicated STA background thread.
/// Falls back to shell icon extraction if thumbnail generation is unsupported or fails.
/// </summary>
public interface IThumbnailService
{
    Task<ImageSource?> GetThumbnailAsync(string path, int size = 96, CancellationToken ct = default);
}

