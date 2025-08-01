using Library.Logger;
using Library.MessageQueue;
using Library.MessageQueue.Message;
using Library.Network;
using Library.ObjectPool;
using Library.Timer;
using Messages;
using Server.ServerWorker;
using Server.ServerWorker.Interface;
using System.Net.Sockets;

namespace Server.Actors.User;

public class UserSession : IDisposable, ITickable, IMessageQueueReceiver
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

    private readonly IObjectPool<UserSession> _userSessionPool;
    private readonly ThreadPoolManager _threadPoolManager;

    public static UserSession Of(
        ulong sessionId,
        MessageQueueWorkerManager manager,
        IObjectPool<UserSession> userSessionPool,
        ThreadPoolManager threadPoolManager)
    {
        var user = new UserSession(sessionId, manager, userSessionPool, threadPoolManager);
        user.RegisterHandlers();

        return user;
    }

    private UserSession(
        ulong sessionId,
        MessageQueueWorkerManager menager,
        IObjectPool<UserSession> userSessionPool,
        ThreadPoolManager threadPoolManager)
    {
        _userSessionPool = userSessionPool;
        _threadPoolManager = threadPoolManager;

        _sessionId = sessionId;
        _messageQueueDispatcher = new MessageQueueDispatcher();
        var messageQueueWorker = menager.GetWorker(sessionId);
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
        _timerScheduleManager.Dispose();
        _receiver.Dispose();
        _sender.Dispose();

        if (_userConnection != null)
        {
            _userConnection.Dispose();
            _userConnection = null;
        }
        _disposed = true;

        _threadPoolManager.Remove(this);
        _userSessionPool.Return(this);

    }

    public async Task<bool> OnRecvMessageAsync(IMessageQueue message)
    {
        if (_disposed)
            return false;

        return await _messageQueueDispatcher.OnRecvMessageAsync(this, _sender, message);
    }

    public bool SendMessage(MessageWrapper message)
    {
        return _sender.Send(message);
    }

    public void Tick()
    {
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
