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
