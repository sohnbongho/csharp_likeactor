using Library.Logger;
using Messages;
using Server.Session.Pool;
using Server.Session.User.Network;
using System.IO;
using System.Net.Sockets;

namespace Server.Session.User;

public class UserSession : IDisposable
{
    private readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();
    private TcpClient? _client;
    private NetworkStream? _stream;    
    private ReceiverHandler? _receiver;

    public static UserSession Of()
    {
        return new UserSession();
    }

    private UserSession()
    {        
        _receiver = new ReceiverHandler(OnHandle, OnHandleAsync);
    }
    public void Bind(TcpClient client)
    {
        _client = client;
        _stream = _client.GetStream();        
    }

    public void Dispose()
    {
        if (_receiver != null)
        {
            _receiver.Dispose();
            _receiver = null;
        }
        if(_stream != null)
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

    public async Task RunAsync()
    {
        if (_client == null)
            return;

        if (_receiver == null)
            return;

        if (_stream == null)
            return;

        try
        {
            while (true)
            {
                var succeed = await _receiver.OnReceiveAsync(_stream);
                if (succeed == false)
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(() => $"[오류] 클라이언트 처리 중 예외 발생 ", ex);
        }
        finally
        {
            Dispose();
        }
    }
    public void OnHandle(MessageWrapper messageWrapper)
    {

    }

    public Task<bool> OnHandleAsync(MessageWrapper messageWrapper)
    {
        return Task.FromResult<bool>(true);
    }


}
