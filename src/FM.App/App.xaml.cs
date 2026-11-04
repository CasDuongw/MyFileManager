using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using FM.App.Helpers;
using FM.App.Services;
using FM.App.Services.SingleInstance;
using FM.App.ViewModels;
using FM.App.Views;
using FM.Core.CommandLine;
using FM.Core.Logging;
using FM.Core.Services;
using FM.Core.ShellIntegration;
using Microsoft.Extensions.DependencyInjection;

namespace FM.App;

/// <summary>
/// Application entry point configuring dependency injection, single instance IPC,
/// global crash handling, and command-line argument processing.
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _serviceProvider;
    private ISingleInstanceService? _singleInstanceService;
    private ICrashLogger? _crashLogger;

    protected override async void OnStartup(StartupEventArgs e)
    {
        // 1. Single Instance check: forward arguments and exit if already running
        _singleInstanceService = new NamedPipeSingleInstanceService();
        if (!_singleInstanceService.IsFirstInstance)
        {
            try
            {
                _ = _singleInstanceService.ForwardArgsToRunningInstanceAsync(e.Args).GetAwaiter().GetResult();
            }
            catch { }
            Shutdown(0);
            return;
        }

        base.OnStartup(e);

        // 2. Setup Global Crash Safety Handlers & Logging
        _crashLogger = new CrashLogger();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // Purge old crash logs asynchronously
        _ = _crashLogger.PurgeOldLogsAsync(5);

        // 3. Configure DI Container
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        // 4. Start IPC Server for receiving forwarded command line args
        _ = _singleInstanceService.StartServerAsync(async forwardedArgs =>
        {
            var dispatcher = Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.HasShutdownStarted)
            {
                await dispatcher.InvokeAsync(async () =>
                {
                    if (_serviceProvider != null)
                    {
                        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
                        var mainViewModel = _serviceProvider.GetRequiredService<MainViewModel>();
                        await mainViewModel.HandleCommandLineArgsAsync(forwardedArgs);
                        WindowActivationHelper.BringToFront(mainWindow);
                    }
                });
            }
        });

        // 5. Load settings once at application startup
        var settingsService = _serviceProvider.GetRequiredService<ISettingsService>();
        var settings = settingsService.LoadSettings();

        var win = _serviceProvider.GetRequiredService<MainWindow>();
        ApplyWindowBounds(win, settings);
        win.Show();

        // 6. Initialize view model paths and process initial command line args
        var mainVm = _serviceProvider.GetRequiredService<MainViewModel>();
        await mainVm.InitializeSettingsAsync(settings);

        if (e.Args != null && e.Args.Length > 0)
        {
            await mainVm.HandleCommandLineArgsAsync(e.Args);
        }

        // 7. Run crash recovery and staging retention purge asynchronously
        var fs = _serviceProvider.GetRequiredService<FM.Core.FileSystem.IFileSystem>();
        var stager = _serviceProvider.GetRequiredService<FM.Core.Operations.Transfer.IStagingBackupStore>();
        var journal = _serviceProvider.GetRequiredService<FM.Core.Operations.Transfer.IUndoJournalStore>();

        _ = Task.Run(async () =>
        {
            try
            {
                if (journal is FM.Core.Operations.Transfer.FileUndoJournalStore fjs)
                {
                    await fjs.RecoverPendingEntriesAsync(fs, stager).ConfigureAwait(false);
                    await fjs.CompactAsync(TimeSpan.FromDays(7)).ConfigureAwait(false);
                }
                var committed = await journal.LoadEntriesAsync().ConfigureAwait(false);
                var liveIds = new HashSet<Guid>(committed.Select(c => c.OperationId));
                await stager.PurgeAsync(FM.Core.Operations.Transfer.StagingPolicy.Default, liveIds).ConfigureAwait(false);
            }
            catch
            {
                // Background maintenance failure handled non-fatally
            }
        });
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _crashLogger?.LogExceptionSync(e.Exception, "WPF DispatcherUnhandledException");

        try
        {
            var dialog = new CrashReportDialog(e.Exception, _crashLogger?.LogDirectory ?? "", canContinue: true);
            dialog.ShowDialog();

            if (dialog.ShouldRestart)
            {
                RestartApplication();
                e.Handled = true;
                Shutdown(1);
            }
            else if (dialog.ShouldContinue)
            {
                e.Handled = true;
            }
            else
            {
                e.Handled = true;
                Shutdown(1);
            }
        }
        catch
        {
            e.Handled = true;
            Shutdown(1);
        }
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            _crashLogger?.LogExceptionSync(ex, $"AppDomain UnhandledException (IsTerminating={e.IsTerminating})");
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _crashLogger?.LogExceptionSync(e.Exception, "TaskScheduler UnobservedTaskException");
        e.SetObserved();
    }

    private static void RestartApplication()
    {
        try
        {
            string? exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true
                });
            }
        }
        catch { }
    }

    private static void ApplyWindowBounds(Window window, AppSettings settings)
    {
        double vLeft = SystemParameters.VirtualScreenLeft;
        double vTop = SystemParameters.VirtualScreenTop;
        double vWidth = SystemParameters.VirtualScreenWidth;
        double vHeight = SystemParameters.VirtualScreenHeight;

        double width = Math.Min(settings.WindowWidth, vWidth);
        double height = Math.Min(settings.WindowHeight, vHeight);

        window.Width = width;
        window.Height = height;

        if (settings.WindowLeft.HasValue && settings.WindowTop.HasValue)
        {
            double left = settings.WindowLeft.Value;
            double top = settings.WindowTop.Value;

            // Clamp coordinates so the window never opens completely off-screen after monitor changes
            if (left < vLeft) left = vLeft;
            if (top < vTop) top = vTop;
            if (left + width > vLeft + vWidth) left = Math.Max(vLeft, vLeft + vWidth - width);
            if (top + height > vTop + vHeight) top = Math.Max(vTop, vTop + vHeight - height);

            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = left;
            window.Top = top;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        if (Enum.TryParse<WindowState>(settings.WindowState, ignoreCase: true, out var state) && state != WindowState.Minimized)
        {
            window.WindowState = state;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceService?.Dispose();
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // 1. Core Services
        services.AddSingleton<IDirectoryService, DirectoryService>();
        services.AddSingleton<FM.Core.FileSystem.IFileSystem, FM.Core.FileSystem.PhysicalFileSystem>();

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appDataRoot = System.IO.Path.Combine(localAppData, "MyFileManager");
        string stagingDir = System.IO.Path.Combine(appDataRoot, "staging");
        string journalFile = System.IO.Path.Combine(appDataRoot, "journal", "undo-journal.jsonl");

        services.AddSingleton<FM.Core.Operations.Transfer.IUndoJournalStore>(sp =>
            new FM.Core.Operations.Transfer.FileUndoJournalStore(sp.GetRequiredService<FM.Core.FileSystem.IFileSystem>(), journalFile));

        services.AddSingleton<FM.Core.Operations.Transfer.IStagingBackupStore>(sp =>
            new FM.Core.Operations.Transfer.StagingBackupStore(
                sp.GetRequiredService<FM.Core.FileSystem.IFileSystem>(),
                stagingDir,
                sp.GetRequiredService<FM.Core.Operations.Transfer.IUndoJournalStore>()));

        services.AddSingleton<FM.Core.Operations.Transfer.ITransferQueue>(sp =>
            new FM.Core.Operations.Transfer.TransferQueue(
                sp.GetRequiredService<FM.Core.FileSystem.IFileSystem>(),
                sp.GetRequiredService<FM.Core.Operations.Transfer.IStagingBackupStore>(),
                sp.GetRequiredService<FM.Core.Operations.Transfer.IUndoJournalStore>()));

        // 2. Application Services
        services.AddSingleton<IIconService, IconService>();
        services.AddSingleton<IThumbnailService, ThumbnailService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IInputDialogService, InputDialogService>();
        services.AddSingleton<IShellLauncher, ShellLauncher>();
        services.AddSingleton<ITransferDialogService, TransferDialogService>();
        services.AddSingleton<FM.Core.Watching.IFolderWatcherFactory, FM.Core.Watching.PhysicalFolderWatcherFactory>();
        services.AddSingleton<FM.Core.Drives.IDriveService, FM.Core.Drives.PhysicalDriveService>();
        services.AddSingleton<FM.Core.RecycleBin.IRecycleBinService, FM.Core.RecycleBin.WindowsRecycleBinService>();
        services.AddSingleton<FM.Core.PathResolution.IPathResolver, FM.Core.PathResolution.PathResolver>();
        services.AddSingleton<ICrashLogger>(_crashLogger ?? new CrashLogger());
        services.AddSingleton<ISingleInstanceService>(_singleInstanceService ?? new NamedPipeSingleInstanceService());
        services.AddSingleton<IShellRegistrationService, FakeShellRegistrationService>();
        services.AddSingleton<ICommandLineParser, CommandLineParser>();
        services.AddSingleton<FM.Core.Search.ISearchService, FM.Core.Search.SearchService>();
        services.AddSingleton<FM.Core.ShellIntegration.IShellNewService, WindowsShellNewService>();
        services.AddSingleton<FM.Core.ShellIntegration.ISendToService, WindowsSendToService>();
        services.AddSingleton<FM.Core.Operations.MultiRename.IMultiRenameDialogService, WindowsMultiRenameDialogService>();
        services.AddSingleton<FM.Core.Elevation.IElevationService, FM.App.Services.Elevation.WindowsElevationService>();

        // 3. ViewModels
        services.AddSingleton<TransferDrawerViewModel>();
        services.AddSingleton<MainViewModel>();

        // 4. Views
        services.AddSingleton<MainWindow>();
    }
}
