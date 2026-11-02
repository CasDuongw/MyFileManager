using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace FM.App.Services.SingleInstance;

/// <summary>
/// Production single-instance service using a local Mutex and Named Pipe IPC server.
/// </summary>
public sealed class NamedPipeSingleInstanceService : ISingleInstanceService
{
    private readonly string _mutexName;
    private readonly string _pipeName;
    private Mutex? _mutex;
    private bool _hasLock;
    private CancellationTokenSource? _serverCts;

    public bool IsFirstInstance => _hasLock;

    public NamedPipeSingleInstanceService(string? customIdentifier = null)
    {
        string id = customIdentifier ?? Environment.UserName;
        // Strip non-alphanumeric chars for safe named object identifiers
        string safeId = new(id.Where(char.IsLetterOrDigit).ToArray());
        if (string.IsNullOrEmpty(safeId)) safeId = "Default";

        _mutexName = $"Local\\MyFileManager_SingleInstance_{safeId}";
        _pipeName = $"MyFileManager_IPC_{safeId}";

        try
        {
            _mutex = new Mutex(true, _mutexName, out bool createdNew);
            _hasLock = createdNew;
        }
        catch (AbandonedMutexException)
        {
            _hasLock = true;
        }
        catch
        {
            _hasLock = false;
        }
    }

    public Task StartServerAsync(Func<string[], Task> onArgsReceived, CancellationToken ct = default)
    {
        if (!_hasLock) return Task.CompletedTask;

        _serverCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _serverCts.Token;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        _pipeName,
                        PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(token).ConfigureAwait(false);

                    using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
                    string? line = await reader.ReadLineAsync(token).ConfigureAwait(false);

                    if (!string.IsNullOrEmpty(line))
                    {
                        string[] receivedArgs;
                        try
                        {
                            receivedArgs = JsonSerializer.Deserialize<string[]>(line) ?? Array.Empty<string>();
                        }
                        catch
                        {
                            receivedArgs = new[] { line };
                        }

                        await onArgsReceived(receivedArgs).ConfigureAwait(false);
                    }

                    // Send ack
                    using var writer = new StreamWriter(server, Encoding.UTF8, leaveOpen: true);
                    await writer.WriteLineAsync("OK".AsMemory(), token).ConfigureAwait(false);
                    await writer.FlushAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Delay slightly to avoid rapid retry on transient IPC errors
                    try { await Task.Delay(200, token).ConfigureAwait(false); } catch { }
                }
            }
        }, token);

        return Task.CompletedTask;
    }

    public async Task<bool> ForwardArgsToRunningInstanceAsync(string[] args, CancellationToken ct = default)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(1500, ct).ConfigureAwait(false);

            using var writer = new StreamWriter(client, Encoding.UTF8, leaveOpen: true);
            string json = JsonSerializer.Serialize(args);
            await writer.WriteLineAsync(json.AsMemory(), ct).ConfigureAwait(false);
            await writer.FlushAsync(ct).ConfigureAwait(false);

            using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);
            string? ack = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            return ack == "OK";
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _serverCts?.Cancel();
        _serverCts?.Dispose();
        _serverCts = null;

        if (_mutex != null)
        {
            if (_hasLock)
            {
                try { _mutex.ReleaseMutex(); } catch { }
            }
            _mutex.Dispose();
            _mutex = null;
        }
    }
}

