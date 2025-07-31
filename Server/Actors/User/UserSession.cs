using Library.Logger;
using Library.MessageQueue;
using Library.MessageQueue.Message;
using Library.Network;
using Messages;
using Server.Handler.InnerAttribute;
using Server.Handler.RemoteAttribute;
using Server.Model;
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
        _receiver = new ReceiverHandler(this, _messageQueueWorker, _remoteMessageHandlers.IsAsync);
        _userConnection = new UserConnectionSystem(client);
        _sender = new SenderHandler(this, _messageQueueWorker);
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
    }
    public async Task RunAsync()
    {
        try
        {
            if (_receiver == null)
                return;

            if (_userConnection == null)
                return;

            var stream = _userConnection.Stream;
            if (stream == null)
                return;

            while (true)
            {
                var succeed = await _receiver.OnReceiveAsync(stream);
                if (succeed == false)
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(() => $"[오류] 클라이언트 처리 중 예외 발생 ", ex);
        }
        finally
        {
            Dispose();
        }
    }

    public async Task<bool> OnRecvMessageAsync(IMessageQueue message)
    {
        if (message is RemoteReceiveMessage receiveMessage)
        {
            _remoteMessageHandlers.OnRecvMessage(this, receiveMessage.Message);
        }
        else if (message is RemoteReceiveMessageAsync receiveMessageAsync)
        {
            await _remoteMessageHandlers.OnRecvMessageAsync(this, receiveMessageAsync.Message);
        }
        else if (message is RemoteSendMessageAsync sendMessage)
        {
            if (_sender != null)
            {
                await _sender.SendAsync(sendMessage.Message);
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

    public void OnRecvMessageHandle(IInnerServerMessage message)
    {
    }


}
