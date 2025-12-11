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

    private readonly ReceiverHandler _receiver;
    private readonly SenderHandler _sender;
    private UserConnectionComponent? _userConnection;

    private readonly MessageQueueWorker _messageQueueWorker;
    private readonly MessageQueueDispatcher _messageQueueDispatcher;
    private readonly TimerScheduleManager _timerScheduleManager;

    private readonly ulong _sessionId;
    private bool _disposed;

    private readonly UserObjectPoolManager _userManager;

    public static UserSession Of(
        ulong sessionId,
        UserObjectPoolManager userManager,
        MessageQueueWorkerManager workerManager)
    {
        var user = new UserSession(sessionId, userManager, workerManager);
        user.RegisterHandlers();

        return user;
    }

    private UserSession(
        ulong sessionId,
        UserObjectPoolManager userManager,
        MessageQueueWorkerManager workerManager)
    {
        _userManager = userManager;
        _sessionId = sessionId;
        _messageQueueDispatcher = new MessageQueueDispatcher();
        var messageQueueWorker = workerManager.GetWorker(sessionId);
        _messageQueueWorker = messageQueueWorker;

        _timerScheduleManager = new TimerScheduleManager();
        _receiver = new ReceiverHandler(this, _messageQueueWorker);
        _sender = new SenderHandler();
    }

    private void RegisterHandlers()
    {
        _messageQueueDispatcher.RegisterHandlers();
    }


    public void Bind(Socket socket)
    {
        _disposed = false;
        _userConnection = new UserConnectionComponent(socket);

        _sender.Bind(_userConnection.Socket);
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

        _timerScheduleManager.Dispose();
        _receiver.Dispose();
        _sender.Dispose();

        _userManager.RemoveUser(this);
    }

    public async Task<bool> OnRecvMessageAsync(IMessageQueue message)
    {
        if (_disposed)
            return false;

        return await _messageQueueDispatcher.OnRecvMessageAsync(this, _sender, message);
    }

    public void Tick()
    {
        //_logger.Debug(() => "Tick()");
        _timerScheduleManager.Tick();
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
