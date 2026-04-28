using DummyClient.Session;
using Library.Logger;
using Library.World;
using System.Collections.Concurrent;

namespace DummyClient;

public class TcpDummyClient
{
    private const string ServerIp = "127.0.0.1";
    private const int ServerPort = 9000;
    private static readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();

    private readonly UserObjectPoolManager _userObjectPoolManager;
    private readonly LobbyThreadManager _lobbyThreadManager;
    private readonly ManualResetEvent _shutdownEvent = new(false);
    private readonly int _maxClientCount = 10000;
    private readonly ConcurrentQueue<UserSession> _connectedUsers = new();

    public TcpDummyClient()
    {
        _lobbyThreadManager = new LobbyThreadManager();
        _userObjectPoolManager = new UserObjectPoolManager(_lobbyThreadManager);
    }

    public void Init()
    {
        _lobbyThreadManager.Start();
        _userObjectPoolManager.Init();
    }

    public async Task StartAsync()
    {
        _logger.Info(() => $"[DummyClient] 접속 시작 (목표: {_maxClientCount}명)");

        try
        {
            const int batchSize = 1000;
            for (int i = 0; i < _maxClientCount; ++i)
            {
                var userSession = _userObjectPoolManager.RentUser();
                await userSession.ConnectAsync(ServerIp, ServerPort);
                _connectedUsers.Enqueue(userSession);
                userSession.Run();

                if ((i + 1) % batchSize == 0 || i + 1 == _maxClientCount)
                {
                    _logger.Info(() => $"[동접] {_connectedUsers.Count}/{_maxClientCount}");
                    if (i + 1 < _maxClientCount)
                        await Task.Delay(1000);
                }
            }

            _shutdownEvent.WaitOne();
        }
        catch (Exception ex)
        {
            _logger.Error(() => $"[오류] ", ex);
        }
    }

    public void Stop()
    {
        _logger.Info(() => $"Stop Server");
        for (int i = 0; i < _maxClientCount; ++i)
        {
            if (false == _connectedUsers.TryDequeue(out var userSession))
                break;


            userSession.Disconnect();
        }

        _lobbyThreadManager.StopAsync().GetAwaiter().GetResult();
        _shutdownEvent.Set();
    }
}
