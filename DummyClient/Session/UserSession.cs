using Library.ContInfo;
using Library.Logger;
using Library.MessageQueue;
using Library.MessageQueue.Message;
using Library.Network;
using Library.Security;
using Library.Worker.Interface;
using Messages;
using System.Diagnostics;
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
    private bool _isAuthenticated;
    private long _lastKeepAliveSentAt;

    public ulong SessionId => _sessionId;
    public string UserId { get; private set; } = string.Empty;
    public byte[] ClientHashBytes { get; private set; } = Array.Empty<byte>();

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

    public void Run(int accountIndex)
    {
        UserId = $"user_{accountIndex:D5}";
        ClientHashBytes = PasswordHashHelper.ComputeClientHash("Test1234!");
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

        if (_isAuthenticated)
        {
            var now = Stopwatch.GetTimestamp();
            var elapsed = (now - _lastKeepAliveSentAt) / (double)Stopwatch.Frequency;
            if (elapsed >= SessionConstInfo.KeepAliveIntervalSeconds)
            {
                _lastKeepAliveSentAt = now;
                Send(new MessageWrapper { KeepAliveRequest = new KeepAliveRequest() });
            }
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

    public void OnAuthenticated()
    {
        _isAuthenticated = true;
        _lastKeepAliveSentAt = Stopwatch.GetTimestamp();
    }

    public void Disconnect() => Dispose();

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposedFlag, 1, 0) != 0)
            return;
        _isAuthenticated = false;
        _client?.Dispose();
    }
}
