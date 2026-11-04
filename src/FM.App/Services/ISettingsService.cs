namespace FM.App.Services;

/// <summary>
/// Model for persistent application settings.
/// </summary>
public class AppSettings
{
    public double WindowWidth { get; set; } = 1100;
    public double WindowHeight { get; set; } = 700;
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public string WindowState { get; set; } = "Normal";
    public string LeftPanePath { get; set; } = string.Empty;
    public string RightPanePath { get; set; } = string.Empty;
    public List<string> PanePaths { get; set; } = new();
    public string LayoutMode { get; set; } = "DualVertical";
    public bool ShowHiddenFiles { get; set; } = false;
    public List<string> RecentFolders { get; set; } = new();
    public List<string> IndexedRoots { get; set; } = new();
    public List<string> IndexExcludes { get; set; } = new();
    public bool ConfirmNormalDelete { get; set; } = false;
    public List<PaneTabSettings> PaneTabs { get; set; } = new();
}

public class TabStateSetting
{
    public string Path { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ViewMode { get; set; } = "Details";
    public string SortColumn { get; set; } = "Name";
    public bool SortAscending { get; set; } = true;
    public string SpecialMode { get; set; } = "None";
}

public class PaneTabSettings
{
    public List<TabStateSetting> Tabs { get; set; } = new();
    public int ActiveTabIndex { get; set; } = 0;
}

public interface ISettingsService
{
    AppSettings LoadSettings();
    void SaveSettings(AppSettings settings);
    Task ValidatePathsAsync(AppSettings settings, TimeSpan? timeout = null);
}
