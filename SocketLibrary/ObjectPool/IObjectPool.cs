namespace Library.ObjectPool;

public interface IObjectPool<T>
{
    void Init(Func<T> factory);
    T Rent();
    bool TryRent(out T? obj);
    void Return(T obj);
    int Count { get; }
}
