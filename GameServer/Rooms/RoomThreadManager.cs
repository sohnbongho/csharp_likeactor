using Library.Worker;
using Library.Worker.Interface;

namespace GameServer.Rooms;

// RoomId % N 고정 풀로 룸(전투) 스레드를 관리한다. 룸 하나는 항상 같은 스레드에서 틱되므로
// 룸 내부 상태(시뮬레이션, 멤버 목록)에는 잠금이 필요 없다.
// 세션용 워커(100 ms)와 달리 전투 시뮬 틱 간격(50 ms)으로 돈다.
public sealed class RoomThreadManager
{
    public static readonly int WorkerCount = Math.Max(2, Environment.ProcessorCount / 2);

    private readonly TickThreadWorker[] _workers;

    public RoomThreadManager(int tickMs)
    {
        _workers = new TickThreadWorker[WorkerCount];
        for (int i = 0; i < WorkerCount; i++)
            _workers[i] = new TickThreadWorker(2001 + i, tickMs); // 룸 워커: id 2001..
    }

    public void Start()
    {
        foreach (var w in _workers) w.Start();
    }

    public async Task StopAsync()
    {
        foreach (var w in _workers) await w.StopAsync();
    }

    public void Add(ITickable room) => _workers[room.SessionId % (ulong)WorkerCount].Add(room);
    public void Remove(ITickable room) => _workers[room.SessionId % (ulong)WorkerCount].Remove(room);
}
