using Library.Logger;
using Library.MessageQueue;
using Library.MessageQueue.Message;
using Library.Network;
using Library.Timer;
using Messages;
using Server.ServerWorker.Interface;
using System.Net.Sockets;

namespace Server.Actors.User;

public class UserSession : IDisposable, ITickable, IMessageQueueReceiver
{
    public ulong SessionId => _sessionId;

    private readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();

    private ReceiverHandler? _receiver;
    private SenderHandler? _sender;

    private UserConnectionComponent? _userConnection;
    private readonly MessageQueueWorker _messageQueueWorker;
    private readonly MessageQueueDispatcher _messageQueueDispatcher;
    private readonly TimerScheduleManager _timerScheduleManager;
    private readonly ulong _sessionId;
    private bool _disposed;

    public static UserSession Of(ulong sessionId, MessageQueueWorkerManager manager)
    {
        var user = new UserSession(sessionId, manager);
        user.RegisterHandlers();

        return user;
    }

    private UserSession(ulong sessionId, MessageQueueWorkerManager menager)
    {
        _sessionId = sessionId;
        _messageQueueDispatcher = new MessageQueueDispatcher();
        _timerScheduleManager = new TimerScheduleManager();

        var messageQueueWorker = menager.GetWorker(sessionId);
        _messageQueueWorker = messageQueueWorker;
    }

    private void RegisterHandlers()
    {
        _messageQueueDispatcher.RegisterHandlers();
    }


    public void Bind(TcpClient client)
    {
        _disposed = false;

        _receiver = new ReceiverHandler(this, _messageQueueWorker);
        _userConnection = new UserConnectionComponent(client);
        _sender = new SenderHandler(this, _messageQueueWorker);

        _receiver.SetStream(_userConnection.Stream);
        _sender.SetStream(_userConnection.Stream);
    }


    public void Dispose()
    {
        if (_receiver != null)
        {
            _receiver.Dispose();
            _receiver = null;
        }
        if (_sender != null)
        {
            _sender.Dispose();
            _sender = null;
        }

        if (_userConnection != null)
        {
            _userConnection.Dispose();
            _userConnection = null;
        }
        _disposed = true;
    }
    public async Task RunAsync()
    {
        try
        {
            if (_receiver == null)
                return;

            if (_userConnection == null)
                return;

            while (true)
            {
                var succeed = await _receiver.OnReceiveAsync();
                if (succeed == false)
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(() => $"fail RunAsync", ex);
        }
        finally
        {
            Dispose();
        }
    }

    public async Task<bool> OnRecvMessageAsync(IMessageQueue message)
    {
        if (_disposed)
            return false;

        return await _messageQueueDispatcher.OnRecvMessageAsync(this, _sender, message);
    }

    public async Task<bool> SendQueueAsync(MessageWrapper message)
    {
        if (_sender == null)
            return false;

        return await _sender.AddQueueAsync(message);
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

}
