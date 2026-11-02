using FM.Core.Drives;
using Xunit;

namespace FM.Core.Tests;

public class DriveServiceTests
{
    [Fact]
    public async Task GetDrivesAsync_ReturnsConfiguredDrives()
    {
        var fake = new FakeDriveService();
        var drives = await fake.GetDrivesAsync();

        Assert.NotEmpty(drives);
        Assert.Contains(drives, d => d.DriveLetter == "C:");
    }

    [Fact]
    public async Task EjectDriveAsync_RemovesDriveAndFiresEvent()
    {
        var fake = new FakeDriveService();
        var usbDrive = new DriveItem(@"E:\", "USB Drive", System.IO.DriveType.Removable, 32_000_000_000L, 16_000_000_000L, "FAT32", true);
        fake.AddDrive(usbDrive);

        bool eventFired = false;
        fake.DrivesChanged += (s, e) => eventFired = true;

        bool success = await fake.EjectDriveAsync(@"E:\");

        Assert.True(success);
        Assert.True(eventFired);
        Assert.Contains(@"E:\", fake.EjectedDrives);

        var remaining = await fake.GetDrivesAsync();
        Assert.DoesNotContain(remaining, d => d.Path == @"E:\");
    }

    [Fact]
    public void DriveItem_CalculatesUsedSpaceAndFormattingProperly()
    {
        var drive = new DriveItem(@"C:\", "Windows", System.IO.DriveType.Fixed, 100_000_000_000L, 40_000_000_000L, "NTFS", true);

        Assert.Equal(60_000_000_000L, drive.UsedBytes);
        Assert.Equal(60.0, drive.PercentUsed, precision: 1);
        Assert.False(drive.IsRemovable);
        Assert.Contains("trống trên", drive.FormattedFreeSpaceText);
    }
}

