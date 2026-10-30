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

        PopulateSidebar();
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

    public void InitializeSettings()
    {
        var settings = _settingsService.LoadSettings();

        // Navigate both panes to their persisted or fallback paths
        LeftPane.Navigate(settings.LeftPanePath);
        RightPane.Navigate(settings.RightPanePath);
    }

    public void PersistSettings(double width, double height, string windowState)
    {
        var settings = new AppSettings
        {
            WindowWidth = width,
            WindowHeight = height,
            WindowState = windowState,
            LeftPanePath = string.IsNullOrEmpty(LeftPane.CurrentPath) ? @"C:\" : LeftPane.CurrentPath,
            RightPanePath = string.IsNullOrEmpty(RightPane.CurrentPath) ? @"C:\" : RightPane.CurrentPath
        };

        _settingsService.SaveSettings(settings);
    }

    private void PopulateSidebar()
    {
        // 1. Favorites
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var docsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var downloadsPath = Path.Combine(userProfile, "Downloads");

        if (Directory.Exists(desktopPath))
            SidebarFavorites.Add(new SidebarItemViewModel("Desktop", desktopPath, SidebarItemCategory.Favorite));

        if (Directory.Exists(docsPath))
            SidebarFavorites.Add(new SidebarItemViewModel("Documents", docsPath, SidebarItemCategory.Favorite));

        if (Directory.Exists(downloadsPath))
            SidebarFavorites.Add(new SidebarItemViewModel("Downloads", downloadsPath, SidebarItemCategory.Favorite));

        // 2. Drives
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady) continue;

                string label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                    ? drive.Name.TrimEnd('\\')
                    : $"{drive.VolumeLabel} ({drive.Name.TrimEnd('\\')})";

                SidebarDrives.Add(new SidebarItemViewModel(label, drive.RootDirectory.FullName, SidebarItemCategory.Drive));
            }
        }
        catch
        {
            // Fall back gracefully if drive interrogation throws security exception
        }

        // 3. Asynchronously load shell icons for sidebar items
        _ = LoadSidebarIconsAsync();
    }

    private async Task LoadSidebarIconsAsync()
    {
        foreach (var item in SidebarFavorites.Concat(SidebarDrives))
        {
            try
            {
                var icon = await _iconService.GetIconAsync(item.Path, isDirectory: true);
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
