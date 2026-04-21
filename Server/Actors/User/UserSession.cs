using Library.Logger;
using Library.MessageQueue;
using Library.MessageQueue.Message;
using Library.Network;
using Library.Timer;
using Library.Worker.Interface;
using System.Net.Sockets;

namespace Server.Actors.User;

public class UserSession : IDisposable, ITickable, IMessageQueueReceiver, ISessionUsable
{
    public ulong SessionId => _sessionId;

    private readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();

    // pool 수명동안 재사용되는 컴포넌트 — 매 rental마다 할당하지 않는다.
    private readonly ReceiverHandler _receiver;
    private readonly SenderHandler _sender;
    private readonly TimerScheduleManager _timerScheduleManager;

    private UserConnectionComponent? _userConnection;

    private MessageQueueWorker _messageQueueWorker;

    private ulong _sessionId;
    private bool _disposed;

    private readonly UserObjectPoolManager _userManager;

    public static UserSession Of(
        ulong sessionId,
        UserObjectPoolManager userManager,
        MessageQueueWorkerManager workerManager)
    {
        return new UserSession(sessionId, userManager, workerManager);
    }

    private UserSession(
        ulong sessionId,
        UserObjectPoolManager userManager,
        MessageQueueWorkerManager workerManager)
    {
        _userManager = userManager;
        _sessionId = sessionId;
        _messageQueueWorker = workerManager.GetWorker(sessionId);

        _timerScheduleManager = new TimerScheduleManager();
        _receiver = new ReceiverHandler(this, _messageQueueWorker);
        _sender = new SenderHandler();
    }

    public void Reinitialize(ulong sessionId, MessageQueueWorkerManager workerManager)
    {
        _sessionId = sessionId;
        _messageQueueWorker = workerManager.GetWorker(sessionId);

        // 재사용 컴포넌트는 Reset만: 8KB 송수신 버퍼/SAEA/PriorityQueue 재할당 회피.
        _timerScheduleManager.Reset();
        _receiver.Reset();
        _sender.Reset();
        _disposed = false;
    }


    public void Bind(Socket socket)
    {
        _disposed = false;
        _userConnection = new UserConnectionComponent(socket);

        _sender.Bind(_userConnection.Socket, Disconnect);
        _receiver.Bind(_userConnection.Socket);
        _receiver.StartReceive();
    }


    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_userConnection != null)
        {
            _userConnection.Dispose(); // 소켓을 먼저 닫아 수신 루프를 즉시 끊는다
            _userConnection = null;
        }

        // 최종 Dispose가 아니라 pool 반환을 위한 Reset.
        _timerScheduleManager.Reset();
        _receiver.Reset();
        _sender.Reset();

        _userManager.RemoveUser(this);
    }

    public async Task<bool> OnRecvMessageAsync(IMessageQueue message)
    {
        if (_disposed)
            return false;

        return await MessageQueueDispatcher.Instance.OnRecvMessageAsync(this, _sender, message);
    }

    public void Tick()
    {
        //_logger.Debug(() => "Tick()");
        _timerScheduleManager.Tick();
    }

    public ValueTask<bool> EnqueueMessageAsync(IMessageQueue message)
    {
        return _messageQueueWorker.EnqueueAsync(this, message);
    }

    public bool Send(Messages.MessageWrapper message)
    {
        return _disposed ? false : _sender.Send(message);
    }

    public void Disconnect()
    {
        Dispose();
    }

}
