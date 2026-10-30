namespace FM.App.Services;

/// <summary>
/// Model for persistent application settings.
/// </summary>
public class AppSettings
{
    public double WindowWidth { get; set; } = 1100;
    public double WindowHeight { get; set; } = 700;
    public string WindowState { get; set; } = "Normal";
    public string LeftPanePath { get; set; } = string.Empty;
    public string RightPanePath { get; set; } = string.Empty;
}

public interface ISettingsService
{
    AppSettings LoadSettings();
    void SaveSettings(AppSettings settings);
}
