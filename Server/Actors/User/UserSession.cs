using Library.ContInfo;
using Library.Db.Sql;
using Library.Logger;
using Library.MessageQueue;
using Library.MessageQueue.Message;
using Library.Network;
using Library.Timer;
using Library.Worker.Interface;
using Messages;
using Server.Actors.User.DbRequest.Sql;
using Server.Actors.User.Model;
using System.Net.Sockets;
using System.Threading.Channels;

namespace Server.Actors.User;

public class UserSession : IDisposable, ITickable, IMessageQueueReceiver, ISessionUsable
{
    public ulong SessionId => _sessionId;
    public ulong WorldId { get; private set; }

    public UserAccountData? AccountData { get; private set; }
    public bool IsAuthenticated => AccountData != null;

    private static readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();

    private readonly ReceiverHandler _receiver;
    private readonly SenderHandler _sender;
    private readonly TimerScheduleManager _timerScheduleManager;
    private readonly Channel<IMessageQueue> _messageChannel;
    private readonly UserObjectPoolManager _userManager;
    private readonly SqlWorkerManager _sqlWorkerManager;

    private UserConnectionComponent? _userConnection;
    private ulong _sessionId;
    private int _disposedFlag;

    private int _loginAttemptCount;
    private long _loginWindowStart;
    private const int LoginAttemptsPerMinute = 5;

    public static UserSession Of(ulong sessionId, UserObjectPoolManager userManager, SqlWorkerManager sqlWorkerManager)
    {
        return new UserSession(sessionId, userManager, sqlWorkerManager);
    }

    private UserSession(ulong sessionId, UserObjectPoolManager userManager, SqlWorkerManager sqlWorkerManager)
    {
        _userManager = userManager;
        _sqlWorkerManager = sqlWorkerManager;
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
        WorldId = 0;
        AccountData = null;
        _loginAttemptCount = 0;
        _loginWindowStart = 0;

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

        if (AccountData != null)
        {
            _sqlWorkerManager.Enqueue(new LogoutSqlRequest(AccountData.AccountId));
            _userManager.UnregisterAuthenticatedSession(AccountData.UserId, this);
        }

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
            if (message is RemoteReceiveMessage remote && !IsAuthenticated)
            {
                var payloadCase = remote.MessageWrapper.PayloadCase;
                if (payloadCase != MessageWrapper.PayloadOneofCase.LoginRequest &&
                    payloadCase != MessageWrapper.PayloadOneofCase.KeepAliveRequest)
                {
                    RemoteReceiveMessage.Return(remote);
                    Disconnect();
                    return;
                }
            }

            await MessageQueueDispatcher.Instance.OnRecvMessageAsync(this, _sender, message);
            drained++;
        }

        _timerScheduleManager.Tick();
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

    public void MoveToWorld(ulong worldId) => _userManager.MoveToWorld(this, worldId);

    internal void SetWorldId(ulong worldId) => WorldId = worldId;

    internal void OnAuthenticated(UserAccountData data)
    {
        AccountData = data;
        _userManager.RegisterAuthenticatedSession(data.UserId, this);
    }

    internal bool EnqueueSqlRequest(ISqlRequest request) => _sqlWorkerManager.Enqueue(request);

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
