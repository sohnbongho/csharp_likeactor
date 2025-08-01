using Library.ContInfo;
using Library.Logger;
using Library.ObjectPool;
using Server.ServerWorker;
using Server.Actors.User;
using System.Net;
using System.Net.Sockets;
using Library.MessageQueue;
using Server.Actors;

namespace Server;

public class TcpServer
{
    private readonly int _port;
    private TcpListener _listener = null!;
    private readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();
    private readonly UserObjectPoolManager _userObjectPoolManager;
    private readonly ThreadPoolManager _threadPoolManager;
    private readonly MessageQueueWorkerManager _messageQueueWorkerManager;

    public TcpServer(int port)
    {
        _port = port;
        _threadPoolManager = new ThreadPoolManager(ThreadConstInfo.MaxUserThreadCount); // 4개 스레드
        _messageQueueWorkerManager = new MessageQueueWorkerManager(ThreadConstInfo.MaxMessageQueueWorkerCount); // 4개 스레드
        _userObjectPoolManager = new UserObjectPoolManager(_threadPoolManager, _messageQueueWorkerManager);
    }
    public void Init()
    {
        _threadPoolManager.Start();
        _userObjectPoolManager.Init();
    }

    public async Task StartAsync()
    {
        _listener = new TcpListener(IPAddress.Any, _port);
        _listener.Start();

        _logger.Info(() => $"[서버 시작] 포트 {_port} 에서 연결 대기 중...");

        while (true)
        {
            var client = await _listener.AcceptTcpClientAsync();
            _logger.Debug(() => $"[접속] 클라이언트 연결됨");

            var socket = client.Client;
            _userObjectPoolManager.AcceptUser(socket);
        }
    }

}

