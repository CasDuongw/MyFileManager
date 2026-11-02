namespace FM.App.Services.SingleInstance;

/// <summary>
/// Service coordinating single-instance application lifetime and forwarding command-line arguments.
/// </summary>
public interface ISingleInstanceService : IDisposable
{
    bool IsFirstInstance { get; }
    Task StartServerAsync(Func<string[], Task> onArgsReceived, CancellationToken ct = default);
    Task<bool> ForwardArgsToRunningInstanceAsync(string[] args, CancellationToken ct = default);
}

