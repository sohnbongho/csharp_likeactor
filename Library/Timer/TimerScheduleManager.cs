using Library.Logger;
using Library.Timer.Message;
using System.Diagnostics;

namespace Library.Timer;

public class TimerScheduleManager : IDisposable
{
    private readonly List<TimerSchedule> _timers = new();
    private readonly object _lock = new();
    private readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();

    public void AddSchedule(
        ITimerMessage message, 
        TimeSpan delay, 
        Func<ITimerMessage, bool>? callback, 
        Func<ITimerMessage, Task<bool>>? callbackAsync = null)
    {
        var now = Stopwatch.GetTimestamp();
        var tick = delay.TotalMilliseconds / 1000.0 * Stopwatch.Frequency;
        var expire = now + (long)tick;

        var timer = new TimerSchedule(message, expire, callback, callbackAsync);

        lock (_lock)
        {
            _timers.Add(timer);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _timers.Clear();
        }
    }

    public void Tick()
    {
        var now = Stopwatch.GetTimestamp();
        List<TimerSchedule> expired;

        lock (_lock)
        {
            expired = _timers.Where(t => t.ExpireTime <= now).ToList();
            _timers.RemoveAll(t => t.ExpireTime <= now);
        }

        foreach (var timer in expired)
        {
            try
            {
                timer.Invoke();
            }
            catch (Exception ex)
            {
                _logger.Error(() => "fail TimerScheduleManager", ex);
            }
        }
    }
}
