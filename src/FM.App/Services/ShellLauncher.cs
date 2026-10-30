using System.Diagnostics;

namespace FM.App.Services;

/// <summary>
/// Default implementation of <see cref="IShellLauncher"/> launching files with UseShellExecute = true.
/// </summary>
public class ShellLauncher : IShellLauncher
{
    public void Launch(string path)
    {
        var psi = new ProcessStartInfo(path)
        {
            UseShellExecute = true
        };
        Process.Start(psi);
    }
}
