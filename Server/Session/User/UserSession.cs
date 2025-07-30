using Server.Logger;
using System.Net.Sockets;
using System.Text;

namespace Server.Session.User;

public class UserSession
{
    private readonly TcpClient _client;
    private readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();

    public static UserSession Of(TcpClient client)
    {
        return new UserSession(client);
    }

    private UserSession(TcpClient client)
    {
        _client = client;
    }

    public async Task RunAsync()
    {
        using NetworkStream stream = _client.GetStream();
        byte[] buffer = new byte[4096];

        try
        {
            while (true)
            {
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0)
                {
                    _logger.Debug("[연결 종료] 클라이언트가 연결을 끊었습니다.");
                    break;
                }

                string receivedText = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                _logger.Debug($"[수신] {receivedText}");

                string response = $"서버 응답: {receivedText}";
                byte[] responseBytes = Encoding.UTF8.GetBytes(response);
                await stream.WriteAsync(responseBytes, 0, responseBytes.Length);
            }
        }
        catch (Exception ex)
        {
            _logger.Debug($"[오류] 클라이언트 처리 중 예외 발생: {ex.Message}");
        }
        finally
        {
            _client.Close();
        }
    }
}
