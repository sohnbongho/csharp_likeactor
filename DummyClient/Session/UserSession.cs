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

public enum SessionPhase { LoginServer, GameServer }

public class UserSession : IDisposable, IMessageQueueReceiver, ISessionUsable, ITickable
{
    private TcpClient _client;
    private static readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();
    private SenderHandler _sender;
    private ReceiverHandler _receiver;
    private readonly Channel<IMessageQueue> _messageChannel;
    private readonly ulong _sessionId;
    private readonly UserObjectPoolManager _userManager;
    private int _disposedFlag;
    private bool _isAuthenticated;
    private long _lastKeepAliveSentAt;

    public static string GameServerIp { get; set; } = "127.0.0.1";
    public static int GameServerPort { get; set; } = 9001;

    public ulong SessionId => _sessionId;
    public string UserId { get; private set; } = string.Empty;
    public byte[] ClientHashBytes { get; private set; } = Array.Empty<byte>();
    public float X { get; private set; }
    public float Y { get; private set; }
    public string AuthToken { get; private set; } = string.Empty;
    public SessionPhase Phase { get; private set; } = SessionPhase.LoginServer;

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
        _userManager.OnLoginServerConnected();
    }

    public async Task ConnectToGameServerAsync(string authToken)
    {
        AuthToken = authToken;
        Phase = SessionPhase.GameServer;
        _userManager.OnMovedToGameServer();

        // ReceiverHandler.OnReceiveCompleted 는 구소켓 오류 시 Disconnect() → Dispose() 를 호출한다.
        // Dispose() 가 새 TcpClient 를 건드리지 못하도록 flag를 먼저 1로 세팅한다.
        Interlocked.Exchange(ref _disposedFlag, 1);
        _receiver.Reset();   // 구소켓 명시적 종료 → IO 완료 콜백 즉시 큐잉
        _client.Dispose();
        // 1ms 대기: IOCP 스레드가 구 콜백을 처리하여 _disposedFlag=1 을 보고 bail-out 하도록 보장
        await Task.Delay(1);

        var newClient = new TcpClient();
        await newClient.ConnectAsync(GameServerIp, GameServerPort);

        _client = newClient;
        _sender = new SenderHandler();
        _receiver = new ReceiverHandler(this);
        var socket = _client.Client;
        _sender.Bind(socket);
        _receiver.Bind(socket);
        // StartReceive 전에 flag를 해제해야 GameServer가 즉시 보내는
        // ConnectedResponse가 EnqueueMessageAsync 체크에서 드롭되지 않는다.
        Interlocked.Exchange(ref _disposedFlag, 0);
        _receiver.StartReceive();
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

    public bool SendMove(float dx, float dy)
    {
        if (!_isAuthenticated) return false;
        X += dx;
        Y += dy;
        _logger.Debug(() => $"[이동] {UserId} ({X}, {Y})");
        return Send(new MessageWrapper { MoveRequest = new MoveRequest { X = X, Y = Y } });
    }

    public void Disconnect() => Dispose();

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposedFlag, 1, 0) != 0)
            return;
        _isAuthenticated = false;
        _client?.Dispose();
        _userManager.OnDisconnected(Phase);
    }
}
