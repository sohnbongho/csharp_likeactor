using Library.ContInfo;
using Library.Network;
using Library.ObjectPool;
using Library.World;
using System.Net.Sockets;

namespace DummyClient.Session;

public class UserObjectPoolManager
{
    private readonly IObjectPool<UserSession> _userSessionPool;
    private readonly LobbyThreadManager _lobbyThreadManager;

    private int _loginServerCount;
    private int _gameServerCount;
    private int _disconnectedCount;

    public int LoginServerCount  => Volatile.Read(ref _loginServerCount);
    public int GameServerCount   => Volatile.Read(ref _gameServerCount);
    public int DisconnectedCount => Volatile.Read(ref _disconnectedCount);

    public UserObjectPoolManager(LobbyThreadManager lobbyThreadManager)
    {
        _userSessionPool = new ObjectPool<UserSession>(SessionConstInfo.MaxUserSessionPoolSize);
        _lobbyThreadManager = lobbyThreadManager;
    }

    public void Init()
    {
        _userSessionPool.Init(() => UserSession.Of(new TcpClient(), SessionIdGenerator.Generate(), this));
    }

    public UserSession RentUser()
    {
        var session = _userSessionPool.Rent();
        _lobbyThreadManager.Add(session);
        return session;
    }

    public void RemoveUser(UserSession userSession)
    {
        _lobbyThreadManager.Remove(userSession);
        _userSessionPool.Return(userSession);
    }

    internal void OnLoginServerConnected()  => Interlocked.Increment(ref _loginServerCount);

    internal void OnMovedToGameServer()
    {
        Interlocked.Decrement(ref _loginServerCount);
        Interlocked.Increment(ref _gameServerCount);
    }

    internal void OnDisconnected(SessionPhase phase)
    {
        if (phase == SessionPhase.GameServer)
            Interlocked.Decrement(ref _gameServerCount);
        else
            Interlocked.Decrement(ref _loginServerCount);
        Interlocked.Increment(ref _disconnectedCount);
    }
}
