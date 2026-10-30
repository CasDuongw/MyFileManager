using FM.Core.Navigation;

namespace FM.Core.Tests;

public class NavigationHistoryTests
{
    [Fact]
    public void InitialState_HasNoHistory()
    {
        var history = new NavigationHistory(@"C:\Users");

        Assert.Equal(@"C:\Users", history.CurrentPath);
        Assert.False(history.CanGoBack);
        Assert.False(history.CanGoForward);
    }

    [Fact]
    public void NavigateToNewPath_PushesToBackStackAndClearsForwardStack()
    {
        var history = new NavigationHistory(@"C:\Folder1");

        history.CommitNavigate(@"C:\Folder2");

        Assert.Equal(@"C:\Folder2", history.CurrentPath);
        Assert.True(history.CanGoBack);
        Assert.False(history.CanGoForward);

        // Go back to Folder1
        string? back = history.CommitBack();
        Assert.Equal(@"C:\Folder1", back);
        Assert.Equal(@"C:\Folder1", history.CurrentPath);
        Assert.False(history.CanGoBack);
        Assert.True(history.CanGoForward);

        // Go forward to Folder2
        string? forward = history.CommitForward();
        Assert.Equal(@"C:\Folder2", forward);
        Assert.Equal(@"C:\Folder2", history.CurrentPath);
        Assert.True(history.CanGoBack);
        Assert.False(history.CanGoForward);
    }

    [Fact]
    public void NavigatingToSamePath_DoesNotDuplicateHistory()
    {
        var history = new NavigationHistory(@"C:\Folder1");

        bool navigated = history.CommitNavigate(@"C:\Folder1");
        Assert.False(navigated);
        Assert.False(history.CanGoBack);
    }

    [Fact]
    public void NavigateAfterGoBack_ClearsForwardStack()
    {
        var history = new NavigationHistory(@"C:\1");
        history.CommitNavigate(@"C:\2");
        history.CommitNavigate(@"C:\3");

        history.CommitBack(); // Current is C:\2, Forward has C:\3
        Assert.True(history.CanGoForward);

        history.CommitNavigate(@"C:\4"); // Navigating clears Forward stack
        Assert.Equal(@"C:\4", history.CurrentPath);
        Assert.False(history.CanGoForward);
        Assert.True(history.CanGoBack);
    }

    [Fact]
    public void GetParentPath_ReturnsParentDirectory()
    {
        var history = new NavigationHistory(@"C:\Windows\System32");

        string? parent = history.GetParentPath();

        Assert.NotNull(parent);
        Assert.EndsWith("Windows", parent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TwoPhaseNavigation_TargetFailsValidation_HistoryRemainsConsistent()
    {
        var history = new NavigationHistory(@"C:\FolderA");
        history.CommitNavigate(@"C:\FolderB");

        // User requests GoBack -> peek target
        string? target = history.PeekBack();
        Assert.Equal(@"C:\FolderA", target);

        // Validation fails (e.g. FolderA was deleted), so CommitBack is NOT called.
        // History state must remain completely consistent at FolderB.
        Assert.Equal(@"C:\FolderB", history.CurrentPath);
        Assert.True(history.CanGoBack);
        Assert.False(history.CanGoForward);
        Assert.Equal(@"C:\FolderA", history.PeekBack());
    }

    [Fact]
    public void TwoPhaseNavigation_Success_CommitsProperly()
    {
        var history = new NavigationHistory(@"C:\FolderA");
        history.CommitNavigate(@"C:\FolderB");

        // Target validated successfully -> CommitBack
        string? newPath = history.CommitBack();
        Assert.Equal(@"C:\FolderA", newPath);
        Assert.Equal(@"C:\FolderA", history.CurrentPath);
        Assert.False(history.CanGoBack);
        Assert.True(history.CanGoForward);
        Assert.Equal(@"C:\FolderB", history.PeekForward());
    }
}
