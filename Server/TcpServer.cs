using Library.Logger;
using Server.Session.User;
using System.Net;
using System.Net.Sockets;

namespace Server;

public class TcpServer
{
    private readonly int _port;
    private TcpListener _listener = null!;
    private readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();

    public TcpServer(int port)
    {
        _port = port;
    }

    public async Task StartAsync()
    {
        _listener = new TcpListener(IPAddress.Any, _port);
        _listener.Start();
        _logger.Info($"[서버 시작] 포트 {_port} 에서 연결 대기 중...");

        while (true)
        {
            var client = await _listener.AcceptTcpClientAsync();
            _logger.Debug($"[접속] 클라이언트 연결됨");

            var session = UserSession.Of(client);
            _ = session.RunAsync(); // fire-and-forget
        }
    }
}

