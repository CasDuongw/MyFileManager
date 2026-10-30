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
    public bool ShowHiddenFiles { get; set; } = false;
}

public interface ISettingsService
{
    AppSettings LoadSettings();
    void SaveSettings(AppSettings settings);
    Task ValidatePathsAsync(AppSettings settings, TimeSpan? timeout = null);
}
