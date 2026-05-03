using Library.ContInfo;
using Library.Db.Sql;
using Library.Logger;
using Library.Network;
using Library.ObjectPool;
using Library.World;
using Server.Actors.User;
using System.Collections.Concurrent;
using System.Net.Sockets;

namespace Server.Actors;

public class UserObjectPoolManager
{
    private static readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();
    private readonly IObjectPool<UserSession> _userSessionPool;
    private readonly LobbyThreadManager _lobbyThreadManager;
    private readonly WorldThreadManager _worldThreadManager;
    private readonly SqlWorkerManager _sqlWorkerManager;
    private readonly ConcurrentDictionary<ulong, UserSession> _activeSessions = new();
    private readonly ConcurrentDictionary<string, UserSession> _authenticatedSessions = new();
    private volatile bool _stopping;
    private readonly object _shutdownLock = new();

    public int ActiveSessionCount => _activeSessions.Count;

    public UserObjectPoolManager(LobbyThreadManager lobbyThreadManager, WorldThreadManager worldThreadManager, SqlWorkerManager sqlWorkerManager)
    {
        _userSessionPool = new ObjectPool<UserSession>(SessionConstInfo.MaxUserSessionPoolSize);
        _lobbyThreadManager = lobbyThreadManager;
        _worldThreadManager = worldThreadManager;
        _sqlWorkerManager = sqlWorkerManager;
    }

    public void Init()
    {
        _userSessionPool.Init(() => UserSession.Of(0, this, _sqlWorkerManager));
    }

    public void AcceptUser(Socket socket)
    {
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

            session.Reinitialize(SessionIdGenerator.Generate());
            _activeSessions.TryAdd(session.SessionId, session);
            session.Bind(socket);
            session.Send(new Messages.MessageWrapper { ConnectedResponse = new Messages.ConnectedResponse { Index = 0 } });
            _lobbyThreadManager.Add(session);
        }
    }

    public void RemoveUser(UserSession session)
    {
        if (session.WorldId == 0)
            _lobbyThreadManager.Remove(session);
        else
            _worldThreadManager.Remove(session, session.WorldId);

        _activeSessions.TryRemove(session.SessionId, out _);
        _userSessionPool.Return(session);
    }

    public void RegisterAuthenticatedSession(string userId, UserSession newSession)
    {
        _authenticatedSessions.TryGetValue(userId, out var old);
        _authenticatedSessions[userId] = newSession;
        if (old != null && !ReferenceEquals(old, newSession))
            old.Disconnect();
    }

    public void UnregisterAuthenticatedSession(string userId, UserSession session)
    {
        if (_authenticatedSessions.TryGetValue(userId, out var current) && ReferenceEquals(current, session))
            _authenticatedSessions.TryRemove(userId, out _);
    }

    // 반드시 세션의 현재 tick 스레드 내에서 호출할 것 (월드 이동 시 핸들러에서 호출).
    public void MoveToWorld(UserSession session, ulong worldId)
    {
        if (session.WorldId == 0)
            _lobbyThreadManager.Remove(session);
        else
            _worldThreadManager.Remove(session, session.WorldId);

        session.SetWorldId(worldId);

        if (worldId == 0)
            _lobbyThreadManager.Add(session);
        else
            _worldThreadManager.Add(session, worldId);
    }

    public void BroadcastAll(Messages.MessageWrapper message)
    {
        foreach (var kv in _activeSessions)
            kv.Value.Send(message);
    }

    public IEnumerable<UserSession> EnumerateSessions()
    {
        foreach (var kv in _activeSessions)
            yield return kv.Value;
    }

    public bool TryDisconnect(ulong sessionId)
    {
        if (!_activeSessions.TryGetValue(sessionId, out var session))
            return false;
        session.Disconnect();
        return true;
    }

    public void ShutdownAll()
    {
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
