using System.IO;
using System.Runtime.CompilerServices;
using FM.Core.Models;
using FM.Core.Services;

namespace FM.App.Tests.Fakes;

public class FakeDirectoryService : IDirectoryService
{
    private readonly Dictionary<string, List<FileEntry>> _directories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Exception> _errors = new(StringComparer.OrdinalIgnoreCase);
    private TimeSpan _delay = TimeSpan.Zero;

    public void AddDirectory(string path, IEnumerable<FileEntry> entries)
    {
        _directories[Path.GetFullPath(path)] = entries.ToList();
    }

    public void SetError(string path, Exception ex)
    {
        _errors[Path.GetFullPath(path)] = ex;
    }

    public void SetDelay(TimeSpan delay)
    {
        _delay = delay;
    }

    public async IAsyncEnumerable<IReadOnlyList<FileEntry>> EnumerateBatchesAsync(
        string path,
        int batchSize = 200,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        string fullPath = Path.GetFullPath(path);

        if (_delay > TimeSpan.Zero)
        {
            await Task.Delay(_delay, ct);
        }

        if (_errors.TryGetValue(fullPath, out var ex))
        {
            throw ex;
        }

        if (!_directories.TryGetValue(fullPath, out var entries))
        {
            throw new DirectoryNotFoundException($"Directory not found: '{path}'");
        }

        for (int i = 0; i < entries.Count; i += batchSize)
        {
            ct.ThrowIfCancellationRequested();
            var batch = entries.Skip(i).Take(batchSize).ToList();
            yield return batch;
        }
    }

    public async IAsyncEnumerable<FileEntry> EnumerateAsync(
        string path,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var batch in EnumerateBatchesAsync(path, 200, ct))
        {
            foreach (var item in batch)
            {
                ct.ThrowIfCancellationRequested();
                yield return item;
            }
        }
    }
}
