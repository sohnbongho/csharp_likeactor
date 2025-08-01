namespace Library.ObjectPool;

public interface IObjectPool<T>
{
    void Init(Func<T> factory);
    T Rent();
    void Return(T obj);
    int Count { get; }
}
