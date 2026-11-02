namespace FM.Core.ShellIntegration;

public record ShellRegistrationResult(bool Success, string Message, string? BackupValue = null);

/// <summary>
/// Abstraction for Windows Shell registry integration (default file manager and context menu verbs).
/// Keeps pure logic testable without touching the physical Windows Registry.
/// </summary>
public interface IShellRegistrationService
{
    bool IsDefaultFileManager { get; }
    bool IsContextMenuVerbRegistered { get; }

    ShellRegistrationResult RegisterAsDefaultFileManager(string applicationPath);
    ShellRegistrationResult RestoreDefaultFileManager();

    ShellRegistrationResult RegisterContextMenuVerb(string applicationPath);
    ShellRegistrationResult UnregisterContextMenuVerb();
}

