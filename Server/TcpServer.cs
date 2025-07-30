using Library.ContInfo;
using Library.Logger;
using Server.ServerWorker;
using Server.Session.UserPool;
using System.Net;
using System.Net.Sockets;

namespace Server;

public class TcpServer
{
    private readonly int _port;
    private TcpListener _listener = null!;
    private readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();
    private readonly ISessionPool _sessionPool = new UserSessionPool(SessionConstInfo.MaxUserSessionPoolSize); // 1만개 유저 풀 미리 만들어둠
    private readonly ThreadPoolManager _threadPoolManager = new ThreadPoolManager(ThreadConstInfo.MaxUserThreadCount); // 4개 스레드

    public TcpServer(int port)
    {
        _port = port;
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

            var session = _sessionPool.Rent();
            session.Bind(client);
            _threadPoolManager.Add(session);

            _ = session.RunAsync().ContinueWith(t =>
            {
                _threadPoolManager.Remove(session);
                _sessionPool.Return(session);
            }); // fire-and-forget
        }
    }
}

