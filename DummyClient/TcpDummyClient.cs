using Library.Logger;
using System.Net.Sockets;
using System.Text;

namespace DummyClient;

public class TcpDummyClient
{
    private const string ServerIp = "127.0.0.1";
    private const int ServerPort = 9000;
    private readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();

    public async Task StartAsync()
    {
        _logger.Debug("[DummyClient] 서버에 연결 시도 중...");

        try
        {
            using TcpClient client = new TcpClient();
            await client.ConnectAsync(ServerIp, ServerPort);

            _logger.Debug("[DummyClient] 연결 성공!");

            using NetworkStream stream = client.GetStream();

            int counter = 0;
            while (true)
            {
                string message = $"Hello from dummy client #{++counter}";
                byte[] sendBytes = Encoding.UTF8.GetBytes(message);

                await stream.WriteAsync(sendBytes, 0, sendBytes.Length);
                _logger.Debug($"[송신] {message}");

                byte[] recvBuffer = new byte[4096];
                int bytesRead = await stream.ReadAsync(recvBuffer, 0, recvBuffer.Length);
                string response = Encoding.UTF8.GetString(recvBuffer, 0, bytesRead);
                _logger.Debug($"[수신] {response}");

                await Task.Delay(1000); // 1초 주기
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"[오류] {ex.Message}");
        }
    }
}
