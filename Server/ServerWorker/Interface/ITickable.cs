namespace Server.ServerWorker.Interface;

public interface ITickable
{
    void Tick();
    ulong SessionId { get; }
}
