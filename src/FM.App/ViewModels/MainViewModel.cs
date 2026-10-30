using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FM.App.Services;
using FM.Core.Services;

namespace FM.App.ViewModels;

/// <summary>
/// Root ViewModel orchestrating dual file panes, the sidebar, active pane state, and settings persistence.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly IDirectoryService _directoryService;
    private readonly IIconService _iconService;
    private readonly ISettingsService _settingsService;

    public FilePaneViewModel LeftPane { get; }
    public FilePaneViewModel RightPane { get; }

    [ObservableProperty]
    private FilePaneViewModel _activePane;

    public ObservableCollection<SidebarItemViewModel> SidebarFavorites { get; } = new();
    public ObservableCollection<SidebarItemViewModel> SidebarDrives { get; } = new();

    public MainViewModel(
        IDirectoryService directoryService,
        IIconService iconService,
        ISettingsService settingsService)
    {
        _directoryService = directoryService;
        _iconService = iconService;
        _settingsService = settingsService;

        LeftPane = new FilePaneViewModel(directoryService, iconService);
        RightPane = new FilePaneViewModel(directoryService, iconService);

        LeftPane.Activated += OnPaneActivated;
        RightPane.Activated += OnPaneActivated;

        // Default to LeftPane active
        _activePane = LeftPane;
        LeftPane.IsActive = true;
        RightPane.IsActive = false;

        _ = PopulateSidebarAsync();
    }

    private void OnPaneActivated(FilePaneViewModel pane)
    {
        SetActivePane(pane);
    }

    public void SetActivePane(FilePaneViewModel pane)
    {
        if (ActivePane == pane) return;

        ActivePane = pane;
        LeftPane.IsActive = (pane == LeftPane);
        RightPane.IsActive = (pane == RightPane);
    }

    [RelayCommand]
    public void SwitchActivePane()
    {
        SetActivePane(ActivePane == LeftPane ? RightPane : LeftPane);
    }

    [RelayCommand]
    public void NavigateActivePane(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            ActivePane.Navigate(path);
        }
    }

    public async Task InitializeSettingsAsync(AppSettings settings)
    {
        await _settingsService.ValidatePathsAsync(settings);

        // Navigate both panes to their persisted or fallback paths
        LeftPane.Navigate(settings.LeftPanePath);
        RightPane.Navigate(settings.RightPanePath);
    }

    public void PersistSettings(double left, double top, double width, double height, string windowState)
    {
        var settings = new AppSettings
        {
            WindowLeft = left,
            WindowTop = top,
            WindowWidth = width,
            WindowHeight = height,
            WindowState = windowState,
            LeftPanePath = string.IsNullOrEmpty(LeftPane.CurrentPath) ? @"C:\" : LeftPane.CurrentPath,
            RightPanePath = string.IsNullOrEmpty(RightPane.CurrentPath) ? @"C:\" : RightPane.CurrentPath
        };

        _settingsService.SaveSettings(settings);
    }

    public async Task PopulateSidebarAsync()
    {
        // 1. Favorites - evaluate paths off UI thread
        var favorites = await Task.Run(() =>
        {
            var list = new List<SidebarItemViewModel>();
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var docsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var downloadsPath = Path.Combine(userProfile, "Downloads");

            if (!string.IsNullOrEmpty(desktopPath) && Directory.Exists(desktopPath))
                list.Add(new SidebarItemViewModel("Desktop", desktopPath, SidebarItemCategory.Favorite));

            if (!string.IsNullOrEmpty(docsPath) && Directory.Exists(docsPath))
                list.Add(new SidebarItemViewModel("Documents", docsPath, SidebarItemCategory.Favorite));

            if (!string.IsNullOrEmpty(downloadsPath) && Directory.Exists(downloadsPath))
                list.Add(new SidebarItemViewModel("Downloads", downloadsPath, SidebarItemCategory.Favorite));

            return list;
        });

        SidebarFavorites.Clear();
        foreach (var item in favorites)
        {
            SidebarFavorites.Add(item);
        }

        // 2. Drives - enumerate drives off UI thread
        var drives = await Task.Run(() =>
        {
            var list = new List<SidebarItemViewModel>();
            try
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (!drive.IsReady) continue;

                    string label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                        ? drive.Name.TrimEnd('\\')
                        : $"{drive.VolumeLabel} ({drive.Name.TrimEnd('\\')})";

                    list.Add(new SidebarItemViewModel(label, drive.RootDirectory.FullName, SidebarItemCategory.Drive));
                }
            }
            catch
            {
                // Fall back gracefully if drive interrogation throws security exception
            }
            return list;
        });

        SidebarDrives.Clear();
        foreach (var drive in drives)
        {
            SidebarDrives.Add(drive);
        }

        // 3. Asynchronously load shell icons for sidebar items
        _ = LoadSidebarIconsAsync();
    }

    private async Task LoadSidebarIconsAsync()
    {
        foreach (var item in SidebarFavorites)
        {
            try
            {
                var icon = await _iconService.GetIconAsync(item.Path, isDirectory: true, isDrive: false);
                if (icon != null)
                {
                    item.Icon = icon;
                }
            }
            catch
            {
                // Non-critical if individual sidebar item icon cannot be retrieved
            }
        }

        foreach (var item in SidebarDrives)
        {
            try
            {
                var icon = await _iconService.GetIconAsync(item.Path, isDirectory: true, isDrive: true);
                if (icon != null)
                {
                    item.Icon = icon;
                }
            }
            catch
            {
                // Non-critical if individual sidebar item icon cannot be retrieved
            }
        }
    }
}
