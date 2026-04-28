using Library.ContInfo;
using Library.Worker;
using Library.Worker.Interface;

namespace Library.World;

// SessionId % N 고정 풀로 로비 스레드를 관리한다.
// 접속 직후부터 월드 입장 전까지 유저를 처리하며, 워커 ID는 1..N을 사용한다.
public class LobbyThreadManager
{
    private readonly TickThreadWorker[] _workers;
    private readonly ulong _workerCount;

    public LobbyThreadManager()
    {
        _workerCount = (ulong)ThreadConstInfo.MaxLobbyThreadCount;
        _workers = new TickThreadWorker[_workerCount];
        for (int i = 0; i < (int)_workerCount; i++)
            _workers[i] = new TickThreadWorker(i + 1); // 로비 워커: id 1..N
    }

    public void Start()
    {
        foreach (var w in _workers) w.Start();
    }

    public async Task StopAsync()
    {
        foreach (var w in _workers) await w.StopAsync();
    }

    public void Add(ITickable session) => GetWorker(session.SessionId).Add(session);
    public void Remove(ITickable session) => GetWorker(session.SessionId).Remove(session);

    private TickThreadWorker GetWorker(ulong sessionId) => _workers[sessionId % _workerCount];
}
