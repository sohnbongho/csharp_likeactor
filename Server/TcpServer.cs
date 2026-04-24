using Library.ContInfo;
using Library.Logger;
using Library.World;
using Server.Acceptor;
using Server.Actors;

namespace Server;

public class TcpServer
{
    private readonly int _port;
    private static readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();
    private readonly UserObjectPoolManager _userObjectPoolManager;
    private readonly LobbyThreadManager _lobbyThreadManager;
    private readonly WorldThreadManager _worldThreadManager;
    private readonly TCPAcceptor _acceptor;
    private readonly ManualResetEvent _shutdownEvent = new(false);

    public TcpServer(int port)
    {
        _port = port;
        _lobbyThreadManager = new LobbyThreadManager();
        _worldThreadManager = new WorldThreadManager();
        _userObjectPoolManager = new UserObjectPoolManager(_lobbyThreadManager, _worldThreadManager);
        _acceptor = new TCPAcceptor(port);
    }

    public void Init()
    {
        _lobbyThreadManager.Start();
        _worldThreadManager.Start();
        _userObjectPoolManager.Init();
        _acceptor.OnAccepted += socket =>
        {
            _logger.Debug(() => $"[접속] {socket.RemoteEndPoint} 수신됨");
            _userObjectPoolManager.AcceptUser(socket);
        };
        _acceptor.Init();
    }

    public void Start()
    {
        _logger.Info(() => $"Server Start Listen Port:{_port}...");
        _acceptor.Start();
        _shutdownEvent.WaitOne();
    }

    public void Stop()
    {
        _logger.Info(() => $"Stop Server");

        _acceptor.Dispose();
        _userObjectPoolManager.ShutdownAll();

        _lobbyThreadManager.StopAsync().GetAwaiter().GetResult();
        _worldThreadManager.StopAllAsync().GetAwaiter().GetResult();

        _shutdownEvent.Set();
    }
}
