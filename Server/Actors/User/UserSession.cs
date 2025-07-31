using Library.Logger;
using Library.MessageQueue;
using Library.Network;
using Messages;
using Server.Handler;
using Server.Handler.InnerAttribute;
using Server.Handler.RemoteAttribute;
using Server.Model;
using Server.ServerWorker.Interface;
using System.Net.Sockets;

namespace Server.Actors.User;

public class UserSession : IDisposable, ITickable, IMessageReceiver
{
    public ulong SessionId => _sessionId;

    private readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();

    private ReceiverHandler? _receiver;
    private SenderHandler? _sender;
    private UserConnectionSystem? _userConnection;
    private MessageQueue<IInnerServerMessage>? _messageQueue;
    private readonly ulong _sessionId;
    private readonly InnerMessageHandlerManager _innerMessageHandlers;
    private readonly RemoteMessageHandlerManager _remoteMessageHandlers;

    public static UserSession Of(ulong sessionId)
    {
        var user = new UserSession(sessionId);
        user.RegisterHandlers();

        return user;
    }


    private UserSession(ulong sessionId)
    {
        _sessionId = sessionId;
        _innerMessageHandlers = new();
        _remoteMessageHandlers = new();
    }
    private void RegisterHandlers()
    {
        _innerMessageHandlers.RegisterHandlers();
        _remoteMessageHandlers.RegisterHandlers();
    }


    public void Bind(TcpClient client)
    {
        _receiver = new ReceiverHandler(OnRecvMessage, OnRecvMessageAsync, _remoteMessageHandlers.IsAsync);
        _sender = new SenderHandler();
        _userConnection = new UserConnectionSystem(client);
        _messageQueue = new MessageQueue<IInnerServerMessage>();
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

        if (_messageQueue != null)
        {
            _messageQueue.Dispose();
            _messageQueue = null;
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
    public void OnRecvMessage(MessageWrapper messageWrapper)
    {
        _logger.Debug(() => $"OnRecvMessage type:{messageWrapper.PayloadCase.ToString()}");
        _remoteMessageHandlers.OnRecvMessage(this, messageWrapper);

    }

    public async Task<bool> OnRecvMessageAsync(MessageWrapper messageWrapper)
    {
        _logger.Debug(() => $"OnRecvMessageAsync type:{messageWrapper.PayloadCase.ToString()}");
        return await _remoteMessageHandlers.OnRecvMessageAsync(this, messageWrapper);
    }

    public async Task<bool> SendAsync(MessageWrapper message)
    {
        if (_sender == null)
            return false;

        if (_userConnection == null)
            return false;

        var stream = _userConnection.Stream;
        if (stream == null)
            return false;

        return await _sender.SendAsync(stream, message);
    }

    public void Tick()
    {        
        if (_messageQueue == null)
            return;

        while (_messageQueue.TryDequeue(out var message))
        {
            // 내부 메시지에 대한 처리 attribute로

        }
    }

    public void OnRecvMessageHandle(IInnerServerMessage message)
    {
    }
}
