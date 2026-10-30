using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Threading;
using FM.App.Services;
using FM.App.ViewModels;
using FM.App.Views;
using FM.Core.Models;
using FM.Core.Services;

namespace FM.Benchmarks;

public sealed class SyntheticDirectoryService : IDirectoryService
{
    private readonly int _entryCount;

    public SyntheticDirectoryService(int entryCount)
    {
        _entryCount = entryCount;
    }

    public async IAsyncEnumerable<IReadOnlyList<FileEntry>> EnumerateBatchesAsync(
        string path,
        int batchSize = 150,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (batchSize <= 0) batchSize = 150;

        var channel = Channel.CreateBounded<IReadOnlyList<FileEntry>>(new BoundedChannelOptions(8)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = true,
            SingleReader = true
        });

        _ = Task.Run(async () =>
        {
            try
            {
                var buffer = new List<FileEntry>(batchSize);
                int currentTarget = Math.Min(50, batchSize);
                var now = DateTime.UtcNow;

                for (int i = 0; i < _entryCount; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    bool isDir = (i % 10 == 0);
                    string name = isDir ? $"Folder_{i:D6}" : $"File_{i:D6}.txt";
                    string fullPath = Path.Combine(path, name);
                    long size = isDir ? 0 : (i * 1024L);

                    buffer.Add(new FileEntry(name, fullPath, isDir, size, now));

                    if (buffer.Count >= currentTarget)
                    {
                        var chunk = buffer.ToArray();
                        buffer.Clear();
                        await channel.Writer.WriteAsync(chunk, ct).ConfigureAwait(false);
                        if (currentTarget < batchSize)
                        {
                            currentTarget = Math.Min(currentTarget * 2, batchSize);
                        }
                    }
                }

                if (buffer.Count > 0)
                {
                    await channel.Writer.WriteAsync(buffer.ToArray(), ct).ConfigureAwait(false);
                }

                channel.Writer.Complete();
            }
            catch (Exception ex)
            {
                channel.Writer.Complete(ex);
            }
        }, CancellationToken.None);

        while (await channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
        {
            while (channel.Reader.TryRead(out var batch))
            {
                ct.ThrowIfCancellationRequested();
                yield return batch;
            }
        }

        await channel.Reader.Completion.ConfigureAwait(false);
    }
}

public static class Program
{
    private record RunMetric(int RunIndex, long FirstBatchMs, long FullListMs, double MaxStallMs, int ResetEvents);

