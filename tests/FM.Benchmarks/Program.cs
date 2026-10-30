using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using FM.App.Services;
using FM.App.ViewModels;
using FM.Core.Services;

namespace FM.Benchmarks;

public static class Program
{
    private record BenchmarkResult(int FileCount, long TimeToFirstBatchMs, long TimeToFullListMs, double MaxStallMs);

    [STAThread]
    public static int Main(string[] args)
    {
        Console.WriteLine("=============================================================");
        Console.WriteLine("           MyFileManager v0.2 Performance Benchmarks         ");
        Console.WriteLine("=============================================================");
        Console.WriteLine("Target Framework: .NET 10.0 (Windows)");
        Console.WriteLine($"Host OS: {Environment.OSVersion}");
        Console.WriteLine($"Processor Count: {Environment.ProcessorCount}");
        Console.WriteLine();

        var dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));

        int exitCode = 0;
        var results = new List<BenchmarkResult>();

        dispatcher.InvokeAsync(async () =>
        {
            try
            {
                int[] counts = { 10_000, 50_000, 200_000 };

                foreach (int count in counts)
                {
                    var result = await RunBenchmarkForCountAsync(count, dispatcher);
                    results.Add(result);
                }

                Console.WriteLine();
                Console.WriteLine("=============================================================");
                Console.WriteLine("                     BENCHMARK RESULTS                       ");
                Console.WriteLine("=============================================================");
                Console.WriteLine($"| {"File Count",12} | {"First Batch (ms)",16} | {"Full List (ms)",14} | {"Max UI Stall (ms)",18} |");
                Console.WriteLine($"|{new string('-', 14)}|{new string('-', 18)}|{new string('-', 16)}|{new string('-', 20)}|");

                foreach (var r in results)
                {
                    Console.WriteLine($"| {r.FileCount,12:N0} | {r.TimeToFirstBatchMs,16:N0} | {r.TimeToFullListMs,14:N0} | {r.MaxStallMs,18:F2} |");
                }
                Console.WriteLine("=============================================================");
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

    private static async Task<BenchmarkResult> RunBenchmarkForCountAsync(int count, Dispatcher dispatcher)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"FM_Bench_{count}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        Console.Write($"[1/3] Generating {count:N0} files in temp folder... ");
        var genSw = Stopwatch.StartNew();

        Parallel.For(0, count, i =>
        {
            string filePath = Path.Combine(tempDir, $"file_{i:D6}.tmp");
            using (File.Create(filePath)) { }
        });

        genSw.Stop();
        Console.WriteLine($"Done ({genSw.ElapsedMilliseconds:N0} ms).");

        Console.Write($"[2/3] Measuring enumeration, streaming, and UI stall... ");

        var dirService = new DirectoryService();
        var iconService = new IconService();
        var viewModel = new FilePaneViewModel(dirService, iconService);

        long? firstBatchMs = null;
        var sw = Stopwatch.StartNew();

        // Setup stall monitor with DispatcherTimer
        double maxStallMs = 0;
        long lastTick = Stopwatch.GetTimestamp();
        double timerIntervalMs = 5.0;

        var timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(timerIntervalMs)
        };

        timer.Tick += (s, e) =>
        {
            long currentTick = Stopwatch.GetTimestamp();
            double elapsedMs = (double)(currentTick - lastTick) * 1000.0 / Stopwatch.Frequency;
            lastTick = currentTick;

            double stall = Math.Max(0, elapsedMs - timerIntervalMs);
            if (stall > maxStallMs)
            {
                maxStallMs = stall;
            }
        };

        // Listen for the first batch arrival
        void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (viewModel.Items.Count > 0 && !firstBatchMs.HasValue)
            {
                firstBatchMs = sw.ElapsedMilliseconds;
            }
        }

        viewModel.Items.CollectionChanged += OnItemsChanged;

        lastTick = Stopwatch.GetTimestamp();
        timer.Start();

        // Perform navigation
        await viewModel.NavigateInternalAsync(tempDir, NavigationPhaseType.New);

        sw.Stop();
        timer.Stop();
        viewModel.Items.CollectionChanged -= OnItemsChanged;

        long fullListMs = sw.ElapsedMilliseconds;
        long recordedFirstBatchMs = firstBatchMs ?? fullListMs;

        Console.WriteLine($"Done.");
        Console.WriteLine($"      -> Loaded: {viewModel.Items.Count:N0} items");
        Console.WriteLine($"      -> First Batch: {recordedFirstBatchMs} ms");
        Console.WriteLine($"      -> Full List: {fullListMs} ms");
        Console.WriteLine($"      -> Max UI Stall: {maxStallMs:F2} ms");

        Console.Write($"[3/3] Cleaning up temp files... ");
        var delSw = Stopwatch.StartNew();
        try
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Cleanup warning: {ex.Message}");
        }
        delSw.Stop();
        Console.WriteLine($"Done ({delSw.ElapsedMilliseconds:N0} ms).\n");

        return new BenchmarkResult(count, recordedFirstBatchMs, fullListMs, maxStallMs);
    }
}
