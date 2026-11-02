using FM.Core.ShellIntegration;

namespace FM.Core.Tests;

public class ShellRegistrationTests
{
    [Fact]
    public void DefaultFileManager_RegistrationAndRestore_MaintainsStateAndBackup()
    {
        var service = new FakeShellRegistrationService();
        Assert.False(service.IsDefaultFileManager);

        var regResult = service.RegisterAsDefaultFileManager(@"C:\App\MyFileManager.exe");
        Assert.True(regResult.Success);
        Assert.True(service.IsDefaultFileManager);
        Assert.Equal("Explorer.exe", service.LastBackupValue);

        var restoreResult = service.RestoreDefaultFileManager();
        Assert.True(restoreResult.Success);
        Assert.False(service.IsDefaultFileManager);
    }

    [Fact]
    public void ContextMenuVerb_RegistrationAndUnregister_MaintainsState()
    {
        var service = new FakeShellRegistrationService();
        Assert.False(service.IsContextMenuVerbRegistered);

        var regResult = service.RegisterContextMenuVerb(@"C:\App\MyFileManager.exe");
        Assert.True(regResult.Success);
        Assert.True(service.IsContextMenuVerbRegistered);

        var unregResult = service.UnregisterContextMenuVerb();
        Assert.True(unregResult.Success);
        Assert.False(service.IsContextMenuVerbRegistered);
    }
}

