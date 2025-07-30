using System.Collections.Concurrent;

namespace Library.MessageQueue;

public class MessageQueue<T> where T : class
{
    private readonly ConcurrentQueue<T> _queue = new();

    public void Enqueue(T message) => _queue.Enqueue(message);

    public bool TryDequeue(out T? message) => _queue.TryDequeue(out message);

    public int Count => _queue.Count;

    public void Clear()
    {
        while (_queue.TryDequeue(out _)) { }
    }

    public void Dispose()
    {
        Clear();
    }

    public bool IsEmpty => _queue.IsEmpty;
}
