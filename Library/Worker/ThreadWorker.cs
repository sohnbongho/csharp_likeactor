using Library.ContInfo;
using Library.Logger;
using Library.Worker.Interface;
using System.Collections.Concurrent;
using System.Threading;

namespace Library.Worker;

public class ThreadWorker
{
    private readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();
    private readonly ConcurrentDictionary<ulong, ITickable> _sessions = new();
    private readonly Thread _thread;
    private readonly int _id;
    private readonly CancellationTokenSource _cts = new();

    public ThreadWorker(int id)
    {
        _id = id;
        _thread = new Thread(Run) { IsBackground = true };
    }

    public void Start() => _thread.Start();

    public void Stop()
    {
        _cts.Cancel();          // 루프 종료 요청
        _thread.Join();         // 안전하게 종료 대기
    }

    public void Add(ITickable obj)
    {
        _sessions[obj.SessionId] = obj;
    }

    public void Remove(ITickable obj)
    {
        _sessions.TryRemove(obj.SessionId, out _);
    }

    private void Run()
    {
        var token = _cts.Token;

        while (!token.IsCancellationRequested)
        {
            try
            {
                // snapshot 기반 foreach (ConcurrentDictionary 특징)
                foreach (var session in _sessions.Values)
                {
                    // 혹시라도 Stop()이 들어온 경우 빠르게 빠져나오고 싶으면 체크
                    if (token.IsCancellationRequested)
                        break;

                    session.Tick();
                }
            }
            catch (Exception ex)
            {
                _logger.Error(() => $"[Worker#{_id} TickError] ", ex);
            }

            // Tick 주기
            try
            {
                // Cancel 가능 sleep
                token.WaitHandle.WaitOne(ThreadConstInfo.TickMillSecond);
            }
            catch (ObjectDisposedException)
            {
                // cts가 Dispose 된 경우 대비 (원하면 생략 가능)
                break;
            }
        }
    }
}
