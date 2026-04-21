using Library.ContInfo;
using Library.Logger;
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
    private readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();
    private readonly IObjectPool<UserSession> _userSessionPool;
    private readonly ThreadPoolManager _threadPoolManager;
    private readonly MessageQueueWorkerManager _messageQueueWorkerManager;
    private readonly ConcurrentDictionary<ulong, UserSession> _activeSessions = new();
    private volatile bool _stopping;
    private readonly object _shutdownLock = new();

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
        // ShutdownAll과의 레이스 방지: _stopping 확인과 세션 추가를 원자적으로 처리
        lock (_shutdownLock)
        {
            if (_stopping)
            {
                socket.Close();
                return;
            }

            if (!_userSessionPool.TryRent(out var session) || session == null)
            {
                _logger.Warn(() => "세션 풀 고갈: 새 연결 거부");
                socket.Close();
                return;
            }

            session.Reinitialize(SessionIdGenerator.Generate(), _messageQueueWorkerManager);
            _activeSessions.TryAdd(session.SessionId, session);

            session.Bind(socket);
            _threadPoolManager.Add(session);
        }
    }
    public void RemoveUser(UserSession userSession)
    {
        _threadPoolManager.Remove(userSession);
        _activeSessions.TryRemove(userSession.SessionId, out _);
        _userSessionPool.Return(userSession);
    }

    public void ShutdownAll()
    {
        // _stopping 설정을 락 안에서 수행 → AcceptUser가 중간에 끼어들지 못하도록
        lock (_shutdownLock)
        {
            _stopping = true;
        }

        foreach (var kv in _activeSessions)
        {
            kv.Value.Disconnect();
        }
    }
}
