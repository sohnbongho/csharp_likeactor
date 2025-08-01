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

        var socket = _client.Client;

        _sender.Bind(socket);

        _receiver.Bind(socket);
        _receiver.StartReceive();
        
    }

    public bool Start()
    {
        while (true)
        {
            var message = new MessageWrapper
            {
                KeepAliveRequest = new KeepAliveRequest { }
            };
            _sender.Send(message);
            _logger.Debug(() => $"[송신] KeepAliveRequest #{++_counter} 전송");

            Task.Delay(1000).Wait();
        }
        return true;
    }

    public void OnRecvMessage(MessageWrapper messageWrapper)
    {
        _logger.Debug(() => $"OnRecvMessage type:{messageWrapper.PayloadCase.ToString()}");
    }

    public Task<bool> OnRecvMessageAsync(IMessageQueue message)
    {
        if (message is RemoteReceiveMessage receiveMessage)
        {
            OnRecvMessage(receiveMessage.MessageWrapper);
        }
        else if (message is RemoteSendMessage sendMessage)
        {
            if (_sender != null)
            {
                _sender.Send(sendMessage.MessageWrapper);
            }
        }
        return Task.FromResult(true);
    }

    public async Task<bool> EnqueueMessageAsync(IMessageQueue message)
    {
        await _messageQueueWorker.EnqueueAsync(this, message);
        return true;
    }

    public void Disconnect()
    {
        Dispose();
    }
}
