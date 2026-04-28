using Library.ContInfo;
using Library.Worker;
using Library.Worker.Interface;

namespace Library.World;

// WorldId % N 고정 풀로 월드 스레드를 관리한다.
// 같은 WorldId의 유저는 항상 같은 스레드에서 실행 → 유저 간 전투 시 추가 잠금 불필요.
public class WorldThreadManager
{
    private readonly TickThreadWorker[] _workers;
    private readonly ulong _workerCount;

    public WorldThreadManager()
    {
        _workerCount = (ulong)ThreadConstInfo.MaxWorldThreadCount;
        _workers = new TickThreadWorker[_workerCount];
        for (int i = 0; i < (int)_workerCount; i++)
            _workers[i] = new TickThreadWorker(1001 + i); // 월드 워커: id 1001..
    }

    public void Start()
    {
        foreach (var w in _workers) w.Start();
    }

    public async Task StopAllAsync()
    {
        foreach (var w in _workers) await w.StopAsync();
    }

    public void Add(ITickable session, ulong worldId) => GetWorker(worldId).Add(session);
    public void Remove(ITickable session, ulong worldId) => GetWorker(worldId).Remove(session);

    private TickThreadWorker GetWorker(ulong worldId) => _workers[worldId % _workerCount];
}
