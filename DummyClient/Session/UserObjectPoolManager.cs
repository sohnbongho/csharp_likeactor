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
}
