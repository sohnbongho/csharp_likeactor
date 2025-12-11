using Library.ContInfo;
using Library.MessageQueue;
using Library.Network;
using Library.ObjectPool;
using Library.Worker;
using Server.Actors.User;
using System.Collections.Concurrent;
using System.Net.Sockets;

namespace Server.Actors;

public class UserObjectPoolManager
{
    private readonly IObjectPool<UserSession> _userSessionPool;
    private readonly ThreadPoolManager _threadPoolManager;
    private readonly MessageQueueWorkerManager _messageQueueWorkerManager;
    private readonly ConcurrentDictionary<ulong, UserSession> _activeSessions = new();
    private volatile bool _stopping;
    public UserObjectPoolManager(ThreadPoolManager threadPoolManager, MessageQueueWorkerManager messageQueueWorkerManager)
    {
        _userSessionPool = new ObjectPool<UserSession>(SessionConstInfo.MaxUserSessionPoolSize);
        _threadPoolManager = threadPoolManager;
        _messageQueueWorkerManager = messageQueueWorkerManager;
    }
    public void Init()
    {
        _userSessionPool.Init(() => UserSession.Of(SessionIdGenerator.Generate(), this, _messageQueueWorkerManager));
    }
    public void AcceptUser(Socket socket)
    {
        if (_stopping)
            return;

        var session = _userSessionPool.Rent();
        _activeSessions.TryAdd(session.SessionId, session);

        session.Bind(socket);
        _threadPoolManager.Add(session);
    }
    public void RemoveUser(UserSession userSession)
    {
        _threadPoolManager.Remove(userSession);
        _activeSessions.TryRemove(userSession.SessionId, out _);
        _userSessionPool.Return(userSession);
    }

    public void ShutdownAll()
    {
        _stopping = true;
        foreach (var kv in _activeSessions)
        {
            kv.Value.Disconnect();
        }
        _activeSessions.Clear();
    }
}
