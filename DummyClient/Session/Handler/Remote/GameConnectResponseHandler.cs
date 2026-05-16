using Library.Logger;
using Library.MessageQueue;
using Library.MessageQueue.Attributes.Remote;
using Messages;

namespace DummyClient.Session.Handler.Remote;

[RemoteMessageHandlerAsync(MessageWrapper.PayloadOneofCase.GameConnectResponse)]
public class GameConnectResponseHandler : IRemoteMessageHandlerAsync
{
    private static readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();

    public Task<bool> HandleAsync(IMessageQueueReceiver receiver, MessageWrapper message)
    {
        var resp = message.GameConnectResponse;

        if (resp.Success)
        {
            if (receiver is UserSession session)
                session.OnAuthenticated();
        }
        else
        {
            _logger.Warn(() => $"[GameConnectResponse] 게임서버 연결 실패 (errorCode={resp.ErrorCode}, sessionId={receiver.SessionId})");
        }

        return Task.FromResult(true);
    }
}
