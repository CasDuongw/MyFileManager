using FM.Core.CommandLine;
using FM.Core.FileSystem;
using FM.Core.PathResolution;

namespace FM.Core.Tests;

public class CommandLineParserTests
{
    private readonly InMemoryFileSystem _fs;
    private readonly PathResolver _pathResolver;
    private readonly CommandLineParser _parser;

    public CommandLineParserTests()
    {
        _fs = new InMemoryFileSystem();
        _pathResolver = new PathResolver();
        _parser = new CommandLineParser();

        _fs.CreateDirectory(@"C:\Projects");
        _fs.CreateDirectory(@"C:\Projects\Web");
        _fs.CreateVirtualFile(@"C:\Projects\Web\index.html", 512);
        _fs.CreateVirtualFile(@"C:\Projects\readme.txt", 128);
    }

    [Fact]
    public void Parse_NullOrEmptyArgs_ReturnsEmptyResult()
    {
        var res1 = _parser.Parse(null, _fs, _pathResolver);
        Assert.False(res1.HasTargets);
        Assert.Empty(res1.Targets);

        var res2 = _parser.Parse(Array.Empty<string>(), _fs, _pathResolver);
        Assert.False(res2.HasTargets);
        Assert.Empty(res2.Targets);
    }

    [Fact]
    public void Parse_ExistingDirectory_ReturnsDirectoryTarget()
    {
        var res = _parser.Parse(new[] { @"C:\Projects" }, _fs, _pathResolver);

        Assert.True(res.HasTargets);
        var target = Assert.Single(res.Targets);
        Assert.True(target.IsValid);
        Assert.Equal(@"C:\Projects", target.TargetDirectory);
        Assert.Null(target.SelectFileName);
    }

    [Fact]
    public void Parse_ExistingFile_ReturnsParentDirectoryAndSelectFileName()
    {
        var res = _parser.Parse(new[] { @"C:\Projects\readme.txt" }, _fs, _pathResolver);

        Assert.True(res.HasTargets);
        var target = Assert.Single(res.Targets);
        Assert.True(target.IsValid);
        Assert.Equal(@"C:\Projects", target.TargetDirectory);
        Assert.Equal("readme.txt", target.SelectFileName);
    }

    [Theory]
    [InlineData(@"/select,C:\Projects\Web\index.html")]
    [InlineData(@"/select,""C:\Projects\Web\index.html""")]
    [InlineData(@"-select,C:\Projects\Web\index.html")]
    public void Parse_ExplorerSelectFileSyntax_OpensParentAndSelectsFile(string arg)
    {
        var res = _parser.Parse(new[] { arg }, _fs, _pathResolver);

        Assert.True(res.HasTargets);
        var target = Assert.Single(res.Targets);
        Assert.True(target.IsValid);
        Assert.Equal(@"C:\Projects\Web", target.TargetDirectory);
        Assert.Equal("index.html", target.SelectFileName);
    }

    [Fact]
    public void Parse_ExplorerSelectTwoTokens_OpensParentAndSelectsFile()
    {
        var res = _parser.Parse(new[] { "/select", @"C:\Projects\readme.txt" }, _fs, _pathResolver);

        Assert.True(res.HasTargets);
        var target = Assert.Single(res.Targets);
        Assert.True(target.IsValid);
        Assert.Equal(@"C:\Projects", target.TargetDirectory);
        Assert.Equal("readme.txt", target.SelectFileName);
    }

    [Fact]
    public void Parse_ExplorerSelectDirectory_OpensParentAndSelectsDirectory()
    {
        var res = _parser.Parse(new[] { @"/select,C:\Projects\Web" }, _fs, _pathResolver);

        Assert.True(res.HasTargets);
        var target = Assert.Single(res.Targets);
        Assert.True(target.IsValid);
        Assert.Equal(@"C:\Projects", target.TargetDirectory);
        Assert.Equal("Web", target.SelectFileName);
    }

    [Fact]
    public void Parse_MultipleArguments_ReturnsMultipleTargets()
    {
        var res = _parser.Parse(new[] { @"C:\Projects", @"C:\Projects\Web\index.html" }, _fs, _pathResolver);

        Assert.Equal(2, res.Targets.Count);
        Assert.Equal(@"C:\Projects", res.Targets[0].TargetDirectory);
        Assert.Null(res.Targets[0].SelectFileName);

        Assert.Equal(@"C:\Projects\Web", res.Targets[1].TargetDirectory);
        Assert.Equal("index.html", res.Targets[1].SelectFileName);
    }

    [Fact]
    public void Parse_VirtualLocations_ReturnsVirtualTargets()
    {
        var res = _parser.Parse(new[] { "thispc://", "recyclebin://" }, _fs, _pathResolver);

        Assert.Equal(2, res.Targets.Count);
        Assert.Equal("thispc://", res.Targets[0].TargetDirectory);
        Assert.True(res.Targets[0].IsValid);

        Assert.Equal("recyclebin://", res.Targets[1].TargetDirectory);
        Assert.True(res.Targets[1].IsValid);
    }

    [Fact]
    public void Parse_NonExistentPath_ReturnsInvalidTargetWithoutThrowing()
    {
        var res = _parser.Parse(new[] { @"C:\NonExistent\Folder" }, _fs, _pathResolver);

        Assert.True(res.HasTargets);
        var target = Assert.Single(res.Targets);
        Assert.False(target.IsValid);
        Assert.Contains("does not exist", target.ErrorMessage);
    }

    [Fact]
    public void Parse_UnsupportedVirtualFolder_ReturnsInvalidTargetWithoutThrowing()
    {
        var res = _parser.Parse(new[] { "shell:ControlPanelFolder" }, _fs, _pathResolver);

        Assert.True(res.HasTargets);
        var target = Assert.Single(res.Targets);
        Assert.False(target.IsValid);
        Assert.NotNull(target.ErrorMessage);
        Assert.Contains("ControlPanelFolder", target.ErrorMessage);
    }
}
