using Library.Worker;
using Library.Worker.Interface;

namespace Library.World;

// 월드에 진입하기 전 유저를 처리하는 단일 전용 스레드.
// 접속 직후부터 월드 입장 전까지 모든 유저가 이 스레드에서 tick/message를 처리한다.
public class LobbyThreadManager
{
    private readonly TickThreadWorker _worker = new(0);

    public void Start() => _worker.Start();
    public Task StopAsync() => _worker.StopAsync();

    public void Add(ITickable session) => _worker.Add(session);
    public void Remove(ITickable session) => _worker.Remove(session);
}
