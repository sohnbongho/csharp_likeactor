using Library.Logger;
using Server.Session.User;
using System.Collections.Concurrent;

namespace Server.Session.UserPool;

public interface ISessionPool
{
    UserSession Rent();
    void Return(UserSession session);
}

public class UserSessionPool : ISessionPool
{
    private readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();
    private readonly ConcurrentBag<UserSession> _pool = new();
    private readonly int _maxSize;

    public UserSessionPool(int size)
    {
        _maxSize = size;

        for (int i = 0; i < size; i++)
        {
            _pool.Add(UserSession.Of());
        }

        _logger.Info(() => $"User Session Pool Size:{size}");
    }

    public UserSession Rent()
    {
        if (_pool.TryTake(out var session))
        {
            _logger.Debug(() => $"Rent User Session Pool Size:{_pool.Count}");
            return session;
        }

        throw new InvalidOperationException("Over UserSession Pool");
    }

    public void Return(UserSession session)
    {
        _pool.Add(session);

        _logger.Debug(() => $"Return User Session Pool Size:{_pool.Count}");
    }

    public int Count => _pool.Count;
}

