using FM.Core.FileSystem;
using FM.Tests.Common;

namespace FM.Core.Tests;

public class FileSystemTests
{
    [Fact]
    public void InMemoryFileSystem_VirtualFile_CostsZeroDiskAndRAM()
    {
        var fs = new InMemoryFileSystem();
        long fiveGigabytes = 5L * 1024 * 1024 * 1024;
        string path = @"C:\huge_file.iso";

        fs.CreateVirtualFile(path, fiveGigabytes);

        var info = fs.GetInfo(path);
        Assert.NotNull(info);
        Assert.True(info.Exists);
        Assert.False(info.IsDirectory);
        Assert.Equal(fiveGigabytes, info.Size);

        using var stream = fs.OpenRead(path);
        Assert.Equal(fiveGigabytes, stream.Length);

        // Read at offset 0
        byte[] buffer = new byte[4];
        stream.ReadExactly(buffer, 0, buffer.Length);
        Assert.Equal(InMemoryFileSystem.GetDeterministicByte(0), buffer[0]);
        Assert.Equal(InMemoryFileSystem.GetDeterministicByte(1), buffer[1]);

        // Seek to 4 GB and read
        long fourGbOffset = 4L * 1024 * 1024 * 1024;
        stream.Seek(fourGbOffset, SeekOrigin.Begin);
        Assert.Equal(fourGbOffset, stream.Position);

        stream.ReadExactly(buffer, 0, buffer.Length);
        Assert.Equal(InMemoryFileSystem.GetDeterministicByte(fourGbOffset), buffer[0]);
    }

    [Fact]
    public void InMemoryFileSystem_SimulatedVolumes_AndFreeSpace()
    {
        var fs = new InMemoryFileSystem();
        string cFile = @"C:\dir\file.txt";
        string dFile = @"D:\dir\file.txt";

        string volC = fs.GetVolumeId(cFile);
        string volD = fs.GetVolumeId(dFile);

        Assert.NotEqual(volC, volD);
        Assert.Equal(volC, fs.GetVolumeId(@"C:\another\path.log"));

        fs.SetVolumeFreeSpace("C:", 500 * 1024 * 1024);
        Assert.Equal(500 * 1024 * 1024, fs.GetFreeSpace(@"C:\test"));
    }

    [Fact]
    public void InMemoryFileSystem_FailureInjection_ThrowsAtOffset()
    {
        var fs = new InMemoryFileSystem();
        string path = @"C:\fail.bin";
        fs.CreateVirtualFile(path, 1024);

        var expectedEx = new IOException("Simulated network drop");
        fs.InjectFailure(path, byteOffset: 100, expectedEx, count: 1);

        using var stream = fs.OpenRead(path);
        byte[] buf = new byte[64];
        stream.ReadExactly(buf, 0, 64); // 0-64: ok

        // Reading into offset 100 throws
        var ex = Assert.Throws<IOException>(() => stream.ReadExactly(buf, 0, 64));
        Assert.Same(expectedEx, ex);

        // Second attempt at offset 100 succeeds because count was 1
        stream.ReadExactly(buf, 0, 64);
    }

    [Fact]
    public void PhysicalFileSystem_BasicOperations_WorkProperly()
    {
        using var ws = new TestWorkspace("PhysFsBasic");
        var fs = new PhysicalFileSystem();
        string filePath = Path.Combine(ws.RootPath, "sample.txt");

        using (var writer = fs.CreateFileForWrite(filePath))
        using (var sw = new StreamWriter(writer))
        {
            sw.Write("Antigravity Copy Engine");
        }

        var info = fs.GetInfo(filePath);
        Assert.NotNull(info);
        Assert.True(info.Exists);
        Assert.False(info.IsDirectory);
        Assert.True(info.Size > 0);

        string volId = fs.GetVolumeId(filePath);
        Assert.False(string.IsNullOrEmpty(volId));

        long free = fs.GetFreeSpace(filePath);
        Assert.True(free > 0);

        using (var reader = fs.OpenRead(filePath))
        using (var sr = new StreamReader(reader))
        {
            string content = sr.ReadToEnd();
            Assert.Equal("Antigravity Copy Engine", content);
        }

        fs.Delete(filePath);
        Assert.False(fs.FileExists(filePath));
    }
}
