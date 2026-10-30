using System.Windows;
using FM.App.Helpers;
using FM.Core.FileSystem;
using FM.Core.Operations.Transfer;

namespace FM.App.Tests;

public class DropEffectResolverTests
{
    private readonly InMemoryFileSystem _fs;

    public DropEffectResolverTests()
    {
        _fs = new InMemoryFileSystem();
        _fs.CreateDirectory(@"C:\volumeC\source");
        _fs.CreateDirectory(@"C:\volumeC\dest");
        _fs.CreateDirectory(@"D:\volumeD\dest");

        _fs.CreateVirtualFile(@"C:\volumeC\source\file1.txt", 100);
        _fs.CreateVirtualFile(@"C:\volumeC\source\file2.txt", 200);
        _fs.CreateDirectory(@"C:\volumeC\source\subfolder");
        _fs.CreateVirtualFile(@"C:\volumeC\source\subfolder\child.txt", 50);
    }

    [Fact]
    public void SameVolume_Default_ResolvesToMove()
    {
        var sources = new[] { @"C:\volumeC\source\file1.txt" };
        var res = DropEffectResolver.Resolve(_fs, sources, @"C:\volumeC\dest", DragDropKeyStates.None);

        Assert.True(res.IsValid);
        Assert.Equal(DragDropEffects.Move, res.Effect);
        Assert.Equal(TransferKind.Move, res.Kind);
    }

    [Fact]
    public void DifferentVolume_Default_ResolvesToCopy()
    {
        var sources = new[] { @"C:\volumeC\source\file1.txt" };
        var res = DropEffectResolver.Resolve(_fs, sources, @"D:\volumeD\dest", DragDropKeyStates.None);

        Assert.True(res.IsValid);
        Assert.Equal(DragDropEffects.Copy, res.Effect);
        Assert.Equal(TransferKind.Copy, res.Kind);
    }

    [Fact]
    public void CtrlKey_ForcesCopy_EvenOnSameVolume()
    {
        var sources = new[] { @"C:\volumeC\source\file1.txt" };
        var res = DropEffectResolver.Resolve(_fs, sources, @"C:\volumeC\dest", DragDropKeyStates.ControlKey);

        Assert.True(res.IsValid);
        Assert.Equal(DragDropEffects.Copy, res.Effect);
        Assert.Equal(TransferKind.Copy, res.Kind);
    }

    [Fact]
    public void ShiftKey_ForcesMove_EvenAcrossVolumes()
    {
        var sources = new[] { @"C:\volumeC\source\file1.txt" };
        var res = DropEffectResolver.Resolve(_fs, sources, @"D:\volumeD\dest", DragDropKeyStates.ShiftKey);

        Assert.True(res.IsValid);
        Assert.Equal(DragDropEffects.Move, res.Effect);
        Assert.Equal(TransferKind.Move, res.Kind);
    }

    [Fact]
    public void AltKey_OrCtrlShift_ReturnsNone_UnsupportedLink()
    {
        var sources = new[] { @"C:\volumeC\source\file1.txt" };

        var resAlt = DropEffectResolver.Resolve(_fs, sources, @"C:\volumeC\dest", DragDropKeyStates.AltKey);
        Assert.False(resAlt.IsValid);
        Assert.Equal(DragDropEffects.None, resAlt.Effect);
        Assert.Null(resAlt.Kind);

        var resCtrlShift = DropEffectResolver.Resolve(_fs, sources, @"C:\volumeC\dest", DragDropKeyStates.ControlKey | DragDropKeyStates.ShiftKey);
        Assert.False(resCtrlShift.IsValid);
        Assert.Equal(DragDropEffects.None, resCtrlShift.Effect);
        Assert.Null(resCtrlShift.Kind);
    }

    [Fact]
    public void DropIntoSameParentFolder_ReturnsNone()
    {
        var sources = new[] { @"C:\volumeC\source\file1.txt", @"C:\volumeC\source\file2.txt" };
        var res = DropEffectResolver.Resolve(_fs, sources, @"C:\volumeC\source", DragDropKeyStates.None);

        Assert.False(res.IsValid);
        Assert.Equal(DragDropEffects.None, res.Effect);
        Assert.Contains("containing folder", res.Reason ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DropDirectory_IntoItself_ReturnsNone()
    {
        var sources = new[] { @"C:\volumeC\source\subfolder" };
        var res = DropEffectResolver.Resolve(_fs, sources, @"C:\volumeC\source\subfolder", DragDropKeyStates.None);

        Assert.False(res.IsValid);
        Assert.Equal(DragDropEffects.None, res.Effect);
        Assert.Contains("itself", res.Reason ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DropDirectory_IntoItsDescendant_ReturnsNone()
    {
        _fs.CreateDirectory(@"C:\volumeC\source\subfolder\nested\deep");

        var sources = new[] { @"C:\volumeC\source\subfolder" };
        var res = DropEffectResolver.Resolve(_fs, sources, @"C:\volumeC\source\subfolder\nested\deep", DragDropKeyStates.None);

        Assert.False(res.IsValid);
        Assert.Equal(DragDropEffects.None, res.Effect);
        Assert.Contains("descendant", res.Reason ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NonExistentSource_OrDestination_ReturnsNone()
    {
        var res1 = DropEffectResolver.Resolve(_fs, new[] { @"C:\ghost.txt" }, @"C:\volumeC\dest", DragDropKeyStates.None);
        Assert.False(res1.IsValid);

        var res2 = DropEffectResolver.Resolve(_fs, new[] { @"C:\volumeC\source\file1.txt" }, @"C:\ghost_folder", DragDropKeyStates.None);
        Assert.False(res2.IsValid);
    }

    [Fact]
    public void PathComparison_IsCaseInsensitive()
    {
        var sources = new[] { @"c:\volumec\source\file1.txt" };
        var res = DropEffectResolver.Resolve(_fs, sources, @"C:\VOLUMEC\SOURCE", DragDropKeyStates.None);

        Assert.False(res.IsValid);
        Assert.Equal(DragDropEffects.None, res.Effect);
    }
}
