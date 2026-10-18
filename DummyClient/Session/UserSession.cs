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
    private UserConnectionComponent? _connection;
    private static readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();
    private readonly SenderHandler _sender;
    private readonly ReceiverHandler _receiver;
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
    public bool IsAuthenticated => _isAuthenticated;

    // ── 협동 테스트 모드(--coop) 상태. 핸들러(세션 스레드)와 CoopDirector(별도 태스크)가 함께 읽으므로 volatile.
    public volatile string RoomCode = string.Empty;
    public volatile bool InRoom;
    public volatile bool IsHost;
    public volatile bool InBattle;
    public volatile int RoomError = -1;      // RoomCreate/RoomJoin 응답 error_code (-1: 응답 전)
    public volatile int StartError = -1;     // RoomStartResponse error_code (-1: 응답 전)
    public volatile bool BattleEnded;
    public int MySlot;
    public int SnapshotCount;
    public int EventCount;
    public int LastSnapshotTick;
    public int LastEnemyInView;
    public int LastLevel;
    public int LastHp;
    public int LastKills;
    public int ChoiceCount;
    public long SnapshotBytes;
    private float _walkX, _walkY;
    private float _walkRemaining;
    private int _inputSeq;
    private static readonly Random _walkRandom = new();

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

        _receiver = new ReceiverHandler(this, Disconnect);
        _sender = new SenderHandler(Disconnect);
    }

    public async Task ConnectAsync(string host, int port)
    {
        await _client.ConnectAsync(host, port);
        BindConnection(_client.Client);
    }

    private void BindConnection(Socket socket)
    {
        // 이전 연결 참조는 덮어쓴다. 구 소켓의 stale 콜백은 핸들러의 ReferenceEquals 가드가 차단한다.
        _connection = new UserConnectionComponent(socket);
        _sender.Bind(_connection);
        _receiver.Bind(_connection);
        // StartReceive 전에 flag를 해제해야 서버가 즉시 보내는
        // 응답이 EnqueueMessageAsync 체크에서 드롭되지 않는다.
        Interlocked.Exchange(ref _disposedFlag, 0);
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

        // 구 소켓을 닫으면 핸들러가 Disconnect() → Dispose() 를 호출한다.
        // 이것이 새 TcpClient를 건드리지 못하도록 flag를 먼저 1로 세팅해 Dispose를 no-op으로 만든다.
        Interlocked.Exchange(ref _disposedFlag, 1);

        // 로그인 서버 소켓은 Abort(RST)로 닫는다. Close(FIN)로 닫으면 클라이언트 측 임시 포트가
        // TIME_WAIT(2분)에 묶여, 1만 명 × (로그인 + 게임) 연결이 Windows 동적 포트 범위를 넘겨
        // 게임 서버 connect가 AddressAlreadyInUse로 실패한다.
        _connection?.Abort();
        _sender.Reset();
        _client.Dispose();

        var newClient = new TcpClient();
        try
        {
            await newClient.ConnectAsync(GameServerIp, GameServerPort);
        }
        catch (Exception ex)
        {
            // flag가 이미 1이라 Dispose()는 no-op이므로 여기서 직접 종료 집계한다.
            _logger.Warn(() => $"[게임서버 접속 실패] {UserId}: {ex.Message}");
            newClient.Dispose();
            _isAuthenticated = false;
            _userManager.OnDisconnected(Phase);
            return;
        }

        _client = newClient;
        BindConnection(_client.Client);
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

            if (InBattle)
                SendBattleInput(ThreadConstInfo.TickMillSecond / 1000f);
        }
    }

    // 전투 중 틱마다 입력 전송: 1~3초마다 방향을 바꾸는 무작위 걷기
    private void SendBattleInput(float dt)
    {
        _walkRemaining -= dt;
        if (_walkRemaining <= 0f)
        {
            lock (_walkRandom)
            {
                _walkRemaining = 1f + (float)_walkRandom.NextDouble() * 2f;
                if (_walkRandom.NextDouble() < 0.2)
                {
                    _walkX = 0f; _walkY = 0f;
                }
                else
                {
                    double a = _walkRandom.NextDouble() * Math.PI * 2;
                    _walkX = (float)Math.Cos(a); _walkY = (float)Math.Sin(a);
                }
            }
        }
        Send(new MessageWrapper
        {
            BattleInputRequest = new BattleInputRequest { Seq = ++_inputSeq, MoveX = _walkX, MoveY = _walkY, Skill = false }
        });
    }

    public void ResetCoopState()
    {
        RoomCode = string.Empty;
        InRoom = false;
        IsHost = false;
        InBattle = false;
        RoomError = -1;
        StartError = -1;
        BattleEnded = false;
        MySlot = -1;
        SnapshotCount = 0;
        EventCount = 0;
        LastSnapshotTick = 0;
        LastEnemyInView = 0;
        LastLevel = 0;
        LastHp = 0;
        LastKills = 0;
        ChoiceCount = 0;
        SnapshotBytes = 0;
        _walkRemaining = 0f;
        _inputSeq = 0;
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
        _connection?.Close();
        _sender.Reset();
        _client?.Dispose();
        _userManager.OnDisconnected(Phase);
    }
}
