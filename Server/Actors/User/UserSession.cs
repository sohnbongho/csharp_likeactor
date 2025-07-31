using Library.Logger;
using Library.MessageQueue;
using Library.MessageQueue.Message;
using Library.Network;
using Messages;
using Server.Handler.InnerAttribute;
using Server.Handler.RemoteAttribute;
using Server.ServerWorker.Interface;
using System.Net.Sockets;

namespace Server.Actors.User;

public class UserSession : IDisposable, ITickable, IMessageQueueReceiver
{
    public ulong SessionId => _sessionId;

    private readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();

    private ReceiverHandler? _receiver;
    private SenderHandler? _sender;

    private UserConnectionSystem? _userConnection;
    private readonly MessageQueueWorker _messageQueueWorker;
    private readonly ulong _sessionId;
    private readonly InnerMessageHandlerManager _innerMessageHandlers;
    private readonly RemoteMessageHandlerManager _remoteMessageHandlers;
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
        _innerMessageHandlers = new();
        _remoteMessageHandlers = new();

        var messageQueueWorker = menager.GetWorker(sessionId);
        _messageQueueWorker = messageQueueWorker;        
    }

    private void RegisterHandlers()
    {
        _innerMessageHandlers.RegisterHandlers();
        _remoteMessageHandlers.RegisterHandlers();
    }


    public void Bind(TcpClient client)
    {
        _disposed = false;

        _receiver = new ReceiverHandler(this, _messageQueueWorker);
        _userConnection = new UserConnectionSystem(client);
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
        if(_disposed) 
            return false;

        if (message is RemoteReceiveMessage receiveMessage)
        {
            var messageWrapper = receiveMessage.MessageWrapper;
            if (_remoteMessageHandlers.IsAsync(messageWrapper.PayloadCase))
            {
                await _remoteMessageHandlers.OnRecvMessageAsync(this, messageWrapper);
            }
            else
            {
                _remoteMessageHandlers.OnRecvMessage(this, messageWrapper);
            }
        }

        else if (message is RemoteSendMessage sendMessage)
        {
            if (_sender != null)
            {
                await _sender.SendAsync(sendMessage.MessageWrapper);
            }
        }
        return true;
    }

    public async Task<bool> SendQueueAsync(MessageWrapper message)
    {
        if (_sender == null)
            return false;

        return await _sender.AddQueueAsync(message);
    }

    public void Tick()
    {
    }

    public async Task<bool> EnqueueAsync(IMessageQueue message)
    {
        await _messageQueueWorker.EnqueueAsync(this, message);
        return true;
    }
    
}
