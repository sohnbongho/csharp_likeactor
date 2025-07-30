using Library.Logger;
using System.Collections.Concurrent;

namespace Library.ObjectPool;

public class ObjectPool<T> : IObjectPool<T> where T : class
{
    private readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();
    private readonly ConcurrentBag<T> _pool = new();
    private readonly int _maxSize;
    private readonly Func<T> _factory;

    public ObjectPool(int size, Func<T> factory)
    {
        _maxSize = size;
        _factory = factory;

        for (int i = 0; i < size; i++)
        {            
            _pool.Add(_factory());
        }

        _logger.Info(() => $"[ObjectPool<{typeof(T).Name}>] Initialized with size: {size}");
    }

    public T Rent()
    {
        if (_pool.TryTake(out var obj))
        {
            _logger.Debug(() => $"[ObjectPool<{typeof(T).Name}>] Rent Count: {_pool.Count}");
            return obj;
        }

        throw new InvalidOperationException($"[ObjectPool<{typeof(T).Name}>] Pool exhausted");
    }

    public void Return(T obj)
    {
        _pool.Add(obj);
        _logger.Debug(() => $"[ObjectPool<{typeof(T).Name}>] Return Count:{_pool.Count}");
    }

    public int Count => _pool.Count;
}
