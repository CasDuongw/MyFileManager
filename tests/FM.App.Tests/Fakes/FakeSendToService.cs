using FM.Core.ShellIntegration;

namespace FM.App.Tests.Fakes;

public class FakeSendToService : ISendToService
{
    private readonly List<SendToTarget> _targets = new();
    public List<SendToTarget> Targets => _targets;
    public List<(SendToTarget Target, IReadOnlyList<string> Sources)> Executions { get; } = new();

    public FakeSendToService(IEnumerable<SendToTarget>? targets = null)
    {
        if (targets != null)
        {
            _targets.AddRange(targets);
        }
        else
        {
            _targets.Add(new SendToTarget("Desktop (create shortcut)", "Desktop.desklink", IsDesktopShortcut: true));
            _targets.Add(new SendToTarget("Compressed (zipped) folder", "Zip.zfsendtarget", IsZipCompressor: true));
            _targets.Add(new SendToTarget("Documents", "Docs.mydocs", ResolvedDirectory: @"C:\Users\Test\Documents"));
        }
    }

    public IReadOnlyList<SendToTarget> GetTargets() => _targets.ToList();

    public async Task<bool> ExecuteSendToAsync(
        SendToTarget target,
        IReadOnlyList<string> sourcePaths,
        Func<IReadOnlyList<string>, string, Task> copyToFolderAction,
        Func<IReadOnlyList<string>, Task> zipAction,
        CancellationToken ct = default)
    {
        Executions.Add((target, sourcePaths));

        if (target.IsZipCompressor)
        {
            await zipAction(sourcePaths);
            return true;
        }

        if (!string.IsNullOrEmpty(target.ResolvedDirectory))
        {
            await copyToFolderAction(sourcePaths, target.ResolvedDirectory);
            return true;
        }

        return true;
    }
}
