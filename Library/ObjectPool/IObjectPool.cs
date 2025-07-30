namespace Library.ObjectPool;

public interface IObjectPool<T>
{
    T Rent();
    void Return(T obj);
    int Count { get; }
}
