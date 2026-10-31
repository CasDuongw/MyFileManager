using System.IO;
using System.IO.Compression;
using FM.App.Helpers;

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
}
