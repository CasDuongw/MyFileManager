using System.IO;
using FM.App.Services;

namespace FM.App.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _tempFile;

    public SettingsServiceTests()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"FM_Settings_{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_tempFile))
            {
                File.Delete(_tempFile);
            }
        }
        catch
        {
            // Ignore cleanup failure
        }
    }

    [Fact]
    public void MissingFile_ReturnsDefaultSettings()
    {
        var service = new SettingsService(_tempFile);

        var settings = service.LoadSettings();

        Assert.NotNull(settings);
        Assert.True(settings.WindowWidth >= 600);
        Assert.True(settings.WindowHeight >= 400);
        Assert.Equal("Normal", settings.WindowState);
        Assert.False(string.IsNullOrEmpty(settings.LeftPanePath));
        Assert.False(string.IsNullOrEmpty(settings.RightPanePath));
    }

    [Fact]
    public void CorruptedJson_ReturnsDefaultSettingsWithoutCrashing()
    {
        File.WriteAllText(_tempFile, "{ broken json [{{ 12345: true");
        var service = new SettingsService(_tempFile);

        var settings = service.LoadSettings();

        Assert.NotNull(settings);
        Assert.Equal(1100, settings.WindowWidth);
        Assert.Equal(700, settings.WindowHeight);
    }

    [Fact]
    public void RoundTrip_SavesAndLoadsIdenticalValues()
    {
        var service = new SettingsService(_tempFile);
        string tempDir = Path.GetTempPath();

        var original = new AppSettings
        {
            WindowWidth = 1440,
            WindowHeight = 900,
            WindowLeft = 100,
            WindowTop = 50,
            WindowState = "Maximized",
            LeftPanePath = tempDir,
            RightPanePath = tempDir
        };

        service.SaveSettings(original);
        var loaded = service.LoadSettings();

        Assert.Equal(1440, loaded.WindowWidth);
        Assert.Equal(900, loaded.WindowHeight);
        Assert.Equal(100, loaded.WindowLeft);
        Assert.Equal(50, loaded.WindowTop);
        Assert.Equal("Maximized", loaded.WindowState);
        Assert.Equal(tempDir, loaded.LeftPanePath);
        Assert.Equal(tempDir, loaded.RightPanePath);
    }

    [Fact]
    public void InvalidValues_ClampedToSensibleDefaults()
    {
        var service = new SettingsService(_tempFile);

        var invalid = new AppSettings
        {
            WindowWidth = -500, // Invalid width
            WindowHeight = 50,  // Invalid height
            WindowLeft = double.NaN,
            WindowTop = double.PositiveInfinity,
            WindowState = "InvalidWindowState",
            LeftPanePath = @"C:\Nonexistent_Folder_12345",
            RightPanePath = string.Empty
        };

        service.SaveSettings(invalid);
        var loaded = service.LoadSettings();

        // Enforced clamping
        Assert.Equal(1100, loaded.WindowWidth);
        Assert.Equal(700, loaded.WindowHeight);
        Assert.Null(loaded.WindowLeft);
        Assert.Null(loaded.WindowTop);
        Assert.Equal("Normal", loaded.WindowState);
        Assert.True(Directory.Exists(loaded.LeftPanePath));
        Assert.True(Directory.Exists(loaded.RightPanePath));
    }
}
