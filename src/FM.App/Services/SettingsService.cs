using System.IO;
using System.Text.Json;

namespace FM.App.Services;

/// <summary>
/// Saves and loads application configuration to %AppData%/MyFileManager/settings.json.
/// Handles I/O errors gracefully and provides default fallback paths.
/// </summary>
public class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _settingsFilePath;

    public SettingsService()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string folder = Path.Combine(appData, "MyFileManager");
        _settingsFilePath = Path.Combine(folder, "settings.json");
    }

    public AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                string json = File.ReadAllText(_settingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (settings != null)
                {
                    ValidateSettings(settings);
                    return settings;
                }
            }
        }
        catch
        {
            // Fall back to defaults on corrupted file or permission issues
        }

        var defaultSettings = new AppSettings();
        ValidateSettings(defaultSettings);
        return defaultSettings;
    }

    public void SaveSettings(AppSettings settings)
    {
        try
        {
            string? dir = Path.GetDirectoryName(_settingsFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(_settingsFilePath, json);
        }
        catch
        {
            // Best effort; don't crash the application if saving settings fails
        }
    }

    private static void ValidateSettings(AppSettings settings)
    {
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string defaultPath = Directory.Exists(userProfile) ? userProfile : @"C:\";

        if (string.IsNullOrWhiteSpace(settings.LeftPanePath) || !Directory.Exists(settings.LeftPanePath))
        {
            settings.LeftPanePath = defaultPath;
        }

        if (string.IsNullOrWhiteSpace(settings.RightPanePath) || !Directory.Exists(settings.RightPanePath))
        {
            settings.RightPanePath = defaultPath;
        }

        if (settings.WindowWidth < 600) settings.WindowWidth = 1100;
        if (settings.WindowHeight < 400) settings.WindowHeight = 700;
    }
}
