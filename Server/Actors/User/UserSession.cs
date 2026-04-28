using Library.ContInfo;
using Library.Logger;
using Library.MessageQueue;
using Library.MessageQueue.Message;
using Library.Network;
using Library.Timer;
using Library.Worker.Interface;
using System.Net.Sockets;
using System.Threading.Channels;

namespace Server.Actors.User;

public class UserSession : IDisposable, ITickable, IMessageQueueReceiver, ISessionUsable
{
    public ulong SessionId => _sessionId;

    // 0 = 로비, 그 외 = 소속 월드 ID
    public ulong WorldId { get; private set; }

    private static readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();

    // pool 수명동안 재사용되는 컴포넌트
    private readonly ReceiverHandler _receiver;
    private readonly SenderHandler _sender;
    private readonly TimerScheduleManager _timerScheduleManager;

    // 네트워크 수신 스레드가 쓰고, tick 스레드가 읽는 단방향 채널
    private readonly Channel<IMessageQueue> _messageChannel;

    private UserConnectionComponent? _userConnection;
    private ulong _sessionId;
    private int _disposedFlag;
    private readonly UserObjectPoolManager _userManager;

    public static UserSession Of(ulong sessionId, UserObjectPoolManager userManager)
    {
        return new UserSession(sessionId, userManager);
    }

    private UserSession(ulong sessionId, UserObjectPoolManager userManager)
    {
        _userManager = userManager;
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

        // 이전 rental에서 남은 메시지 제거
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

    // tick 스레드에서 호출 — 채널 drain 후 타이머 실행
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

        _timerScheduleManager.Tick();
    }

    // 네트워크 수신 스레드에서 호출 — 채널에 쓰기만 함
    public ValueTask<bool> EnqueueMessageAsync(IMessageQueue message)
    {
        if (Volatile.Read(ref _disposedFlag) != 0)
            return new ValueTask<bool>(false);
        return new ValueTask<bool>(_messageChannel.Writer.TryWrite(message));
    }

    public bool Send(Messages.MessageWrapper message)
    {
        if (Volatile.Read(ref _disposedFlag) != 0)
            return false;

        return _sender.Send(message);
    }

    public void Disconnect()
    {
        Dispose();
    }

    // 핸들러에서 월드 이동 시 호출. 반드시 현재 tick 스레드(핸들러 실행 중) 내에서 호출할 것.
    public void MoveToWorld(ulong worldId) => _userManager.MoveToWorld(this, worldId);

    // UserObjectPoolManager 전용 — WorldId 직접 변경.
    internal void SetWorldId(ulong worldId) => WorldId = worldId;
}
