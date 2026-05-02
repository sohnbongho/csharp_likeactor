using Library.MessageQueue;
using Library.MessageQueue.Attributes.Inner;
using Library.Model;
using Messages;
using Server.Actors.User.Model;
using Server.Model.Message;

namespace Server.Actors.User.Handler.Inner;

[InnerMessageHandlerAsyncAttribute(typeof(LoginResultMessage))]
public class LoginResultHandler : IInnerMessageHandlerAsync
{
    public Task<bool> HandleAsync(IMessageQueueReceiver receiver, IInnerServerMessage message)
    {
        if (receiver is not UserSession session)
            return Task.FromResult(true);

        var result = (LoginResultMessage)message;

        if (result.Success)
        {
            session.OnAuthenticated(new UserAccountData { AccountId = result.AccountId, UserId = result.UserId });
            session.Send(new MessageWrapper { LoginResponse = new LoginResponse { Success = true } });
        }
        else
        {
            session.Send(new MessageWrapper { LoginResponse = new LoginResponse { Success = false, ErrorCode = (int)result.ErrorCode } });
        }

        return Task.FromResult(true);
    }
}
