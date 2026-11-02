using FM.Core.PathResolution;
using Xunit;

namespace FM.Core.Tests;

public class PathResolverTests
{
    private readonly PathResolver _resolver = PathResolver.Instance;

    [Fact]
    public void Resolve_NullOrWhitespace_ReturnsInvalidFailure()
    {
        var res1 = _resolver.Resolve("");
        var res2 = _resolver.Resolve("   ");

        Assert.False(res1.IsSuccess);
        Assert.Equal(ResolvedPathType.Invalid, res1.PathType);

        Assert.False(res2.IsSuccess);
        Assert.Equal(ResolvedPathType.Invalid, res2.PathType);
    }

    [Theory]
    [InlineData("This PC")]
    [InlineData("this pc")]
    [InlineData("thispc://")]
    [InlineData("::ThisPC")]
    [InlineData("shell:ThisPC")]
    [InlineData("shell:MyComputerFolder")]
    [InlineData("shell:DriveFolder")]
    public void Resolve_ThisPcAliases_ReturnsThisPcType(string input)
    {
        var res = _resolver.Resolve(input);
        Assert.True(res.IsSuccess);
        Assert.Equal(ResolvedPathType.ThisPC, res.PathType);
        Assert.Equal("thispc://", res.ResolvedPath);
        Assert.Equal("This PC", res.DisplayName);
    }

    [Theory]
    [InlineData("Recycle Bin")]
    [InlineData("recycle bin")]
    [InlineData("recyclebin://")]
    [InlineData("::RecycleBin")]
    [InlineData("shell:RecycleBinFolder")]
    [InlineData("shell:RecycleBin")]
    [InlineData("Thùng rác")]
    public void Resolve_RecycleBinAliases_ReturnsRecycleBinType(string input)
    {
        var res = _resolver.Resolve(input);
        Assert.True(res.IsSuccess);
        Assert.Equal(ResolvedPathType.RecycleBin, res.PathType);
        Assert.Equal("recyclebin://", res.ResolvedPath);
        Assert.Equal("Recycle Bin", res.DisplayName);
    }

    [Fact]
    public void Resolve_EnvironmentVariable_ExpandsCorrectly()
    {
        string expectedUser = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var res = _resolver.Resolve("%USERPROFILE%");

        Assert.True(res.IsSuccess);
        Assert.Equal(ResolvedPathType.FileSystemDirectory, res.PathType);
        Assert.Equal(expectedUser, res.ResolvedPath);
    }

    [Fact]
    public void Resolve_Tilde_ExpandsToUserProfile()
    {
        string expectedUser = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var resTilde = _resolver.Resolve("~");
        var resSub = _resolver.Resolve("~/Documents");

        Assert.True(resTilde.IsSuccess);
        Assert.Equal(expectedUser, resTilde.ResolvedPath);

        Assert.True(resSub.IsSuccess);
        Assert.Equal(System.IO.Path.Combine(expectedUser, "Documents"), resSub.ResolvedPath);
    }

    [Theory]
    [InlineData("shell:Desktop")]
    [InlineData("shell:Downloads")]
    [InlineData("shell:Documents")]
    [InlineData("shell:Pictures")]
    [InlineData("shell:AppData")]
    public void Resolve_CommonShellFolders_ReturnsValidFileSystemPath(string shellUri)
    {
        var res = _resolver.Resolve(shellUri);
        Assert.True(res.IsSuccess);
        Assert.Equal(ResolvedPathType.FileSystemDirectory, res.PathType);
        Assert.False(string.IsNullOrEmpty(res.ResolvedPath));
        Assert.False(res.ResolvedPath.StartsWith("shell:"));
    }

    [Fact]
    public void Resolve_UnknownShellFolder_ReturnsUnsupportedVirtualFolder_WithoutCrashing()
    {
        var res = _resolver.Resolve("shell:CompletelyNonExistentVirtualFolder123");
        Assert.False(res.IsSuccess);
        Assert.Equal(ResolvedPathType.UnsupportedVirtualFolder, res.PathType);
        Assert.Contains("không được hỗ trợ", res.ErrorMessage);
    }

    [Fact]
    public void Resolve_LongPathPrefix_PreservesPrefix()
    {
        string longPath = @"\\?\C:\Some\Long\Path\Structure";
        var res = _resolver.Resolve(longPath);

        Assert.True(res.IsSuccess);
        Assert.Equal(longPath, res.ResolvedPath);
    }

    [Theory]
    [InlineData(@"\\server\share", "share (server)")]
    [InlineData(@"\\wsl$\Ubuntu", "Ubuntu (WSL)")]
    [InlineData(@"\\wsl.localhost\Debian", "Debian (WSL)")]
    public void Resolve_UncAndWslPaths_PreservesPathAndGeneratesCleanDisplayName(string uncPath, string expectedDisplayName)
    {
        var res = _resolver.Resolve(uncPath);
        Assert.True(res.IsSuccess);
        Assert.Equal(ResolvedPathType.FileSystemDirectory, res.PathType);
        Assert.Equal(uncPath, res.ResolvedPath);
        Assert.Equal(expectedDisplayName, res.DisplayName);
    }
}

