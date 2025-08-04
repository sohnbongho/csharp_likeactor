using Library.Logger;
using System.Collections.Concurrent;

namespace Library.ObjectPool;

public class ObjectPool<T> : IObjectPool<T> where T : class
{
    private readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();
    private readonly ConcurrentQueue<T> _pool = new();
    private readonly int _maxSize;

    public ObjectPool(int size)
    {
        _maxSize = size;
    }
    public void Init(Func<T> factory)
    {
        for (int i = 0; i < _maxSize; i++)
        {
            _pool.Enqueue(factory());
        }

        _logger.Info(() => $"[ObjectPool<{typeof(T).Name}>] Initialized with size: {_maxSize}");
    }

    public T Rent()
    {
        if (_pool.TryDequeue(out var obj))
        {
            _logger.Debug(() => $"[ObjectPool<{typeof(T).Name}>] Rent Count: {_pool.Count}");
            return obj;
        }

        throw new InvalidOperationException($"[ObjectPool<{typeof(T).Name}>] Pool exhausted");
    }

    public void Return(T obj)
    {
        _pool.Enqueue(obj);
        _logger.Debug(() => $"[ObjectPool<{typeof(T).Name}>] Return Count:{_pool.Count}");
    }

    public int Count => _pool.Count;
}
