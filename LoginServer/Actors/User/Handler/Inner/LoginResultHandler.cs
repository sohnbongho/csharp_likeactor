using Library.MessageQueue;
using Library.MessageQueue.Attributes.Inner;
using Library.Model;
using LoginServer.Model.Message;
using Messages;

namespace LoginServer.Actors.User.Handler.Inner;

[InnerMessageHandlerAsyncAttribute(typeof(LoginResultMessage))]
public class LoginResultHandler : IInnerMessageHandlerAsync
{
    public async Task<bool> HandleAsync(IMessageQueueReceiver receiver, IInnerServerMessage message)
    {
        if (receiver is not UserSession session)
            return true;

        var result = (LoginResultMessage)message;

        if (!result.Success)
        {
            session.Send(new MessageWrapper { LoginResponse = new LoginResponse { Success = false, ErrorCode = (int)result.ErrorCode } });
            return true;
        }

        var token = await session.TryIssueAuthToken(result.AccountId, result.UserId);
        if (token == null)
        {
            session.Send(new MessageWrapper { LoginResponse = new LoginResponse { Success = false, ErrorCode = (int)LoginErrorCode.ServerError } });
            return true;
        }

        session.OnAuthenticated(new UserAccountData { AccountId = result.AccountId, UserId = result.UserId });
        session.Send(new MessageWrapper { LoginResponse = new LoginResponse { Success = true, AuthToken = token } });

        return true;
    }
}
