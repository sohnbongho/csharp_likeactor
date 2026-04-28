using Library.ContInfo;
using Library.Logger;
using Library.MessageQueue;
using Library.MessageQueue.Message;
using Library.Network;
using Library.Worker.Interface;
using Messages;
using System.Net.Sockets;
using System.Threading.Channels;

namespace DummyClient.Session;

public class UserSession : IDisposable, IMessageQueueReceiver, ISessionUsable, ITickable
{
    private readonly TcpClient _client;
    private static readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();
    private readonly SenderHandler _sender;
    private readonly ReceiverHandler _receiver;
    private readonly Channel<IMessageQueue> _messageChannel;
    private readonly ulong _sessionId;
    private readonly UserObjectPoolManager _userManager;
    private int _disposedFlag;

    public ulong SessionId => _sessionId;

    public static UserSession Of(TcpClient client, ulong sessionId, UserObjectPoolManager userObjectPoolManager)
    {
        return new UserSession(client, sessionId, userObjectPoolManager);
    }

    public UserSession(TcpClient client, ulong sessionId, UserObjectPoolManager userManager)
    {
        _client = client;
        _sessionId = sessionId;
        _userManager = userManager;

        _messageChannel = Channel.CreateBounded<IMessageQueue>(
            new BoundedChannelOptions(SessionConstInfo.MaxMessageChannelCapacity)
            {
                SingleReader = true,
                AllowSynchronousContinuations = false,
                FullMode = BoundedChannelFullMode.DropWrite
            });

        _receiver = new ReceiverHandler(this);
        _sender = new SenderHandler();
    }

    public async Task ConnectAsync(string host, int port)
    {
        await _client.ConnectAsync(host, port);

        var socket = _client.Client;
        _sender.Bind(socket);
        _receiver.Bind(socket);
        _receiver.StartReceive();
    }

    public void Run()
    {
        _sender.Send(new MessageWrapper
        {
            KeepAliveRequest = new KeepAliveRequest()
        });
    }

    public async ValueTask TickAsync()
    {
        if (Volatile.Read(ref _disposedFlag) != 0)
            return;

        int drained = 0;
        while (drained < SessionConstInfo.MaxMessagesPerTick && _messageChannel.Reader.TryRead(out var message))
        {
            await MessageQueueDispatcher.Instance.OnRecvMessageAsync(this, _sender, message);
            drained++;
        }
    }

    public ValueTask<bool> EnqueueMessageAsync(IMessageQueue message)
    {
        if (Volatile.Read(ref _disposedFlag) != 0)
            return new ValueTask<bool>(false);
        return new ValueTask<bool>(_messageChannel.Writer.TryWrite(message));
    }

    public bool Send(MessageWrapper message)
    {
        if (Volatile.Read(ref _disposedFlag) != 0)
            return false;
        return _sender.Send(message);
    }

    public void Disconnect() => Dispose();

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposedFlag, 1, 0) != 0)
            return;
        _client?.Dispose();
    }
}
