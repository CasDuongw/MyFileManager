namespace FM.App.Services.SingleInstance;

/// <summary>
/// Controllable in-memory test double for single-instance testing.
/// </summary>
public sealed class FakeSingleInstanceService : ISingleInstanceService
{
    public bool IsFirstInstance { get; set; } = true;
    public List<string[]> ForwardedCalls { get; } = new();
    private Func<string[], Task>? _serverHandler;

    public Task StartServerAsync(Func<string[], Task> onArgsReceived, CancellationToken ct = default)
    {
        _serverHandler = onArgsReceived;
        return Task.CompletedTask;
    }

    public async Task<bool> ForwardArgsToRunningInstanceAsync(string[] args, CancellationToken ct = default)
    {
        ForwardedCalls.Add(args);
        if (_serverHandler != null)
        {
            await _serverHandler(args);
            return true;
        }
        return true;
    }

    public void Dispose()
    {
    }
}

