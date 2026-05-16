using GameServer.Actors.User.DbRequest.Cache;
using GameServer.Model.Message;
using Library.MessageQueue;
using Library.MessageQueue.Attributes.Remote;
using Messages;

namespace GameServer.Actors.User.Handler.Remote;

[RemoteMessageHandlerAsyncAttribute(MessageWrapper.PayloadOneofCase.GameConnectRequest)]
public class GameConnectRequestHandler : IRemoteMessageHandlerAsync
{
    internal static string AuthTokenPrefix = "auth:token:";

    public Task<bool> HandleAsync(IMessageQueueReceiver receiver, MessageWrapper message)
    {
        if (receiver is not UserSession session)
            return Task.FromResult(true);

        if (session.IsAuthenticated)
        {
            session.Send(new MessageWrapper { GameConnectResponse = new GameConnectResponse { Success = false, ErrorCode = (int)GameConnectErrorCode.InvalidToken } });
            return Task.FromResult(true);
        }

        var token = message.GameConnectRequest.AuthToken;
        if (string.IsNullOrEmpty(token))
        {
            session.Send(new MessageWrapper { GameConnectResponse = new GameConnectResponse { Success = false, ErrorCode = (int)GameConnectErrorCode.InvalidToken } });
            return Task.FromResult(true);
        }

        if (!session.EnqueueCacheRequest(new ValidateTokenCacheRequest(session, token, AuthTokenPrefix)))
            session.Send(new MessageWrapper { GameConnectResponse = new GameConnectResponse { Success = false, ErrorCode = (int)GameConnectErrorCode.ServerError } });

        return Task.FromResult(true);
    }
}
