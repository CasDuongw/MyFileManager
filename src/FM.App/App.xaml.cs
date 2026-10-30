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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        var settingsService = _serviceProvider.GetRequiredService<ISettingsService>();
        var settings = settingsService.LoadSettings();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();

        ApplyWindowBounds(mainWindow, settings);

        mainWindow.Show();
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

        // 2. Application Services
        services.AddSingleton<IIconService, IconService>();
        services.AddSingleton<ISettingsService, SettingsService>();

        // 3. ViewModels
        services.AddSingleton<MainViewModel>();

        // 4. Views
        services.AddSingleton<MainWindow>();
    }
}
