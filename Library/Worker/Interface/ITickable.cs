namespace Library.Worker.Interface;

public interface ITickable
{
    void Tick();
    ulong SessionId { get; }
}