    [STAThread]
    public static int Main(string[] args)
    {
        Console.WriteLine("=============================================================");
        Console.WriteLine("          MyFileManager v0.2.1 Performance Benchmarks        ");
        Console.WriteLine("=============================================================");
        Console.WriteLine($"Target Framework : .NET 10.0 (Windows)");
        Console.WriteLine($"Host OS          : {Environment.OSVersion}");
        Console.WriteLine($"Processor Count  : {Environment.ProcessorCount}");

        string? realDiskPath = null;
        int syntheticCount = 10_000; // Default smoke mode

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--path", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                realDiskPath = args[++i];
            }
            else if (args[i].Equals("--medium", StringComparison.OrdinalIgnoreCase))
            {
                syntheticCount = 50_000;
            }
            else if (args[i].Equals("--large", StringComparison.OrdinalIgnoreCase))
            {
                syntheticCount = 200_000;
            }
        }

        bool isRealDisk = !string.IsNullOrEmpty(realDiskPath);
        if (isRealDisk)
        {
            if (!Directory.Exists(realDiskPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error: Provided path does not exist: '{realDiskPath}'");
                Console.ResetColor();
                return 1;
            }
            Console.WriteLine($"Mode             : Real-Disk READ-ONLY ('{realDiskPath}')");
        }
        else
        {
            Console.WriteLine($"Mode             : Synthetic In-Memory ({syntheticCount:N0} entries, 0 disk files)");
        }

        long memBefore = GC.GetTotalMemory(true);
        long wsBefore = Process.GetCurrentProcess().WorkingSet64;
        Console.WriteLine($"Working Set Initial: {wsBefore / (1024.0 * 1024.0):F2} MiB (GC Total: {memBefore / (1024.0 * 1024.0):F2} MiB)");
        Console.WriteLine("=============================================================\n");

        var app = new Application();
        var dispatcher = Dispatcher.CurrentDispatcher;
        int exitCode = 0;

        dispatcher.InvokeAsync(async () =>
        {
            try
            {
                // Host FilePaneView inside a real Window to measure real layout & virtualization overhead
                var window = new Window
                {
                    Title = "Benchmark View Host",
                    Width = 1024,
                    Height = 768,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };
                var paneView = new FilePaneView();
                window.Content = paneView;
                window.Show();

                var runs = new List<RunMetric>();

                Console.WriteLine("Running 1 warm-up run + 5 measurement runs...\n");

                for (int run = 0; run <= 5; run++)
                {
                    bool isWarmup = (run == 0);
                    string label = isWarmup ? "Warm-up" : $"Run #{run}";

                    IDirectoryService dirService = isRealDisk
                        ? new DirectoryService()
                        : new SyntheticDirectoryService(syntheticCount);

                    var iconService = new IconService();
                    var vm = new FilePaneViewModel(dirService, iconService);
                    paneView.DataContext = vm;

                    int resetCount = 0;
                    NotifyCollectionChangedEventHandler onColChanged = (s, e) =>
                    {
                        if (e.Action == NotifyCollectionChangedAction.Reset)
                        {
                            Interlocked.Increment(ref resetCount);
                        }
                    };
                    vm.Items.CollectionChanged += onColChanged;

                    long? firstBatchMs = null;
                    var sw = Stopwatch.StartNew();

                    double maxStallMs = 0;
                    long lastTick = Stopwatch.GetTimestamp();
                    const double intervalMs = 16.0;

                    var timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher)
                    {
                        Interval = TimeSpan.FromMilliseconds(intervalMs)
                    };

                    timer.Tick += (s, e) =>
                    {
                        long currentTick = Stopwatch.GetTimestamp();
                        double elapsed = (double)(currentTick - lastTick) * 1000.0 / Stopwatch.Frequency;
                        lastTick = currentTick;

                        double stall = Math.Max(0, elapsed - intervalMs);
                        if (stall > maxStallMs)
                        {
                            maxStallMs = stall;
                        }
                    };

                    void OnItemsFirstBatch(object? s, NotifyCollectionChangedEventArgs e)
                    {
                        if (vm.Items.Count > 0 && !firstBatchMs.HasValue)
                        {
                            firstBatchMs = sw.ElapsedMilliseconds;
                        }
                    }
                    vm.Items.CollectionChanged += OnItemsFirstBatch;

                    lastTick = Stopwatch.GetTimestamp();
                    timer.Start();

                    string target = isRealDisk ? realDiskPath! : @"C:\SyntheticBenchmarkFolder";
                    await vm.NavigateInternalAsync(target, NavigationPhaseType.New);

                    sw.Stop();
                    timer.Stop();
                    vm.Items.CollectionChanged -= OnItemsFirstBatch;
                    vm.Items.CollectionChanged -= onColChanged;

                    long fullMs = sw.ElapsedMilliseconds;
                    long firstMs = firstBatchMs ?? fullMs;

                    var metric = new RunMetric(run, firstMs, fullMs, maxStallMs, resetCount);
                    if (!isWarmup)
                    {
                        runs.Add(metric);
                    }

                    Console.WriteLine($"[{label,7}] Items: {vm.Items.Count,7:N0} | First Batch: {firstMs,4} ms | Full List: {fullMs,5} ms | Max Stall: {maxStallMs,5:F2} ms | Resets: {resetCount}");

                    // Clean up and allow brief UI dispatch between runs
                    paneView.DataContext = null;
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    await Task.Delay(100);
                }

                // Compute median of the 5 runs
                long medianFirstBatch = GetMedian(runs.Select(r => r.FirstBatchMs).ToList());
                long medianFullList = GetMedian(runs.Select(r => r.FullListMs).ToList());
                double medianMaxStall = GetMedian(runs.Select(r => r.MaxStallMs).ToList());
                int medianResets = (int)GetMedian(runs.Select(r => (long)r.ResetEvents).ToList());

                Console.WriteLine("\n=============================================================");
                Console.WriteLine("                 MEDIAN SUMMARY (5 RUNS)                     ");
                Console.WriteLine("=============================================================");
                Console.WriteLine($"First Batch Arrival : {medianFirstBatch:N0} ms");
                Console.WriteLine($"Full List Completed : {medianFullList:N0} ms");
                Console.WriteLine($"Max UI Stall (16ms) : {medianMaxStall:F2} ms");
                Console.WriteLine($"Reset Events Count  : {medianResets} (Target: <= 1 per navigation)");
                Console.WriteLine("=============================================================");

                long memAfter = GC.GetTotalMemory(true);
                long wsAfter = Process.GetCurrentProcess().WorkingSet64;
                Console.WriteLine($"Working Set Final   : {wsAfter / (1024.0 * 1024.0):F2} MiB (Delta: +{(wsAfter - wsBefore) / (1024.0 * 1024.0):F2} MiB)");
                Console.WriteLine($"GC Total Memory     : {memAfter / (1024.0 * 1024.0):F2} MiB");
                Console.WriteLine("=============================================================");

                window.Close();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\nBenchmark failed with exception: {ex}");
                Console.ResetColor();
                exitCode = 1;
            }
            finally
            {
                dispatcher.InvokeShutdown();
            }
        });

        Dispatcher.Run();
        return exitCode;
    }

    private static T GetMedian<T>(List<T> values) where T : IComparable<T>
    {
        values.Sort();
        int mid = values.Count / 2;
        return values[mid];
    }
}
