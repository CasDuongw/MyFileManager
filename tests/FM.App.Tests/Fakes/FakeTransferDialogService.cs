using FM.App.Services;
using FM.Core.Operations.Transfer;

namespace FM.App.Tests.Fakes;

public class FakeTransferDialogService : ITransferDialogService
{
    public List<ConflictInfo> ConflictCalls { get; } = new();
    public List<FailureInfo> FailureCalls { get; } = new();

    public Func<ConflictInfo, CancellationToken, Task<ConflictDecision>>? ConflictHandler { get; set; }
    public Func<FailureInfo, CancellationToken, Task<FailureDecision>>? FailureHandler { get; set; }

    public Task<ConflictDecision> ShowConflictDialogAsync(ConflictInfo info, CancellationToken ct = default)
    {
        ConflictCalls.Add(info);
        if (ConflictHandler != null)
        {
            return ConflictHandler(info, ct);
        }
        return Task.FromResult(new ConflictDecision(ConflictAction.KeepBoth));
    }

    public Task<FailureDecision> ShowFailureDialogAsync(FailureInfo info, CancellationToken ct = default)
    {
        FailureCalls.Add(info);
        if (FailureHandler != null)
        {
            return FailureHandler(info, ct);
        }
        return Task.FromResult(new FailureDecision(FailureAction.Skip));
    }
}
