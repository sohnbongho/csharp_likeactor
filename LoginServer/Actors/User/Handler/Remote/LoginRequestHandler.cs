using Library.MessageQueue;
using Library.MessageQueue.Attributes.Remote;
using Library.MessageQueue.Message;
using LoginServer.Actors.User.DbRequest.Sql;
using LoginServer.Model.Message;
using Messages;

namespace LoginServer.Actors.User.Handler.Remote;

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
