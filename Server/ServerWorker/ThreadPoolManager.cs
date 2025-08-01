using Server.ServerWorker.Interface;

namespace Server.ServerWorker;

public class ThreadPoolManager
{
    private readonly int _threadCount;
    private readonly ThreadWorker[] _workers;

    public ThreadPoolManager(int threadCount)
    {
        _threadCount = threadCount;
        _workers = new ThreadWorker[threadCount];
        
    }
    public void Start()
    {
        for (int i = 0; i < _threadCount; i++)
        {
            _workers[i] = new ThreadWorker(i);
            _workers[i].Start();
        }
    }

    public void Add(ITickable iTickable)
    {
        var index = iTickable.SessionId % (ulong)_threadCount;
        _workers[index].Add(iTickable);
    }

    public void Remove(ITickable iTickable)
    {
        var index = iTickable.SessionId % (ulong)_threadCount;
        _workers[index].Remove(iTickable);
    }

    public void StopAll()
    {
        foreach (var worker in _workers)
            worker.Stop();
    }
}
