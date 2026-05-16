using Library.ContInfo;
using Library.Db.Cache;
using Library.Db.Sql;
using Library.Logger;
using Library.Network;
using Library.ObjectPool;
using Library.World;
using LoginServer.Actors.User;
using StackExchange.Redis;
using System.Collections.Concurrent;
using System.Net.Sockets;

namespace LoginServer.Actors;

public class UserObjectPoolManager
{
    private static readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();
    private readonly IObjectPool<UserSession> _userSessionPool;
    private readonly LobbyThreadManager _lobbyThreadManager;
    private readonly SqlWorkerManager _sqlWorkerManager;
    private readonly IDatabase _redisDb;
    private readonly string _authTokenPrefix;
    private readonly int _authTokenTtlSeconds;
    private readonly ConcurrentDictionary<ulong, UserSession> _activeSessions = new();
    private readonly ConcurrentDictionary<string, UserSession> _authenticatedSessions = new();
    private volatile bool _stopping;
    private readonly object _shutdownLock = new();

    public int ActiveSessionCount => _activeSessions.Count;

    public UserObjectPoolManager(LobbyThreadManager lobbyThreadManager, SqlWorkerManager sqlWorkerManager, IDatabase redisDb, string authTokenPrefix, int authTokenTtlSeconds)
    {
        _userSessionPool = new ObjectPool<UserSession>(SessionConstInfo.MaxUserSessionPoolSize);
        _lobbyThreadManager = lobbyThreadManager;
        _sqlWorkerManager = sqlWorkerManager;
        _redisDb = redisDb;
        _authTokenPrefix = authTokenPrefix;
        _authTokenTtlSeconds = authTokenTtlSeconds;
    }

    public void Init()
    {
        _userSessionPool.Init(() => UserSession.Of(0, this, _sqlWorkerManager, _redisDb, _authTokenPrefix, _authTokenTtlSeconds));
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
        _lobbyThreadManager.Remove(session);
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
