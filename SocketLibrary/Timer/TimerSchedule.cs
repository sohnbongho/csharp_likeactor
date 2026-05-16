using Library.Timer.Message;

namespace Library.Timer;

public class TimerSchedule
{
    public ITimerMessage Message { get; set; }
    public long ExpireTime { get; private set; }    
    public Func<ITimerMessage, bool>? Callback { get; private set; }
    public Func<ITimerMessage, Task<bool>>? CallbackAsync { get; private set; }

    public TimerSchedule(ITimerMessage message, long expireTime, Func<ITimerMessage, bool>? callback, Func<ITimerMessage, Task<bool>>? callbackAsync = null)
    {
        Message = message;
        ExpireTime = expireTime;
        Callback = callback;
        CallbackAsync = callbackAsync;
    }
    public bool IsAsync()
    {
        return CallbackAsync != null;
    }

    public async Task<bool> InvokeAsync()
    {
        if (CallbackAsync != null)
        {
            await CallbackAsync(Message);
            return true;
        }
        
        if (Callback != null)
        {
            Callback(Message);
            return true;
        }
        return true;
    }

    public bool Invoke()
    {
        if (Callback != null)
        {
            Callback(Message);            
        }
        return true;
    }
}

