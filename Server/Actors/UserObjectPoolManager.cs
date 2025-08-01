using Library.ContInfo;
using Library.MessageQueue;
using Library.ObjectPool;
using Server.Actors.User;
using Server.ServerWorker;
using System.Net.Sockets;

namespace Server.Actors;

public class UserObjectPoolManager
{
    private readonly IObjectPool<UserSession> _userSessionPool;
    private readonly ThreadPoolManager _threadPoolManager;
    private readonly MessageQueueWorkerManager _messageQueueWorkerManager;
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
        var session = _userSessionPool.Rent();        

        session.Bind(socket);
        _threadPoolManager.Add(session);
    }
    public void RemoveUser(UserSession userSession)
    {
        _threadPoolManager.Remove(userSession);
        _userSessionPool.Return(userSession);
    }
}
