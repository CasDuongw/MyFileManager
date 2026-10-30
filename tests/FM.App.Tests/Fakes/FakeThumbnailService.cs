using System.Windows.Media;
using FM.App.Services;

namespace FM.App.Tests.Fakes;

public class FakeThumbnailService : IThumbnailService
{
    public Func<string, int, CancellationToken, Task<ImageSource?>>? Handler { get; set; }
    public List<string> RequestedPaths { get; } = new();

    public Task<ImageSource?> GetThumbnailAsync(string path, int size = 96, CancellationToken ct = default)
    {
        RequestedPaths.Add(path);
        if (Handler != null)
        {
            return Handler(path, size, ct);
        }
        return Task.FromResult<ImageSource?>(null);
    }
}

