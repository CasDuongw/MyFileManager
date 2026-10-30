using System.Windows.Media;
using FM.App.Services;

namespace FM.App.Tests.Fakes;

public class FakeIconService : IIconService
{
    public Task<ImageSource?> GetIconAsync(string fullPath, bool isDirectory, bool isDrive = false, CancellationToken ct = default)
    {
        return Task.FromResult<ImageSource?>(null);
    }

    public void Dispose()
    {
    }
}
