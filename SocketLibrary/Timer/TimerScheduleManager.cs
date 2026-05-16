using Library.ContInfo;
using Library.Logger;
using Library.Timer.Message;
using System.Diagnostics;

namespace Library.Timer;

public class TimerScheduleManager : IDisposable
{
    // 만료시각(정수 틱) 기준 최소 힙 — Add/ExtractExpired O(log n).
    private readonly PriorityQueue<TimerSchedule, long> _timers = new();
    private readonly object _lock = new();
    private static readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();

    public void AddSchedule(
        ITimerMessage message,
        TimeSpan delay,
        Func<ITimerMessage, bool>? callback,
        Func<ITimerMessage, Task<bool>>? callbackAsync = null)
    {
        // 정수 연산으로 정밀도 손실 방지
        var expire = Stopwatch.GetTimestamp()
            + (long)((double)delay.Ticks / TimeSpan.TicksPerSecond * Stopwatch.Frequency);

        var timer = new TimerSchedule(message, expire, callback, callbackAsync);

        lock (_lock)
        {
            if (_timers.Count >= SessionConstInfo.MaxTimerPerSession)
            {
                _logger.Warn(() => $"타이머 한도 초과 ({SessionConstInfo.MaxTimerPerSession}), 등록 무시");
                return;
            }
            _timers.Enqueue(timer, expire);
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _timers.Clear();
        }
    }

    public void Dispose() => Reset();

    public void Tick()
    {
        var now = Stopwatch.GetTimestamp();

        while (true)
        {
            TimerSchedule? timer = null;
            lock (_lock)
            {
                if (_timers.TryPeek(out var head, out var expireAt) && expireAt <= now)
                {
                    _timers.Dequeue();
                    timer = head;
                }
            }

            if (timer == null)
                break;

            try
            {
                if (timer.IsAsync())
                {
                    // Tick 스레드를 블로킹하지 않도록 fire-and-forget. 예외는 내부에서 로깅.
                    _ = InvokeAsyncSafe(timer);
                }
                else
                {
                    timer.Invoke();
                }
            }
            catch (Exception ex)
            {
                _logger.Error(() => "fail TimerScheduleManager", ex);
            }
        }
    }

    private async Task InvokeAsyncSafe(TimerSchedule timer)
    {
        try
        {
            await timer.InvokeAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(() => "fail TimerScheduleManager(async)", ex);
        }
    }
}
