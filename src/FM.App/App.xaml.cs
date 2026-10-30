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

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
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
