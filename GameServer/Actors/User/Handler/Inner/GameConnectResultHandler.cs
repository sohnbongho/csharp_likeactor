using GameServer.Model.Message;
using Library.MessageQueue;
using Library.MessageQueue.Attributes.Inner;
using Library.Model;
using Messages;

namespace GameServer.Actors.User.Handler.Inner;

[InnerMessageHandlerAsyncAttribute(typeof(GameConnectResultMessage))]
public class GameConnectResultHandler : IInnerMessageHandlerAsync
{
    public Task<bool> HandleAsync(IMessageQueueReceiver receiver, IInnerServerMessage message)
    {
        if (receiver is not UserSession session)
            return Task.FromResult(true);

        var result = (GameConnectResultMessage)message;

        if (!result.Success)
        {
            session.Send(new MessageWrapper { GameConnectResponse = new GameConnectResponse { Success = false, ErrorCode = (int)result.ErrorCode } });
            return Task.FromResult(true);
        }

        session.OnAuthenticated(new UserAccountData { AccountId = result.AccountId, UserId = result.UserId });
        session.Send(new MessageWrapper { GameConnectResponse = new GameConnectResponse { Success = true } });

        return Task.FromResult(true);
    }
}
