using System.Collections.Concurrent;
using System.Net.Sockets;

namespace Server.Acceptor;

public class SocketAsyncEventArgsPool
{
    private readonly ConcurrentStack<SocketAsyncEventArgs> _pool;

    public SocketAsyncEventArgsPool(int capacity)
    {
        _pool = new ConcurrentStack<SocketAsyncEventArgs>();
    }

    public void Push(SocketAsyncEventArgs args) => _pool.Push(args);

    public bool TryPop(out SocketAsyncEventArgs? args) => _pool.TryPop(out args);
}

