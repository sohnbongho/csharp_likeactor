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
using GameServer.Actors.User.DbRequest.Sql;
using GameServer.Rooms;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading.Channels;

namespace GameServer.Actors.User;

public class UserSession : IDisposable, ITickable, IMessageQueueReceiver, ISessionUsable
{
    public ulong SessionId => _sessionId;
    public ulong WorldId { get; private set; }
    public float X { get; private set; }
    public float Y { get; private set; }

    public UserAccountData? AccountData { get; private set; }
    public bool IsAuthenticated => AccountData != null;

    // 참가 중인 협동 방. 룸 스레드가 Attach/Detach하고 세션 스레드가 읽으므로 volatile.
    private volatile Room? _room;
    public Room? Room => _room;
    public int RoomSlot { get; private set; } = -1;

    private static readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();

    private readonly ReceiverHandler _receiver;
    private readonly SenderHandler _sender;
    private readonly TimerScheduleManager _timerScheduleManager;
    private readonly Channel<IMessageQueue> _messageChannel;
    private readonly UserObjectPoolManager _userManager;
    private readonly SqlWorkerManager _sqlWorkerManager;
    private readonly CacheWorkerManager _cacheWorkerManager;

    private UserConnectionComponent? _userConnection;
    private ulong _sessionId;
    private int _disposedFlag;
    private long _lastKeepAliveReceivedAt;

    public static UserSession Of(ulong sessionId, UserObjectPoolManager userManager, SqlWorkerManager sqlWorkerManager, CacheWorkerManager cacheWorkerManager)
    {
        return new UserSession(sessionId, userManager, sqlWorkerManager, cacheWorkerManager);
    }

    private UserSession(ulong sessionId, UserObjectPoolManager userManager, SqlWorkerManager sqlWorkerManager, CacheWorkerManager cacheWorkerManager)
    {
        _userManager = userManager;
        _sqlWorkerManager = sqlWorkerManager;
        _cacheWorkerManager = cacheWorkerManager;
        _sessionId = sessionId;

        _messageChannel = Channel.CreateBounded<IMessageQueue>(
            new BoundedChannelOptions(SessionConstInfo.MaxMessageChannelCapacity)
            {
                SingleReader = true,
                AllowSynchronousContinuations = false,
                FullMode = BoundedChannelFullMode.DropWrite
            });

        _timerScheduleManager = new TimerScheduleManager();
        _receiver = new ReceiverHandler(this, Disconnect);
        _sender = new SenderHandler(Disconnect);
    }

    public void Reinitialize(ulong sessionId)
    {
        _sessionId = sessionId;
        WorldId = 0;
        AccountData = null;
        _room = null;
        RoomSlot = -1;
        _lastKeepAliveReceivedAt = 0;

        while (_messageChannel.Reader.TryRead(out var msg))
        {
            if (msg is RemoteReceiveMessage rem) RemoteReceiveMessage.Return(rem);
        }

        _timerScheduleManager.Reset();
        _sender.Reset();
        Volatile.Write(ref _disposedFlag, 0);
    }

    public void Bind(Socket socket)
    {
        // 이전 연결 참조는 덮어쓴다. 구 소켓의 stale 콜백은 핸들러의 ReferenceEquals 가드가 차단한다.
        _userConnection = new UserConnectionComponent(socket);
        _sender.Bind(_userConnection);
        _receiver.Bind(_userConnection);
        Volatile.Write(ref _disposedFlag, 0);
        _receiver.StartReceive();
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposedFlag, 1, 0) != 0)
            return;

        // 1) 소켓 닫기. 이후 pending ReceiveAsync/SendAsync는 오류로 완료되고
        //    핸들러가 Disconnect()를 다시 부르지만 위 CAS에서 즉시 반환된다.
        _userConnection?.Close();

        // 2) 도메인 정리
        // 방에 있었다면 룸 스레드에 퇴장을 알린다. 이 세션 객체는 곧 풀로 돌아가므로
        // 룸은 SessionId를 비교해 재사용된 세션을 건드리지 않는다.
        var room = _room;
        if (room != null)
        {
            var self = this;
            room.Enqueue(r => r.Leave(self, respond: false));
        }

        if (AccountData != null)
        {
            _sqlWorkerManager.Enqueue(new LogoutSqlRequest(AccountData.AccountId));
            _userManager.UnregisterAuthenticatedSession(AccountData.UserId, this);
        }

        // 3) 핸들러/타이머 자기 상태 초기화 (소켓 무관)
        _timerScheduleManager.Reset();
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

            // 전투 중에는 입력이 KeepAlive 대신 활동 증거가 된다: 어떤 메시지든 받으면 타임아웃을 미룬다
            if (message is RemoteReceiveMessage)
                UpdateKeepAlive();

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

    public void MoveToWorld(ulong worldId) => _userManager.MoveToWorld(this, worldId);

    public void UpdatePosition(float x, float y)
    {
        X = x;
        Y = y;
    }

    internal void SetWorldId(ulong worldId) => WorldId = worldId;

    // 룸 스레드에서 호출
    internal void AttachRoom(Room room, int slot)
    {
        RoomSlot = slot;
        _room = room;
    }

    internal void DetachRoom()
    {
        _room = null;
        RoomSlot = -1;
    }

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

    internal bool EnqueueCacheRequest(ICacheRequest request) => _cacheWorkerManager.Enqueue(request);

    // 미인증 세션은 GameConnectRequest, KeepAliveRequest 외 메시지를 보내면 연결 종료
    private bool IsBlockedBeforeAuth(IMessageQueue message)
    {
        if (IsAuthenticated || message is not RemoteReceiveMessage remote)
            return false;

        var payloadCase = remote.MessageWrapper.PayloadCase;
        if (payloadCase == MessageWrapper.PayloadOneofCase.GameConnectRequest ||
            payloadCase == MessageWrapper.PayloadOneofCase.KeepAliveRequest)
            return false;

        RemoteReceiveMessage.Return(remote);
        Disconnect();
        return true;
    }
}
