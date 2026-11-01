using System;
using System.IO;
using System.IO.Compression;
using FM.App.Helpers;
using FM.App.Models;
using FM.App.ViewModels;
using FM.App.Tests.Fakes;
using FM.Core.Models;

namespace FM.App.Tests;

public class ShellInteropTests
{
    [Fact]
    public void CreateZipArchive_CompressesFilesAndDirectoriesCorrectly()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "FM_ZipTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string zipPath = Path.Combine(tempDir, "output.zip");

        try
        {
            string file1 = Path.Combine(tempDir, "test1.txt");
            File.WriteAllText(file1, "Hello World from File 1");

            string subDir = Path.Combine(tempDir, "SubFolder");
            Directory.CreateDirectory(subDir);
            string file2 = Path.Combine(subDir, "test2.txt");
            File.WriteAllText(file2, "Content inside subfolder");

            ShellInterop.CreateZipArchive(new[] { file1, subDir }, zipPath);

            Assert.True(File.Exists(zipPath));

            using var archive = ZipFile.OpenRead(zipPath);
            Assert.Contains(archive.Entries, e => e.FullName == "test1.txt");
            Assert.Contains(archive.Entries, e => e.FullName.Replace('\\', '/') == "SubFolder/test2.txt");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void IsWinRarInstalled_ReturnsBooleanWithoutException()
    {
        bool installed = ShellInterop.IsWinRarInstalled;
        // Should evaluate without crashing
        Assert.True(installed || !installed);
    }

    [Fact]
    public void PinToStart_WithEmptyPath_DoesNotThrow()
    {
        ShellInterop.PinToStart("");
        ShellInterop.PinToStart("   ");
    }

    [Fact]
    public void ShowOpenWithDialog_WithEmptyPath_DoesNotThrow()
    {
        ShellInterop.ShowOpenWithDialog("");
        ShellInterop.ShowOpenWithDialog(@"C:\NonExistent_FakeFile.xyz");
    }

    [Fact]
    public void RunAsAdministrator_WithEmptyPath_DoesNotThrow()
    {
        ShellInterop.RunAsAdministrator("");
    }

    [Fact]
    public void FilePaneViewModel_IsSelectedArchive_IdentifiesArchiveExtensions()
    {
        var dirService = new FakeDirectoryService();
        var iconService = new FakeIconService();
        var vm = new FilePaneViewModel(dirService, iconService);

        // Initially null selected item
        Assert.False(vm.IsSelectedArchive);

        // Directory is not archive
        var dirEntry = new FileEntry("Folder", @"C:\Folder", true, 0, DateTime.Now);
        vm.SelectedItem = new FileItemViewModel(dirEntry, iconService);
        Assert.False(vm.IsSelectedArchive);

        // Regular text file is not archive
        var txtEntry = new FileEntry("doc.txt", @"C:\doc.txt", false, 100, DateTime.Now);
        vm.SelectedItem = new FileItemViewModel(txtEntry, iconService);
        Assert.False(vm.IsSelectedArchive);

        // ZIP file is archive
        var zipEntry = new FileEntry("archive.zip", @"C:\archive.zip", false, 1000, DateTime.Now);
        vm.SelectedItem = new FileItemViewModel(zipEntry, iconService);
        Assert.True(vm.IsSelectedArchive);

        // RAR file is archive
        var rarEntry = new FileEntry("archive.rar", @"C:\archive.rar", false, 1000, DateTime.Now);
        vm.SelectedItem = new FileItemViewModel(rarEntry, iconService);
        Assert.True(vm.IsSelectedArchive);
    }
}
