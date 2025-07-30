using System.Net.Sockets;

namespace Library.Network;

public class UserConnectionSystem : IDisposable
{
    public NetworkStream? Stream => _stream;
    private TcpClient? _client;
    private NetworkStream? _stream;

    public UserConnectionSystem(TcpClient client)
    {
        _client = client;
        _stream = _client.GetStream();
    }

    public void Dispose()
    {
        if (_stream != null)
        {
            _stream.Dispose();
            _stream = null;
        }
        if (_client != null)
        {
            _client.Close();
            _client.Dispose();
            _client = null;
        }
    }
}
