namespace FM.App.Services;

/// <summary>
/// Service abstraction for launching files and processes via the OS shell.
/// Decouples process execution from ViewModels for unit testability.
/// </summary>
public interface IShellLauncher
{
    void Launch(string path);
}
