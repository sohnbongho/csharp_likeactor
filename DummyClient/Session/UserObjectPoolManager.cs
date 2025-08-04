using Library.ContInfo;
using Library.MessageQueue;
using Library.Network;
using Library.ObjectPool;
using Library.Worker;
using System.Net.Sockets;

namespace DummyClient.Session;

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
        _userSessionPool.Init(() => UserSession.Of(
            new TcpClient(),
            SessionIdGenerator.Generate(), 
            this, 
            _messageQueueWorkerManager));
    }
    public UserSession RentUser()
    {
        var session = _userSessionPool.Rent();        
        _threadPoolManager.Add(session);

        return session;
    }
    public void RemoveUser(UserSession userSession)
    {
        _threadPoolManager.Remove(userSession);
        _userSessionPool.Return(userSession);
    }
}
