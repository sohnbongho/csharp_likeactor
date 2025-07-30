using DummyClient.Session;
using Library.Logger;
using System.Net.Sockets;

namespace DummyClient;

public class TcpDummyClient
{
    private const string ServerIp = "127.0.0.1";
    private const int ServerPort = 9000;
    private readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();

    public async Task StartAsync()
    {
        _logger.Debug(() => "[DummyClient] 서버에 연결 시도 중...");

        try
        {
            using UserSession userSession = new UserSession(new TcpClient());
            await userSession.ConnectAsync(ServerIp, ServerPort);

            _logger.Debug(() => "[DummyClient] 연결 성공!");

            await userSession.StartEcho();

        }
        catch (Exception ex)
        {
            _logger.Error(() => $"[오류] ", ex);
        }
    }
}
