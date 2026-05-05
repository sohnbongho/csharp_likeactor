using DummyClient.Session;
using Library.Logger;
using Library.World;
using System.Collections.Concurrent;

namespace DummyClient;

public class TcpDummyClient
{
    private static readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();

    private readonly UserObjectPoolManager _userObjectPoolManager;
    private readonly LobbyThreadManager _lobbyThreadManager;
    private readonly ManualResetEvent _shutdownEvent = new(false);
    private readonly string _serverIp;
    private readonly int _serverPort;
    private readonly int _maxClientCount;
    private readonly ConcurrentQueue<UserSession> _connectedUsers = new();
    private UserSession? _playerSession;

    public TcpDummyClient(string serverIp, int serverPort, int maxClientCount)
    {
        _serverIp = serverIp;
        _serverPort = serverPort;
        _maxClientCount = maxClientCount;
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
                userSession.Run(i + 1);
                await userSession.ConnectAsync(_serverIp, _serverPort);
                _connectedUsers.Enqueue(userSession);

                if ((i + 1) % batchSize == 0 || i + 1 == _maxClientCount)
                {
                    _logger.Info(() => $"[동접] {_connectedUsers.Count}/{_maxClientCount}");
                    if (i + 1 < _maxClientCount)
                        await Task.Delay(1000);
                }
            }

            if (_connectedUsers.TryPeek(out _playerSession))
            {
                _logger.Info(() => $"[키보드] 플레이어: {_playerSession.UserId} | W/A/S/D 또는 방향키=이동, Q=종료");
                _ = StartKeyboardInputLoopAsync(_playerSession);
            }

            _shutdownEvent.WaitOne();
        }
        catch (Exception ex)
        {
            _logger.Error(() => $"[오류] ", ex);
        }
    }

    private async Task StartKeyboardInputLoopAsync(UserSession playerSession)
    {
        while (true)
        {
            try
            {
                if (!Console.KeyAvailable)
                {
                    await Task.Delay(30);
                    continue;
                }

                var key = Console.ReadKey(intercept: true).Key;
                float dx = 0f, dy = 0f;

                switch (key)
                {
                    case ConsoleKey.W: case ConsoleKey.UpArrow:    dy -= 1f; break;
                    case ConsoleKey.S: case ConsoleKey.DownArrow:  dy += 1f; break;
                    case ConsoleKey.A: case ConsoleKey.LeftArrow:  dx -= 1f; break;
                    case ConsoleKey.D: case ConsoleKey.RightArrow: dx += 1f; break;
                    case ConsoleKey.Q: Stop(); return;
                    default: continue;
                }

                playerSession.SendMove(dx, dy);
            }
            catch (Exception ex)
            {
                _logger.Error(() => "[키보드] 입력 오류", ex);
            }
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
