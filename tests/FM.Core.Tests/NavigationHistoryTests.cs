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

        history.Navigate(@"C:\Folder2");

        Assert.Equal(@"C:\Folder2", history.CurrentPath);
        Assert.True(history.CanGoBack);
        Assert.False(history.CanGoForward);

        // Go back to Folder1
        string? back = history.GoBack();
        Assert.Equal(@"C:\Folder1", back);
        Assert.Equal(@"C:\Folder1", history.CurrentPath);
        Assert.False(history.CanGoBack);
        Assert.True(history.CanGoForward);

        // Go forward to Folder2
        string? forward = history.GoForward();
        Assert.Equal(@"C:\Folder2", forward);
        Assert.Equal(@"C:\Folder2", history.CurrentPath);
        Assert.True(history.CanGoBack);
        Assert.False(history.CanGoForward);
    }

    [Fact]
    public void NavigatingToSamePath_DoesNotDuplicateHistory()
    {
        var history = new NavigationHistory(@"C:\Folder1");

        bool navigated = history.Navigate(@"C:\Folder1");
        Assert.False(navigated);
        Assert.False(history.CanGoBack);
    }

    [Fact]
    public void NavigateAfterGoBack_ClearsForwardStack()
    {
        var history = new NavigationHistory(@"C:\1");
        history.Navigate(@"C:\2");
        history.Navigate(@"C:\3");

        history.GoBack(); // Current is C:\2, Forward has C:\3
        Assert.True(history.CanGoForward);

        history.Navigate(@"C:\4"); // Navigating clears Forward stack
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
}
