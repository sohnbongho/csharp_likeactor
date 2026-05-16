namespace Library.Worker.Interface;

public interface ITickable
{
    ValueTask TickAsync();
    ulong SessionId { get; }
}
