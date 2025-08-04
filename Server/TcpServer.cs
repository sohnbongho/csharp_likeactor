using Library.ContInfo;
using Library.Logger;
using Library.MessageQueue;
using Library.Worker;
using Server.Acceptor;
using Server.Actors;

namespace Server;

public class TcpServer
{
    private readonly int _port;
    private readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();
    private readonly UserObjectPoolManager _userObjectPoolManager;
    private readonly ThreadPoolManager _threadPoolManager;
    private readonly MessageQueueWorkerManager _messageQueueWorkerManager;
    private readonly TCPAcceptor _acceptor;
    private readonly ManualResetEvent _shutdownEvent = new(false);

    public TcpServer(int port)
    {
        _port = port;
        _threadPoolManager = new ThreadPoolManager(ThreadConstInfo.MaxUserThreadCount); // 4개 스레드
        _messageQueueWorkerManager = new MessageQueueWorkerManager(ThreadConstInfo.MaxMessageQueueWorkerCount); // 4개 스레드
        _userObjectPoolManager = new UserObjectPoolManager(_threadPoolManager, _messageQueueWorkerManager);
        _acceptor = new TCPAcceptor(port);
    }
    public void Init()
    {
        _messageQueueWorkerManager.Start();
        _threadPoolManager.Start();
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

        _acceptor.Stop();
        _shutdownEvent.Set();
    }

}

