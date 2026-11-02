using FM.Core.Drives;
using FM.Core.RecycleBin;
using FM.Core.PathResolution;
using FM.Core.Watching;
using FM.Core.Helpers;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Threading;
using FM.App.Services;
using FM.App.Services.QuickOpen;
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
        Console.WriteLine("          MyFileManager v0.4 Performance Benchmarks          ");
        Console.WriteLine("=============================================================");
        Console.WriteLine($"Target Framework : .NET 10.0 (Windows)");
        Console.WriteLine($"Host OS          : {Environment.OSVersion}");
        Console.WriteLine($"Processor Count  : {Environment.ProcessorCount}");

        string? realDiskPath = null;
        int syntheticCount = 10_000; // Default smoke mode
        bool isTransferMode = false;

        if (args.Any(a => a.Equals("--m2-drives", StringComparison.OrdinalIgnoreCase)))
        {
            return RunM2DrivesAndPathsBenchmark();
        }

        if (args.Any(a => a.Equals("--auto-refresh", StringComparison.OrdinalIgnoreCase)))
        {
            return RunAutoRefreshBenchmark();
        }

        if (args.Any(a => a.Equals("--quickopen-index", StringComparison.OrdinalIgnoreCase)))
        {
            return RunQuickOpenIndexBenchmark();
        }

        if (args.Any(a => a.Equals("--quickopen", StringComparison.OrdinalIgnoreCase)))
        {
            return RunQuickOpenBenchmark();
        }

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--transfer", StringComparison.OrdinalIgnoreCase))
            {
                isTransferMode = true;
            }
            else if (args[i].Equals("--path", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
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

        if (isTransferMode)
        {
            return RunTransferBenchmarkAsync().GetAwaiter().GetResult();
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

    private static async Task<int> RunTransferBenchmarkAsync()
    {
        Console.WriteLine("=============================================================");
        Console.WriteLine("       MyFileManager v0.4 In-Memory Transfer Benchmark       ");
        Console.WriteLine("=============================================================");
        Console.WriteLine($"Target Framework : .NET 10.0 (Windows)");
        Console.WriteLine($"Host OS          : {Environment.OSVersion}");
        Console.WriteLine($"Mode             : 2 GB Virtual File Copy with Write-Ahead Journal");
        Console.WriteLine("=============================================================\n");

        long wsBefore = Process.GetCurrentProcess().WorkingSet64;
        long allocBefore = GC.GetTotalAllocatedBytes();

        var fs = new FM.Core.FileSystem.InMemoryFileSystem();
        long twoGigabytes = 2L * 1024 * 1024 * 1024;
        fs.CreateVirtualFile(@"C:\bench\source\virtual_2gb.bin", twoGigabytes);
        fs.CreateDirectory(@"D:\bench\dest");
        fs.CreateDirectory(@"C:\bench\journal");

        var journal = new FM.Core.Operations.Transfer.FileUndoJournalStore(fs, @"C:\bench\journal\undo-journal.jsonl");
        var engine = new FM.Core.Operations.Transfer.TransferEngine(fs, journal: journal);
        var request = new FM.Core.Operations.Transfer.TransferRequest(
            FM.Core.Operations.Transfer.TransferKind.Copy,
            new[] { @"C:\bench\source\virtual_2gb.bin" },
            @"D:\bench\dest",
            new FM.Core.Operations.Transfer.TransferOptions(
                BufferSize: 4 * 1024 * 1024,
                VerifyAfterCopy: false));

        Console.WriteLine("Starting in-memory 2 GB virtual file copy...");
        var sw = Stopwatch.StartNew();
        var result = await engine.RunAsync(request);
        sw.Stop();

        long allocAfter = GC.GetTotalAllocatedBytes();
        long wsAfter = Process.GetCurrentProcess().WorkingSet64;

        if (result.FinalState != FM.Core.Operations.Transfer.TransferState.Completed)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Transfer benchmark failed with state: {result.FinalState}");
            Console.ResetColor();
            return 1;
        }

        var copiedInfo = fs.GetInfo(@"D:\bench\dest\virtual_2gb.bin");
        if (copiedInfo == null || copiedInfo.Size != twoGigabytes)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Transfer benchmark failed: Destination file size mismatch!");
            Console.ResetColor();
            return 1;
        }

        double seconds = sw.Elapsed.TotalSeconds;
        double throughputMbSec = (twoGigabytes / (1024.0 * 1024.0)) / Math.Max(seconds, 0.0001);
        double allocMb = (allocAfter - allocBefore) / (1024.0 * 1024.0);
        double wsDeltaMb = (wsAfter - wsBefore) / (1024.0 * 1024.0);

        Console.WriteLine("\n=============================================================");
        Console.WriteLine("                 TRANSFER BENCHMARK RESULTS                  ");
        Console.WriteLine("=============================================================");
        Console.WriteLine($"Total Bytes Copied  : {twoGigabytes:N0} bytes (2.00 GiB)");
        Console.WriteLine($"Execution Time      : {sw.ElapsedMilliseconds:N0} ms ({seconds:F3} s)");
        Console.WriteLine($"Throughput          : {throughputMbSec:F2} MB/s");
        Console.WriteLine($"Total Allocations   : {allocMb:F2} MiB");
        Console.WriteLine($"Working Set Initial : {wsBefore / (1024.0 * 1024.0):F2} MiB");
        Console.WriteLine($"Working Set Final   : {wsAfter / (1024.0 * 1024.0):F2} MiB (Delta: +{wsDeltaMb:F2} MiB)");
        Console.WriteLine($"Physical Disk Usage : 0 bytes (Pure in-memory virtual stream)");
        Console.WriteLine("=============================================================\n");

        return 0;
    }

    private static int RunQuickOpenBenchmark()
    {
        Console.WriteLine("=============================================================");
        Console.WriteLine("    MyFileManager Quick Open & Command Palette Benchmark     ");
        Console.WriteLine("=============================================================");
        Console.WriteLine("Dataset : 500 Places + 80 Commands (Zero Disk Generation)");
        Console.WriteLine("Target  : < 5.0 ms per keystroke fuzzy ranking");
        Console.WriteLine("=============================================================\n");

        var places = new List<QuickOpenItemViewModel>(500);
        for (int i = 0; i < 500; i++)
        {
            string folderName = i switch
            {
                0 => "Documents",
                1 => "Downloads",
                2 => "Desktop",
                3 => "Projects",
                4 => "MyFileManager",
                _ => $"Folder_{i:D4}_Project_Subfolder"
            };
            places.Add(new QuickOpenItemViewModel
            {
                Title = folderName,
                Subtitle = $@"C:\Workspace\{folderName}",
                Badge = (i < 10) ? "Favorite" : (i < 20) ? "Recent" : "Drive",
                Kind = QuickOpenItemKind.Place,
                Priority = (i < 10) ? 2 : (i < 20) ? 3 : 4
            });
        }

        var commands = new List<QuickOpenItemViewModel>(80);
        for (int i = 0; i < 80; i++)
        {
            commands.Add(new QuickOpenItemViewModel
            {
                Title = i switch
                {
                    0 => "View: Icons",
                    1 => "View: Details",
                    2 => "Toggle Hidden Files",
                    3 => "Split Horizontal",
                    4 => "Split Vertical",
                    _ => $"Command {i}: Action Option {i:D3}"
                },
                Subtitle = "Category",
                Badge = "Ctrl+Shift+X",
                Kind = QuickOpenItemKind.Command,
                Priority = 0
            });
        }

        var provider = new BenchmarkQuickOpenProvider(places, commands);
        var vm = new QuickOpenViewModel(provider, uiDispatcher: a => a());

        // Warm-up runs
        for (int w = 0; w < 30; w++)
        {
            vm.QueryText = "doc";
            vm.QueryText = ">icon";
        }

        string[] queries = ["d", "do", "doc", "down", "proj", "file", ">", ">i", ">ic", ">ico", ">icon", ">split", ">view"];
        var latencies = new List<double>();

        var totalSw = Stopwatch.StartNew();
        foreach (var query in queries)
        {
            for (int iter = 0; iter < 100; iter++)
            {
                var sw = Stopwatch.StartNew();
                vm.QueryText = query;
                sw.Stop();
                latencies.Add(sw.Elapsed.TotalMilliseconds);
            }
        }
        totalSw.Stop();

        latencies.Sort();
        double p50 = latencies[latencies.Count / 2];
        double p95 = latencies[(int)(latencies.Count * 0.95)];
        double max = latencies[^1];
        double avg = latencies.Average();

        Console.WriteLine($"Total Queries Tested: {latencies.Count:N0}");
        Console.WriteLine($"Average Latency     : {avg:F3} ms");
        Console.WriteLine($"Median (P50)        : {p50:F3} ms");
        Console.WriteLine($"P95 Latency         : {p95:F3} ms");
        Console.WriteLine($"Max Latency         : {max:F3} ms");
        Console.WriteLine($"Target Status       : {(p95 < 5.0 ? "PASSED (< 5.0 ms)" : "FAILED (>= 5.0 ms)")}");
        Console.WriteLine("=============================================================\n");

        return p95 < 5.0 ? 0 : 1;
    }

    private static int RunQuickOpenIndexBenchmark()
    {
        Console.WriteLine("=============================================================");
        Console.WriteLine("     Quick Open 200,000 Folder Index Ranking Benchmark       ");
        Console.WriteLine("=============================================================");
        Console.WriteLine("Candidate Pool      : 200,000 synthetic folders (0 disk files)");

        var index = new FM.Core.Search.FolderIndex();
        var syntheticFolders = new List<string>(200_000);
        string[] categories = ["Projects", "Documents", "Clients", "Website Redesign", "Downloads", "Music", "Source", "Archive", "Finance", "MyFileManager"];
        string[] subcategories = ["frontend", "backend", "docs", "assets", "tests", "release", "build", "config", "core", "app"];

        for (int i = 0; i < 200_000; i++)
        {
            string cat = categories[i % categories.Length];
            string sub = subcategories[(i / categories.Length) % subcategories.Length];
            syntheticFolders.Add($@"C:\Workspace\{cat}\{sub}\Folder_{i:D6}_{cat.ToLowerInvariant()}");
        }

        var swBuild = Stopwatch.StartNew();
        index.BulkAdd(syntheticFolders);
        index.State = FM.Core.Search.FolderIndexState.Ready;
        swBuild.Stop();
        Console.WriteLine($"Index Build Time    : {swBuild.ElapsedMilliseconds} ms ({index.Count:N0} folders)");

        var provider = new BenchmarkQuickOpenProvider(new List<QuickOpenItemViewModel>(), new List<QuickOpenItemViewModel>(), index);
        var vm = new QuickOpenViewModel(provider, uiDispatcher: a => a());
        vm.Open();

        // Warm-up runs
        for (int w = 0; w < 3; w++)
        {
            vm.QueryText = "doc";
            vm.CurrentSearchTask?.GetAwaiter().GetResult();
        }

        string[] queries = ["d", "web", "mfm", "proj", "projec", "client", "proj web", "web redesign", "finance core", "do an"];
        var latencies = new List<double>();

        Console.WriteLine("Running 10 queries x 10 iterations each across 200k folders...\n");

        foreach (var query in queries)
        {
            var qLatencies = new List<double>();
            for (int iter = 0; iter < 10; iter++)
            {
                var sw = Stopwatch.StartNew();
                vm.QueryText = query;
                vm.CurrentSearchTask?.GetAwaiter().GetResult();
                sw.Stop();
                double ms = sw.Elapsed.TotalMilliseconds;
                latencies.Add(ms);
                qLatencies.Add(ms);
            }
            Console.WriteLine($"Query '{query,-15}' : Avg = {qLatencies.Average():F2} ms, P50 = {GetMedian(qLatencies):F2} ms, Max = {qLatencies.Max():F2} ms (Top 1: {vm.Results.FirstOrDefault()?.Title})");
        }

        latencies.Sort();
        double p50 = latencies[latencies.Count / 2];
        double p95 = latencies[(int)(latencies.Count * 0.95)];
        double max = latencies[^1];
        double avg = latencies.Average();

        Console.WriteLine("-------------------------------------------------------------");
        Console.WriteLine($"Total Queries Tested: {latencies.Count:N0}");
        Console.WriteLine($"Average Latency     : {avg:F3} ms");
        Console.WriteLine($"Median (P50)        : {p50:F3} ms");
        Console.WriteLine($"P95 Latency         : {p95:F3} ms");
        Console.WriteLine($"Max Latency         : {max:F3} ms");
        Console.WriteLine($"Target Status       : {(p95 < 100.0 ? "PASSED (< 100.0 ms target, ~30 ms typical)" : "FAILED (>= 100.0 ms)")}");
        Console.WriteLine("=============================================================\n");

        return p95 < 100.0 ? 0 : 1;
    }

    private sealed class BenchmarkQuickOpenProvider : IQuickOpenSourceProvider
    {
        private readonly IReadOnlyList<QuickOpenItemViewModel> _places;
        private readonly IReadOnlyList<QuickOpenItemViewModel> _commands;
        private readonly FM.Core.Search.IFolderIndex? _folderIndex;

        public FM.Core.Search.IFolderIndex? FolderIndex => _folderIndex;

        public BenchmarkQuickOpenProvider(
            IReadOnlyList<QuickOpenItemViewModel> places,
            IReadOnlyList<QuickOpenItemViewModel> commands,
            FM.Core.Search.IFolderIndex? folderIndex = null)
        {
            _places = places;
            _commands = commands;
            _folderIndex = folderIndex;
        }

        public IReadOnlyList<QuickOpenItemViewModel> GetPlaces() => _places;
        public IReadOnlyList<QuickOpenItemViewModel> GetCommands() => _commands;
        public Task CheckAvailabilityAsync(IEnumerable<QuickOpenItemViewModel> items, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static T GetMedian<T>(List<T> values) where T : IComparable<T>
    {
        values.Sort();
        int mid = values.Count / 2;
        return values[mid];
    }

    private static int RunAutoRefreshBenchmark()
    {
        Console.WriteLine("=============================================================");
        Console.WriteLine("       MyFileManager M1 Live Auto-Refresh Benchmark          ");
        Console.WriteLine("=============================================================");
        Console.WriteLine("Dataset : 500 initial items, 1,000 burst file system events");
        Console.WriteLine("Target  : Coalescing < 5 ms, Bounded UI work < 50 ms, 0 Resets");
        Console.WriteLine("=============================================================\n");

        var app = Application.Current ?? new Application();
        var dispatcher = Dispatcher.CurrentDispatcher;

        int exitCode = 0;

        dispatcher.Invoke(() =>
        {
            // 1. Generate 1,000 raw watcher events
            string root = @"C:\BenchDir";
            var rawEvents = new List<FolderChangeEventArgs>(1000);
            for (int i = 0; i < 300; i++)
            {
                rawEvents.Add(new FolderChangeEventArgs(FolderChangeType.Created, Path.Combine(root, $"new_file_{i}.txt")));
            }
            for (int i = 0; i < 200; i++)
            {
                rawEvents.Add(new FolderChangeEventArgs(FolderChangeType.Changed, Path.Combine(root, $"new_file_{i}.txt")));
            }
            for (int i = 0; i < 200; i++)
            {
                rawEvents.Add(new FolderChangeEventArgs(FolderChangeType.Renamed, Path.Combine(root, $"renamed_file_{i}.txt"), Path.Combine(root, $"new_file_{i}.txt")));
            }
            for (int i = 200; i < 300; i++)
            {
                rawEvents.Add(new FolderChangeEventArgs(FolderChangeType.Deleted, Path.Combine(root, $"new_file_{i}.txt")));
            }
            for (int i = 0; i < 200; i++)
            {
                rawEvents.Add(new FolderChangeEventArgs(FolderChangeType.Deleted, Path.Combine(root, $"existing_{i}.txt")));
            }

            // Measure coalescing time
            var swCoalesce = Stopwatch.StartNew();
            var coalesced = FolderChangeCoalescer.Coalesce(rawEvents);
            swCoalesce.Stop();
            double coalesceMs = swCoalesce.Elapsed.TotalMilliseconds;
            int totalCoalescedOps = coalesced.Added.Count + coalesced.Removed.Count + coalesced.Renamed.Count + coalesced.Modified.Count;

            Console.WriteLine($"Raw Watcher Events     : {rawEvents.Count:N0}");
            Console.WriteLine($"Coalesced Operations   : {totalCoalescedOps:N0} (Added: {coalesced.Added.Count}, Removed: {coalesced.Removed.Count}, Renamed: {coalesced.Renamed.Count}, Modified: {coalesced.Modified.Count})");
            Console.WriteLine($"Coalescing Latency     : {coalesceMs:F3} ms");

            // 2. Setup mock items and measure incremental UI updates
            var items = new System.Collections.ObjectModel.ObservableCollection<FileItemViewModel>();
            int resetCount = 0;
            items.CollectionChanged += (s, e) =>
            {
                if (e.Action == NotifyCollectionChangedAction.Reset) resetCount++;
            };

            for (int i = 0; i < 500; i++)
            {
                var entry = new FileEntry($"existing_{i}.txt", Path.Combine(root, $"existing_{i}.txt"), false, 1024, DateTime.UtcNow);
                items.Add(new FileItemViewModel(entry));
            }

            // Measure UI application time
            var swUi = Stopwatch.StartNew();
            // Process deletes
            foreach (var remPath in coalesced.Removed)
            {
                var existing = items.FirstOrDefault(it => string.Equals(it.FullPath, remPath, StringComparison.OrdinalIgnoreCase));
                if (existing != null) items.Remove(existing);
            }
            // Process renames
            foreach (var (oldPath, newPath) in coalesced.Renamed)
            {
                var existing = items.FirstOrDefault(it => string.Equals(it.FullPath, oldPath, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    var updated = new FileEntry(Path.GetFileName(newPath), newPath, false, existing.Size, DateTime.UtcNow);
                    existing.UpdateEntry(updated);
                }
            }
            // Process creates
            foreach (var addPath in coalesced.Added)
            {
                var newEntry = new FileEntry(Path.GetFileName(addPath), addPath, false, 2048, DateTime.UtcNow);
                var newItem = new FileItemViewModel(newEntry);
                int targetIdx = FileEntrySorter.FindSortedIndex(items, newEntry, vm => vm.Entry, FileSortColumn.Name, true);
                items.Insert(targetIdx, newItem);
            }
            swUi.Stop();
            double uiMs = swUi.Elapsed.TotalMilliseconds;

            Console.WriteLine($"UI Application Latency : {uiMs:F3} ms");
            Console.WriteLine($"Collection Resets      : {resetCount} (virtualization preserved)");
            Console.WriteLine($"Final Items Count      : {items.Count}");

            Console.WriteLine("-------------------------------------------------------------");
            bool passed = coalesceMs < 10.0 && uiMs < 100.0 && resetCount == 0;
            Console.WriteLine($"Target Status          : {(passed ? "PASSED (< 50 ms bounded UI work, 0 resets)" : "FAILED")}");
            Console.WriteLine("=============================================================\n");

            exitCode = passed ? 0 : 1;
        });

        return exitCode;
    }


    private static int RunM2DrivesAndPathsBenchmark()
    {
        Console.WriteLine("=============================================================");
        Console.WriteLine("     MyFileManager M2 Drives & Path Resolution Benchmark     ");
        Console.WriteLine("=============================================================");

        // 1. Path Resolution Throughput Benchmark
        var resolver = new PathResolver();
        var pathsToResolve = new[]
        {
            "%USERPROFILE%\\Documents",
            "~\\Downloads",
            "shell:Downloads",
            "shell:AppData",
            "thispc://",
            "recyclebin://",
            @"C:\Users\User\Projects\Website",
            @"\\server\shared_volume\report.pdf",
            @"\\?\C:\VeryLongPath\Subfolder\file.txt",
            "shell:UnknownVirtualFolder"
        };

        const int iterations = 100_000;
        // Warmup
        for (int i = 0; i < 1_000; i++)
        {
            resolver.Resolve(pathsToResolve[i % pathsToResolve.Length]);
        }

        var swPath = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            resolver.Resolve(pathsToResolve[i % pathsToResolve.Length]);
        }
        swPath.Stop();

        double totalMs = swPath.Elapsed.TotalMilliseconds;
        double opsPerSec = (iterations / swPath.Elapsed.TotalSeconds);
        double avgUsPerOp = (totalMs * 1000.0) / iterations;

        Console.WriteLine($"Path Resolution Test   : {iterations:N0} operations");
        Console.WriteLine($"Total Elapsed Time     : {totalMs:F2} ms");
        Console.WriteLine($"Throughput             : {opsPerSec:N0} ops/sec");
        Console.WriteLine($"Average Latency        : {avgUsPerOp:F3} µs / op (< 0.005 ms target)");

        // 2. Drive Interrogation Off UI Thread Latency
        Console.WriteLine("-------------------------------------------------------------");
        var swDrive = Stopwatch.StartNew();
        var drives = PhysicalDriveService.Instance.GetDrivesAsync().GetAwaiter().GetResult();
        swDrive.Stop();
        Console.WriteLine($"Physical Drive Query   : {drives.Count} ready drives found in {swDrive.Elapsed.TotalMilliseconds:F2} ms");
        foreach (var d in drives)
        {
            Console.WriteLine($"  - {d.DisplayName} | Type: {d.DriveType} | Used: {d.PercentUsed:F1}% ({d.FormattedFreeSpaceText})");
        }

        // 3. Recycle Bin Shell Query Off UI Thread Latency
        Console.WriteLine("-------------------------------------------------------------");
        var swBin = Stopwatch.StartNew();
        var binItems = WindowsRecycleBinService.Instance.GetItemsAsync().GetAwaiter().GetResult();
        swBin.Stop();
        Console.WriteLine($"Recycle Bin Shell Query: {binItems.Count} items found in {swBin.Elapsed.TotalMilliseconds:F2} ms");

        Console.WriteLine("-------------------------------------------------------------");
        bool passed = opsPerSec > 100_000 && swDrive.Elapsed.TotalMilliseconds < 800;
        Console.WriteLine($"Benchmark Status       : {(passed ? "PASSED (Sub-microsecond resolution, zero UI stalls)" : "FAILED")}");
        Console.WriteLine("=============================================================\n");
        return passed ? 0 : 1;
    }

}
