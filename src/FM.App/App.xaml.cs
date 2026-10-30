using System.Windows;
using FM.App.Services;
using FM.App.ViewModels;
using FM.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace FM.App;

/// <summary>
/// Application entry point configuring dependency injection.
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        // Load settings once at application startup
        var settingsService = _serviceProvider.GetRequiredService<ISettingsService>();
        var settings = settingsService.LoadSettings();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();

        ApplyWindowBounds(mainWindow, settings);

        mainWindow.Show();

        // Initialize view model paths asynchronously without blocking UI startup
        var mainViewModel = _serviceProvider.GetRequiredService<MainViewModel>();
        await mainViewModel.InitializeSettingsAsync(settings);
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
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // 1. Core Services
        services.AddSingleton<IDirectoryService, DirectoryService>();
        services.AddSingleton<FM.Core.FileSystem.IFileSystem, FM.Core.FileSystem.PhysicalFileSystem>();

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appDataRoot = System.IO.Path.Combine(localAppData, "MyFileManager");
        string stagingDir = System.IO.Path.Combine(appDataRoot, "staging");
        string journalFile = System.IO.Path.Combine(appDataRoot, "journal", "undo-journal.jsonl");

        services.AddSingleton<FM.Core.Operations.Transfer.IStagingBackupStore>(sp =>
            new FM.Core.Operations.Transfer.StagingBackupStore(sp.GetRequiredService<FM.Core.FileSystem.IFileSystem>(), stagingDir));

        services.AddSingleton<FM.Core.Operations.Transfer.IUndoJournalStore>(sp =>
            new FM.Core.Operations.Transfer.FileUndoJournalStore(sp.GetRequiredService<FM.Core.FileSystem.IFileSystem>(), journalFile));

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

        // 3. ViewModels
        services.AddSingleton<TransferDrawerViewModel>();
        services.AddSingleton<MainViewModel>();

        // 4. Views
        services.AddSingleton<MainWindow>();
    }
}
