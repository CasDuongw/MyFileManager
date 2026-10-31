using System;
using System.IO;
using FM.App.Models;
using FM.App.Services;
using Xunit;

namespace FM.App.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _tempFile;

    public SettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FM_Test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _tempFile = Path.Combine(_tempDir, "settings.json");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // Ignore cleanup failure
        }
    }

    [Fact]
    public void LoadSettings_WhenFileDoesNotExist_ReturnsDefaultSettings()
    {
        var service = new SettingsService(_tempFile);
        var settings = service.LoadSettings();

        Assert.NotNull(settings);
        Assert.Equal(1100, settings.WindowWidth);
        Assert.Equal(700, settings.WindowHeight);
        Assert.Equal("Normal", settings.WindowState);
        Assert.Equal("DualVertical", settings.LayoutMode);
    }

    [Fact]
    public void SaveSettings_And_LoadSettings_RoundtripsSuccessfully()
    {
        var service = new SettingsService(_tempFile);
        var original = new AppSettings
        {
            WindowWidth = 1400,
            WindowHeight = 900,
            WindowState = "Maximized",
            LayoutMode = "TripleLeft",
            LeftPanePath = @"C:\Windows",
            RightPanePath = @"C:\Program Files"
        };

        service.SaveSettings(original);
        var loaded = service.LoadSettings();

        Assert.Equal(original.WindowWidth, loaded.WindowWidth);
        Assert.Equal(original.WindowHeight, loaded.WindowHeight);
        Assert.Equal(original.WindowState, loaded.WindowState);
        Assert.Equal(original.LayoutMode, loaded.LayoutMode);
        Assert.Equal(original.LeftPanePath, loaded.LeftPanePath);
        Assert.Equal(original.RightPanePath, loaded.RightPanePath);
    }

    [Fact]
    public void ValidateSettings_FixesInvalidDimensions()
    {
        var service = new SettingsService(_tempFile);
        var invalid = new AppSettings
        {
            WindowWidth = 100,
            WindowHeight = 50,
            WindowState = "InvalidState",
            LeftPanePath = "",
            RightPanePath = "   "
        };

        service.SaveSettings(invalid);
        var loaded = service.LoadSettings();

        Assert.Equal(1100, loaded.WindowWidth);
        Assert.Equal(700, loaded.WindowHeight);
        Assert.Equal("Normal", loaded.WindowState);
        Assert.False(string.IsNullOrWhiteSpace(loaded.LeftPanePath));
        Assert.False(string.IsNullOrWhiteSpace(loaded.RightPanePath));
    }

    [Fact]
    public async Task ValidatePathsAsync_ReplacesNonExistentPathsWithFallback()
    {
        var service = new SettingsService(_tempFile);
        var settings = new AppSettings
        {
            LeftPanePath = @"Z:\NonExistentPath_12345",
            RightPanePath = @"Y:\AnotherFakePath_67890"
        };

        await service.ValidatePathsAsync(settings);

        Assert.True(Directory.Exists(settings.LeftPanePath));
        Assert.True(Directory.Exists(settings.RightPanePath));
    }

    [Fact]
    public void LoadSettings_InvalidJson_ReturnsDefaultSettings()
    {
        File.WriteAllText(_tempFile, "{ broken json content ... ");

        var service = new SettingsService(_tempFile);
        var loaded = service.LoadSettings();

        Assert.NotNull(loaded);
        Assert.Equal(1100, loaded.WindowWidth);
        Assert.Equal(700, loaded.WindowHeight);
        Assert.True(Directory.Exists(loaded.LeftPanePath));
        Assert.True(Directory.Exists(loaded.RightPanePath));
    }

    [Fact]
    public void SaveSettings_AtomicWrite_LeavesNoTempFiles()
    {
        var service = new SettingsService(_tempFile);
        service.SaveSettings(new AppSettings { WindowWidth = 1200 });

        Assert.True(File.Exists(_tempFile));
        var tempFiles = Directory.GetFiles(_tempDir, "settings_*.tmp");
        Assert.Empty(tempFiles);
    }
}
