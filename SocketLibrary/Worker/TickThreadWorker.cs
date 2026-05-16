using Library.ContInfo;
using Library.Logger;
using Library.Worker.Interface;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Library.Worker;

public class TickThreadWorker
{
    private static readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();
    private readonly ConcurrentDictionary<ulong, ITickable> _sessions = new();
    private readonly int _id;
    private readonly CancellationTokenSource _cts = new();
    private Task? _task;

    public TickThreadWorker(int id)
    {
        _id = id;
    }

    public void Start()
    {
        _task = Task.Run(RunAsync, _cts.Token);
    }

    public async Task StopAsync()
    {
        _cts.Cancel();
        if (_task != null)
        {
            try { await _task; }
            catch (OperationCanceledException) { }
        }
    }

    public void Add(ITickable obj) => _sessions[obj.SessionId] = obj;
    public void Remove(ITickable obj) => _sessions.TryRemove(obj.SessionId, out _);

    private async Task RunAsync()
    {
        var token = _cts.Token;
        while (!token.IsCancellationRequested)
        {
            var start = Stopwatch.GetTimestamp();

            foreach (var session in _sessions.Values)
            {
                if (token.IsCancellationRequested) break;
                try
                {
                    await session.TickAsync();
                }
                catch (Exception ex)
                {
                    _logger.Error(() => $"[Worker#{_id} TickError]", ex);
                }
            }

            var remaining = TimeSpan.FromMilliseconds(ThreadConstInfo.TickMillSecond) - Stopwatch.GetElapsedTime(start);
            if (remaining > TimeSpan.Zero)
            {
                try { await Task.Delay(remaining, token); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}
