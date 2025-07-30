using Library.Logger;
using Library.Network;
using Messages;
using System.Net.Sockets;

namespace Server.Session.User;

public class UserSession : IDisposable
{
    private readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();
    private TcpClient? _client;
    private NetworkStream? _stream;
    private ReceiverHandler? _receiver;
    private SenderHandler? _sender;

    public static UserSession Of()
    {
        return new UserSession();
    }

    private UserSession()
    {
    }
    public void Bind(TcpClient client)
    {
        _receiver = new ReceiverHandler(OnRecvMessage, OnRecvMessageAsync);
        _sender = new SenderHandler();

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
        if (_sender != null)
        {
            _sender.Dispose();
            _sender = null;
        }
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
    public void OnRecvMessage(MessageWrapper messageWrapper)
    {
        _logger.Debug(() => $"OnRecvMessage type:{messageWrapper.PayloadCase.ToString()}");
        

    }

    public async Task<bool> OnRecvMessageAsync(MessageWrapper messageWrapper)
    {
        _logger.Debug(() => $"OnRecvMessageAsync type:{messageWrapper.PayloadCase.ToString()}");

        await SendAsync(messageWrapper.Clone());

        return true;
    }
    public async Task<bool> SendAsync(MessageWrapper message)
    {
        if (_sender == null)
            return false;
        
        if (_stream == null)
            return false;

        return await _sender.SendAsync(_stream, message);
    }

}
