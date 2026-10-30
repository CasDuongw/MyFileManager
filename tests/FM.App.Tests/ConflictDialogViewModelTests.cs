using FM.App.Tests.Fakes;
using FM.App.ViewModels;
using FM.Core.Operations.Transfer;

namespace FM.App.Tests;

public class ConflictDialogViewModelTests
{
    [Fact]
    public void Badges_DateTolerance_OneSecondOrLess_NoNewerOlderBadge()
    {
        var baseTime = new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);
        var info = new ConflictInfo(
            @"C:\src\doc.txt", 100, baseTime,
            @"D:\dst\doc.txt", 100, baseTime.AddSeconds(0.5)); // 0.5s difference

        var vm = new ConflictDialogViewModel(info);

        Assert.False(vm.SourceIsNewer);
        Assert.False(vm.DestIsNewer);
        Assert.False(vm.SourceIsLarger);
        Assert.False(vm.DestIsLarger);
    }

    [Fact]
    public void Badges_DateDifference_GreaterThanOneSecond_SetsCorrectBadge()
    {
        var baseTime = new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);
        var info = new ConflictInfo(
            @"C:\src\doc.txt", 200, baseTime.AddSeconds(5),
            @"D:\dst\doc.txt", 100, baseTime);

        var vm = new ConflictDialogViewModel(info);

        Assert.True(vm.SourceIsNewer);
        Assert.False(vm.DestIsNewer);
        Assert.True(vm.SourceIsLarger);
        Assert.False(vm.DestIsLarger);
        Assert.Equal("Incoming is newer → will overwrite", vm.OverwriteIfNewerHint);
    }

    [Fact]
    public void UnknownDate_MinValue_DisplaysUnknown_NoBadges()
    {
        var info = new ConflictInfo(
            @"C:\src\doc.txt", 100, DateTime.MinValue,
            @"D:\dst\doc.txt", 100, DateTime.MinValue);

        var vm = new ConflictDialogViewModel(info);

        Assert.Equal("Unknown", vm.SourceModifiedText);
        Assert.Equal("Unknown", vm.DestModifiedText);
        Assert.False(vm.SourceIsNewer);
        Assert.False(vm.DestIsNewer);
    }

    [Fact]
    public void DynamicBanner_ReflectsOverwriteIsUndoable()
    {
        var infoUndoable = new ConflictInfo(
            @"C:\src\doc.txt", 100, DateTime.UtcNow,
            @"D:\dst\doc.txt", 100, DateTime.UtcNow,
            OverwriteIsUndoable: true);

        var vmUndoable = new ConflictDialogViewModel(infoUndoable);
        Assert.False(vmUndoable.BannerIsWarning);
        Assert.Contains("saved to a safe backup", vmUndoable.BannerText);
        Assert.Contains("Ctrl+Z", vmUndoable.BannerText);

        var infoNonUndoable = new ConflictInfo(
            @"C:\src\doc.txt", 100, DateTime.UtcNow,
            @"D:\dst\doc.txt", 100, DateTime.UtcNow,
            OverwriteIsUndoable: false);

        var vmNonUndoable = new ConflictDialogViewModel(infoNonUndoable);
        Assert.True(vmNonUndoable.BannerIsWarning);
        Assert.Contains("cannot be restored with Undo", vmNonUndoable.BannerText);
    }

    [Fact]
    public void Actions_ProduceCorrectDecisions_AndTriggerRequestClose()
    {
        var info = new ConflictInfo(@"C:\src\doc.txt", 100, DateTime.UtcNow, @"D:\dst\doc.txt", 100, DateTime.UtcNow);
        var vm = new ConflictDialogViewModel(info);
        bool closed = false;
        vm.RequestClose += () => closed = true;

        vm.ApplyToAll = true;
        vm.ChooseKeepBoth();

        Assert.True(closed);
        Assert.Equal(ConflictAction.KeepBoth, vm.Decision.Action);
        Assert.True(vm.Decision.ApplyToAll);
    }

    [Fact]
    public async Task Thumbnails_SlowOrFaulty_DoesNotBlockDialogCreation()
    {
        var tcs = new TaskCompletionSource<System.Windows.Media.ImageSource?>();
        var fakeThumbnail = new FakeThumbnailService
        {
            Handler = (p, sz, ct) => tcs.Task // Never resolves or slow
        };

        var info = new ConflictInfo(@"C:\src\doc.txt", 100, DateTime.UtcNow, @"D:\dst\doc.txt", 100, DateTime.UtcNow);

        // Dialog creation must return immediately without waiting for thumbnail
        var vm = new ConflictDialogViewModel(info, fakeThumbnail);

        Assert.Equal("doc.txt", vm.FileName);
        Assert.Null(vm.SourceThumbnail);
        Assert.Null(vm.DestThumbnail);

        // Cancel thumbnails
        vm.CancelThumbnailLoading();
    }
}
