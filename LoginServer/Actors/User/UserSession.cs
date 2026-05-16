using Library.ContInfo;
using Library.Db.Cache;
using Library.Db.Sql;
using Library.Logger;
using Library.MessageQueue;
using Library.MessageQueue.Message;
using Library.Model;
using Library.Network;
using Library.Timer;
using Library.Worker.Interface;
using Messages;
using LoginServer.Actors.User.DbRequest.Sql;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading.Channels;
using StackExchange.Redis;

namespace LoginServer.Actors.User;

public class UserSession : IDisposable, ITickable, IMessageQueueReceiver, ISessionUsable
{
    public ulong SessionId => _sessionId;

    public UserAccountData? AccountData { get; private set; }
    public bool IsAuthenticated => AccountData != null;

    private static readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();

    private readonly ReceiverHandler _receiver;
    private readonly SenderHandler _sender;
    private readonly TimerScheduleManager _timerScheduleManager;
    private readonly Channel<IMessageQueue> _messageChannel;
    private readonly UserObjectPoolManager _userManager;
    private readonly SqlWorkerManager _sqlWorkerManager;
    private readonly IDatabase _redisDb;
    private readonly string _authTokenPrefix;
    private readonly int _authTokenTtlSeconds;

    private UserConnectionComponent? _userConnection;
    private ulong _sessionId;
    private int _disposedFlag;
    private long _lastKeepAliveReceivedAt;

    private int _loginAttemptCount;
    private long _loginWindowStart;
    private const int LoginAttemptsPerMinute = 5;

    public static UserSession Of(ulong sessionId, UserObjectPoolManager userManager, SqlWorkerManager sqlWorkerManager, IDatabase redisDb, string authTokenPrefix, int authTokenTtlSeconds)
    {
        return new UserSession(sessionId, userManager, sqlWorkerManager, redisDb, authTokenPrefix, authTokenTtlSeconds);
    }

    private UserSession(ulong sessionId, UserObjectPoolManager userManager, SqlWorkerManager sqlWorkerManager, IDatabase redisDb, string authTokenPrefix, int authTokenTtlSeconds)
    {
        _userManager = userManager;
        _sqlWorkerManager = sqlWorkerManager;
        _redisDb = redisDb;
        _authTokenPrefix = authTokenPrefix;
        _authTokenTtlSeconds = authTokenTtlSeconds;
        _sessionId = sessionId;

        _messageChannel = Channel.CreateBounded<IMessageQueue>(
            new BoundedChannelOptions(SessionConstInfo.MaxMessageChannelCapacity)
            {
                SingleReader = true,
                AllowSynchronousContinuations = false,
                FullMode = BoundedChannelFullMode.DropWrite
            });

        _timerScheduleManager = new TimerScheduleManager();
        _receiver = new ReceiverHandler(this);
        _sender = new SenderHandler();
    }

    public void Reinitialize(ulong sessionId)
    {
        _sessionId = sessionId;
        AccountData = null;
        _loginAttemptCount = 0;
        _loginWindowStart = 0;
        _lastKeepAliveReceivedAt = 0;

        while (_messageChannel.Reader.TryRead(out var msg))
        {
            if (msg is RemoteReceiveMessage rem) RemoteReceiveMessage.Return(rem);
        }

        _timerScheduleManager.Reset();
        _receiver.Reset();
        _sender.Reset();
        Volatile.Write(ref _disposedFlag, 0);
    }

    public void Bind(Socket socket)
    {
        Volatile.Write(ref _disposedFlag, 0);
        _userConnection = new UserConnectionComponent(socket);
        _sender.Bind(_userConnection.Socket, Disconnect);
        _receiver.Bind(_userConnection.Socket);
        _receiver.StartReceive();
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposedFlag, 1, 0) != 0)
            return;

        if (_userConnection != null)
        {
            _userConnection.Dispose();
            _userConnection = null;
        }

        _timerScheduleManager.Reset();
        _receiver.Reset();
        _sender.Reset();

        _userManager.RemoveUser(this);
    }

    public async ValueTask TickAsync()
    {
        if (Volatile.Read(ref _disposedFlag) != 0)
            return;

        int drained = 0;
        while (drained < SessionConstInfo.MaxMessagesPerTick && _messageChannel.Reader.TryRead(out var message))
        {
            if (IsBlockedBeforeAuth(message))
                return;

            await MessageQueueDispatcher.Instance.OnRecvMessageAsync(this, _sender, message);
            drained++;
        }

        _timerScheduleManager.Tick();

        if (IsAuthenticated)
        {
            var elapsed = (Stopwatch.GetTimestamp() - _lastKeepAliveReceivedAt) / (double)Stopwatch.Frequency;
            if (elapsed > SessionConstInfo.KeepAliveTimeoutSeconds)
            {
                _logger.Warn(() => $"KeepAlive 타임아웃 세션 종료: {_sessionId}");
                Disconnect();
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

    public void Disconnect() => Dispose();

    internal void OnAuthenticated(UserAccountData data)
    {
        AccountData = data;
        _lastKeepAliveReceivedAt = Stopwatch.GetTimestamp();
        _userManager.RegisterAuthenticatedSession(data.UserId, this);
    }

    internal void UpdateKeepAlive()
    {
        _lastKeepAliveReceivedAt = Stopwatch.GetTimestamp();
    }

    internal bool EnqueueSqlRequest(ISqlRequest request) => _sqlWorkerManager.Enqueue(request);

    internal async Task<string?> TryIssueAuthToken(ulong accountId, string userId)
    {
        var token = Guid.NewGuid().ToString("N");
        var key = $"{_authTokenPrefix}{token}";
        var value = $"{accountId}:{userId}";
        var set = await _redisDb.StringSetAsync(key, value, TimeSpan.FromSeconds(_authTokenTtlSeconds));
        return set ? token : null;
    }

    private bool IsBlockedBeforeAuth(IMessageQueue message)
    {
        if (IsAuthenticated || message is not RemoteReceiveMessage remote)
            return false;

        var payloadCase = remote.MessageWrapper.PayloadCase;
        if (payloadCase == MessageWrapper.PayloadOneofCase.LoginRequest ||
            payloadCase == MessageWrapper.PayloadOneofCase.KeepAliveRequest)
            return false;

        RemoteReceiveMessage.Return(remote);
        Disconnect();
        return true;
    }

    public bool TryConsumeLoginAttempt()
    {
        var now = Environment.TickCount64;
        if (now - _loginWindowStart >= 60_000)
        {
            _loginWindowStart = now;
            _loginAttemptCount = 0;
        }
        return ++_loginAttemptCount <= LoginAttemptsPerMinute;
    }
}
