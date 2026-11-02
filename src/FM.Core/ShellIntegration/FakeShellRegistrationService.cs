namespace FM.Core.ShellIntegration;

/// <summary>
/// Controllable in-memory shell registration service for tests.
/// Never touches the real Windows Registry.
/// </summary>
public sealed class FakeShellRegistrationService : IShellRegistrationService
{
    public bool IsDefaultFileManager { get; set; }
    public bool IsContextMenuVerbRegistered { get; set; }
    public string? LastBackupValue { get; private set; }

    public ShellRegistrationResult RegisterAsDefaultFileManager(string applicationPath)
    {
        LastBackupValue = "Explorer.exe";
        IsDefaultFileManager = true;
        return new ShellRegistrationResult(true, "Default file manager registered successfully in memory.", LastBackupValue);
    }

    public ShellRegistrationResult RestoreDefaultFileManager()
    {
        IsDefaultFileManager = false;
        return new ShellRegistrationResult(true, "Default file manager restored to Explorer.exe.");
    }

    public ShellRegistrationResult RegisterContextMenuVerb(string applicationPath)
    {
        IsContextMenuVerbRegistered = true;
        return new ShellRegistrationResult(true, "Context menu verb registered successfully in memory.");
    }

    public ShellRegistrationResult UnregisterContextMenuVerb()
    {
        IsContextMenuVerbRegistered = false;
        return new ShellRegistrationResult(true, "Context menu verb unregistered successfully in memory.");
    }
}

