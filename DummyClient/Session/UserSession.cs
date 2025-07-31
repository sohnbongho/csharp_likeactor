using Library.Logger;
using Library.MessageQueue;
using Library.MessageQueue.Message;
using Library.Network;
using Messages;
using System.Net.Sockets;

namespace DummyClient.Session;

public class UserSession : IDisposable, IMessageQueueReceiver
{
    private readonly TcpClient _client;
    private NetworkStream _stream = null!;
    private int _counter = 0;
    private readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();
    private readonly SenderHandler _sender;
    private readonly ReceiverHandler _receiver;
    private readonly MessageQueueWorker _messageQueueWorker;

    public UserSession(TcpClient client)
    {
        _client = client;
        _messageQueueWorker = new MessageQueueWorker();

        _receiver = new ReceiverHandler(this, _messageQueueWorker);
        _sender = new SenderHandler(this, _messageQueueWorker);
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
        _sender.SetStream(_stream);
    }

    public async Task<bool> StartEcho()
    {
        while (true)
        {
            var message = new MessageWrapper
            {
                KeepAliveRequest = new KeepAliveRequest { }
            };
            await _sender.AddQueueAsync(message);
            _logger.Debug(() => $"[송신] KeepAliveRequest #{++_counter} 전송");

            var succeed = await _receiver.OnReceiveAsync(_stream);
            if (succeed == false)
                break;

            await Task.Delay(500);
        }
        return true;
    }

    public void OnRecvMessage(MessageWrapper messageWrapper)
    {
        _logger.Debug(() => $"OnRecvMessage type:{messageWrapper.PayloadCase.ToString()}");
    }

    public async Task<bool> OnRecvMessageAsync(IMessageQueue message)
    {
        if (message is RemoteReceiveMessage receiveMessage)
        {            
        }
        else if (message is RemoteReceiveMessageAsync receiveMessageAsync)
        {            
        }
        else if (message is RemoteSendMessageAsync sendMessage)
        {
            if (_sender != null)
            {
                await _sender.SendAsync(sendMessage.Message);
            }
        }
        return true;
    }
}
