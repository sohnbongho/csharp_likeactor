using Library.ContInfo;
using Library.Logger;
using Server.ServerWorker.Interface;
using Server.Actors.User;

namespace Server.ServerWorker;

public class ThreadWorker
{
    private readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();
    private readonly List<ITickable> _objs = new();
    private readonly ReaderWriterLockSlim _lock = new();
    private readonly Thread _thread;
    private readonly int _id;
    private bool _running = true;

    public ThreadWorker(int id)
    {
        _id = id;
        _thread = new Thread(Run) { IsBackground = true };
    }

    public void Start() => _thread.Start();
    public void Stop() => _running = false;

    public void Add(ITickable obj)
    {
        _lock.EnterWriteLock();
        try
        {
            _objs.Add(obj);
        }
        finally
        {
            _lock.ExitWriteLock();
        }   
    }

    public void Remove(ITickable obj)
    {
        _lock.EnterWriteLock();
        try 
        { 
            _objs.Remove(obj); 
        }
        finally 
        { 
            _lock.ExitWriteLock(); 
        }
    }

    private void Run()
    {
        while (_running)
        {
            List<ITickable> objs;
            _lock.EnterReadLock();
            try 
            { 
                objs = _objs.ToList(); 
            }
            finally 
            { 
                _lock.ExitReadLock(); 
            }

            try
            {
                foreach (var session in objs)
                {
                    session.Tick(); // 동기 Tick
                }
            }
            catch (Exception ex)
            {
                _logger.Error(() => $"[Worker#{_id} TickError] ", ex);
            }

            Thread.Sleep(ThreadConstInfo.TickMillSecond); // Tick 주기
        }
    }
}

