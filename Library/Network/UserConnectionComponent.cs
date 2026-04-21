using System.Net.Sockets;

namespace Library.Network;

public class UserConnectionComponent : IDisposable
{
    public Socket? Socket => _socket;

    private Socket? _socket;

    public UserConnectionComponent(Socket socket)
    {
        _socket = socket;
        _socket.NoDelay = true;

        // 30초 무활동 시 OS가 keepalive 프로브 전송, 3회 실패 시 연결 강제 종료
        _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        _socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 30);
        _socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 5);
        _socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 3);
    }

    public void Dispose()
    {
        if (_socket != null)
        {
            try
            {
                _socket.Shutdown(SocketShutdown.Both);
            }
            catch
            {
                // 이미 닫혔을 경우 무시
            }

            _socket.Close();
            _socket.Dispose();
            _socket = null;
        }
    }
}
