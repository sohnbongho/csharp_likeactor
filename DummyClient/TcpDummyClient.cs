using DummyClient.Session;
using Library.ContInfo;
using Library.Logger;
using Library.MessageQueue;
using Library.Worker;
using System.Collections.Concurrent;
using System.Net.Sockets;

namespace DummyClient;

public class TcpDummyClient
{
    private const string ServerIp = "127.0.0.1";
    private const int ServerPort = 9000;
    private readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();

    private readonly UserObjectPoolManager _userObjectPoolManager;
    private readonly ThreadPoolManager _threadPoolManager;
    private readonly MessageQueueWorkerManager _messageQueueWorkerManager;    
    private readonly ManualResetEvent _shutdownEvent = new(false);
    private readonly int _maxClientCount = 1;
    private readonly ConcurrentQueue<UserSession> _connectedUsers = new();

    public TcpDummyClient()
    {
        _threadPoolManager = new ThreadPoolManager(ThreadConstInfo.MaxUserThreadCount); // 4개 스레드
        _messageQueueWorkerManager = new MessageQueueWorkerManager(ThreadConstInfo.MaxMessageQueueWorkerCount); // 4개 스레드
        _userObjectPoolManager = new UserObjectPoolManager(_threadPoolManager, _messageQueueWorkerManager);
    }

    public void Init()
    {
        _messageQueueWorkerManager.Start();
        _threadPoolManager.Start();
        _userObjectPoolManager.Init();
    }


    public async Task StartAsync()
    {
        _logger.Debug(() => "[DummyClient] 서버에 연결 시도 중...");

        try
        {            
            for(int i = 0; i < _maxClientCount; ++i)
            {
                var userSession = _userObjectPoolManager.RentUser();
                await userSession.ConnectAsync(ServerIp, ServerPort);
                _connectedUsers.Enqueue(userSession);                

                userSession.Run();
                await Task.Delay(1000);
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
            
            _logger.Debug(() => $"[DummyClient] {i}");

            userSession.Disconnect();
            Task.Delay(1000).Wait();
        }

        _shutdownEvent.Set();
    }
}
