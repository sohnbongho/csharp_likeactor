using Library.Logger;
using Library.Network;
using Messages;
using System.Net.Sockets;

namespace DummyClient.Session;

public class UserSession : IDisposable
{
    private readonly TcpClient _client;
    private NetworkStream _stream = null!;
    private int _counter = 0;
    private readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();
    private readonly SenderHandler _sender = new SenderHandler();
    private readonly ReceiverHandler _receiver = new ReceiverHandler();

    public UserSession(TcpClient client)
    {
        _client = client;        

        _receiver = new ReceiverHandler(OnRecvMessage);
        _sender = new SenderHandler();
    }

    public void Dispose()
    {
        _client?.Dispose();
        _stream?.Dispose();
    }
    public async Task ConnectAsync(string host, int port)
    {
        await _client.ConnectAsync(host, port);
        _stream = _client.GetStream();
    }

    public async Task<bool> StartEcho()
    {
        while (true)
        {
            var message = new MessageWrapper
            {
                KeepAliveRequest = new KeepAliveRequest { }
            };
            await _sender.SendAsync(_stream, message);
            _logger.Debug(() => $"[송신] KeepAliveRequest #{++_counter} 전송");

            var succeed = await _receiver.OnReceiveAsync(_stream);
            if (succeed == false)
                break;

            await Task.Delay(10000);
        }
        return true;
    }

    public void OnRecvMessage(MessageWrapper messageWrapper)
    {
        _logger.Debug(() => $"OnRecvMessage type:{messageWrapper.PayloadCase.ToString()}");
    }

}
