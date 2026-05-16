using Library.Logger;
using Library.MessageQueue;
using Library.MessageQueue.Attributes.Remote;
using Messages;

namespace DummyClient.Session.Handler.Remote;

[RemoteMessageHandlerAsync(MessageWrapper.PayloadOneofCase.LoginResponse)]
public class LoginResponseHandler : IRemoteMessageHandlerAsync
{
    private static readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();

    public async Task<bool> HandleAsync(IMessageQueueReceiver receiver, MessageWrapper message)
    {
        var resp = message.LoginResponse;

        if (!resp.Success)
        {
            _logger.Warn(() => $"[LoginResponse] 로그인 실패 (errorCode={resp.ErrorCode}, sessionId={receiver.SessionId})");
            return true;
        }

        if (receiver is UserSession session)
            await session.ConnectToGameServerAsync(resp.AuthToken);

        return true;
    }
}
