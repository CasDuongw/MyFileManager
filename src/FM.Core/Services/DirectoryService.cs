using System.Runtime.CompilerServices;
using System.Threading.Channels;
using FM.Core.Models;

namespace FM.Core.Services;

/// <summary>
/// Implementation of <see cref="IDirectoryService"/> using a background producer and bounded Channel.
/// Completely decouples directory enumeration from the caller's SynchronizationContext, ensuring
/// that slow I/O and file metadata queries never execute on the UI thread.
/// </summary>
public class DirectoryService : IDirectoryService
{
    public async IAsyncEnumerable<IReadOnlyList<FileEntry>> EnumerateBatchesAsync(
        string path,
        int batchSize = 200,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path cannot be null or empty.", nameof(path));
        }

        string normalizedPath = NormalizePath(path);

        if (batchSize <= 0) batchSize = 200;

        // Bounded channel to prevent background producer from outrunning consumer and bloating memory
        var channel = Channel.CreateBounded<IReadOnlyList<FileEntry>>(new BoundedChannelOptions(capacity: 8)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = true,
            SingleReader = true
        });

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var producerCt = linkedCts.Token;

        // Start producer on a clean ThreadPool thread, completely independent of caller's SynchronizationContext
        _ = Task.Run(async () =>
        {
            try
            {
                if (!Directory.Exists(normalizedPath))
                {
                    channel.Writer.Complete(new DirectoryNotFoundException($"Directory not found: '{path}'"));
                    return;
                }

                var dirInfo = new DirectoryInfo(normalizedPath);

                // Explicitly probe root directory with IgnoreInaccessible = false so access denied on root
                // throws UnauthorizedAccessException instead of silently returning empty
                try
                {
                    var probeOptions = new EnumerationOptions
                    {
                        IgnoreInaccessible = false,
                        RecurseSubdirectories = false,
                        ReturnSpecialDirectories = false
                    };
                    using var probe = dirInfo.EnumerateFileSystemInfos("*", probeOptions).GetEnumerator();
                    _ = probe.MoveNext();
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    channel.Writer.Complete(ex);
                    return;
                }

                var options = new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = false,
                    ReturnSpecialDirectories = false,
                    AttributesToSkip = 0 // Show all files in a file manager
                };

                IEnumerator<FileSystemInfo> enumerator;
                try
                {
                    enumerator = dirInfo.EnumerateFileSystemInfos("*", options).GetEnumerator();
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    channel.Writer.Complete(ex);
                    return;
                }

                using (enumerator)
                {
                    var buffer = new List<FileEntry>(batchSize);
                    int currentTarget = Math.Min(50, batchSize);

                    while (true)
                    {
                        producerCt.ThrowIfCancellationRequested();

                        FileSystemInfo current;
                        try
                        {
                            if (!enumerator.MoveNext())
                            {
                                break;
                            }
                            current = enumerator.Current;
                        }
                        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                        {
                            continue;
                        }

                        FileEntry? entry = null;
                        try
                        {
                            bool isDirectory = (current.Attributes & FileAttributes.Directory) == FileAttributes.Directory;
                            long size = 0;

                            if (!isDirectory && current is FileInfo fileInfo)
                            {
                                try
                                {
                                    size = fileInfo.Length;
                                }
                                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                                {
                                    size = 0;
                                }
                            }

                            DateTime modified = DateTime.MinValue;
                            try
                            {
                                modified = current.LastWriteTime;
                            }
                            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                            {
                                modified = DateTime.MinValue;
                            }

                            entry = new FileEntry(current.Name, current.FullName, isDirectory, size, modified);
                        }
                        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                        {
                            continue;
                        }

                        if (entry != null)
                        {
                            buffer.Add(entry);
                            if (buffer.Count >= currentTarget)
                            {
                                var chunk = buffer.ToArray();
                                buffer.Clear();
                                await channel.Writer.WriteAsync(chunk, producerCt).ConfigureAwait(false);
                                if (currentTarget < batchSize)
                                {
                                    currentTarget = Math.Min(currentTarget * 2, batchSize);
                                }
                            }
                        }
                    }

                    if (buffer.Count > 0)
                    {
                        await channel.Writer.WriteAsync(buffer.ToArray(), producerCt).ConfigureAwait(false);
                    }
                }

                channel.Writer.Complete();
            }
            catch (Exception ex)
            {
                channel.Writer.Complete(ex);
            }
        }, CancellationToken.None);

        try
        {
            while (await channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (channel.Reader.TryRead(out var batch))
                {
                    ct.ThrowIfCancellationRequested();
                    yield return batch;
                }
            }

            // Propagate any exceptions that occurred inside the producer task
            await channel.Reader.Completion.ConfigureAwait(false);
        }
        finally
        {
            // If the consumer broke out of the loop early, cancel producer
            linkedCts.Cancel();
        }
    }

    private static string NormalizePath(string path)
    {
        string fullPath = Path.GetFullPath(path);

        if (OperatingSystem.IsWindows() && fullPath.Length >= 240 && !fullPath.StartsWith(@"\\?\") && !fullPath.StartsWith(@"\\.\"))
        {
            if (fullPath.StartsWith(@"\\"))
            {
                return @"\\?\UNC\" + fullPath.Substring(2);
            }
            return @"\\?\" + fullPath;
        }

        return fullPath;
    }
}
