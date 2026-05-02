using Library.MessageQueue;
using Library.MessageQueue.Attributes.Remote;
using Library.MessageQueue.Message;
using Messages;
using Server.Actors.User.DbRequest.Sql;
using Server.Model.Message;

namespace Server.Actors.User.Handler.Remote;

[RemoteMessageHandlerAsyncAttribute(MessageWrapper.PayloadOneofCase.LoginRequest)]
public class LoginRequestHandler : IRemoteMessageHandlerAsync
{
    public Task<bool> HandleAsync(IMessageQueueReceiver receiver, MessageWrapper message)
    {
        if (receiver is not UserSession session)
            return Task.FromResult(true);

        if (session.IsAuthenticated)
        {
            session.Send(new MessageWrapper { LoginResponse = new LoginResponse { Success = false, ErrorCode = (int)LoginErrorCode.InvalidCredentials } });
            return Task.FromResult(true);
        }

        if (!session.TryConsumeLoginAttempt())
        {
            session.Send(new MessageWrapper { LoginResponse = new LoginResponse { Success = false, ErrorCode = (int)LoginErrorCode.RateLimited } });
            return Task.FromResult(true);
        }

        var req = message.LoginRequest;
        if (string.IsNullOrEmpty(req.UserId) || req.PasswordHash.IsEmpty)
        {
            session.Send(new MessageWrapper { LoginResponse = new LoginResponse { Success = false, ErrorCode = (int)LoginErrorCode.InvalidCredentials } });
            return Task.FromResult(true);
        }

        if (!session.EnqueueSqlRequest(new LoginSqlRequest(session, req.UserId, req.PasswordHash.ToByteArray())))
            session.Send(new MessageWrapper { LoginResponse = new LoginResponse { Success = false, ErrorCode = (int)LoginErrorCode.ServerError } });

        return Task.FromResult(true);
    }
}
