using System.IO;
using System.Text.Json;

namespace FM.App.Services;

/// <summary>
/// Saves and loads application configuration to %AppData%/MyFileManager/settings.json or a custom path.
/// Handles I/O errors gracefully, handles corrupted JSON, and enforces value validation.
/// </summary>
public class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _settingsFilePath;

    public SettingsService(string? customFilePath = null)
    {
        if (!string.IsNullOrWhiteSpace(customFilePath))
        {
            _settingsFilePath = customFilePath;
        }
        else
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string folder = Path.Combine(appData, "MyFileManager");
            _settingsFilePath = Path.Combine(folder, "settings.json");
        }
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
            // Fall back to defaults on corrupted file, invalid JSON, or permission issues
        }

        var defaultSettings = new AppSettings();
        ValidateSettings(defaultSettings);
        return defaultSettings;
    }

    public void SaveSettings(AppSettings settings)
    {
        try
        {
            ValidateSettings(settings);
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

        if (settings.WindowWidth < 600 || double.IsNaN(settings.WindowWidth) || double.IsInfinity(settings.WindowWidth))
        {
            settings.WindowWidth = 1100;
        }

        if (settings.WindowHeight < 400 || double.IsNaN(settings.WindowHeight) || double.IsInfinity(settings.WindowHeight))
        {
            settings.WindowHeight = 700;
        }

        if (settings.WindowLeft.HasValue && (double.IsNaN(settings.WindowLeft.Value) || double.IsInfinity(settings.WindowLeft.Value)))
        {
            settings.WindowLeft = null;
        }

        if (settings.WindowTop.HasValue && (double.IsNaN(settings.WindowTop.Value) || double.IsInfinity(settings.WindowTop.Value)))
        {
            settings.WindowTop = null;
        }

        if (string.IsNullOrWhiteSpace(settings.WindowState) ||
            (!string.Equals(settings.WindowState, "Normal", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(settings.WindowState, "Maximized", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(settings.WindowState, "Minimized", StringComparison.OrdinalIgnoreCase)))
        {
            settings.WindowState = "Normal";
        }
    }
}
