using System.ComponentModel;
using FM.App.Services;

namespace FM.App.Tests.Fakes;

public class FakeShellLauncher : IShellLauncher
{
    public List<string> LaunchedPaths { get; } = new();
    public Exception? ExceptionToThrow { get; set; }

    public void Launch(string path)
    {
        if (ExceptionToThrow != null)
        {
            throw ExceptionToThrow;
        }
        LaunchedPaths.Add(path);
    }
}
