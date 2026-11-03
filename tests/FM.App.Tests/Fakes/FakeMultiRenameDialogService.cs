using FM.Core.FileSystem;
using FM.Core.Operations;
using FM.Core.Operations.MultiRename;

namespace FM.App.Tests.Fakes;

public class FakeMultiRenameDialogService : IMultiRenameDialogService
{
    public List<IReadOnlyList<MultiRenameItemInput>> ShownBatches { get; } = new();
    public MultiRenamePlan? NextPlan { get; set; }
    public Func<IReadOnlyList<MultiRenameItemInput>, IFileSystem, MultiRenamePlan>? PlanGenerator { get; set; }
    public bool DialogResult { get; set; } = true;

    public async Task<bool> ShowDialogAsync(
        IReadOnlyList<MultiRenameItemInput> items,
        IFileSystem fileSystem,
        Func<MultiRenamePlan, Task<OperationResult>> executeCallback,
        CancellationToken ct = default)
    {
        ShownBatches.Add(items);

        if (!DialogResult)
        {
            return false;
        }

        MultiRenamePlan plan;
        if (PlanGenerator != null)
        {
            plan = PlanGenerator(items, fileSystem);
        }
        else if (NextPlan != null)
        {
            plan = NextPlan;
        }
        else
        {
            // Default plan: append "_renamed" to each item
            var opts = new MultiRenameOptions("[N]_renamed.[E]");
            plan = MultiRenamePlanner.Plan(items, opts, fileSystem);
        }

        var result = await executeCallback(plan);
        return result.Success;
    }
}

