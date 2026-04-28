using Library.Logger;
using Library.MessageQueue;
using Library.MessageQueue.Attributes.Remote;
using Messages;
using Server.Actors.User;

namespace Server.Actors.User.Handler.Remote;

[RemoteMessageHandlerAsyncAttribute(MessageWrapper.PayloadOneofCase.EnterWorldRequest)]
public class EnterWorldRequestHandler : IRemoteMessageHandlerAsync
{
    public Task<bool> HandleAsync(IMessageQueueReceiver receiver, MessageWrapper message)
    {
        var worldId = message.EnterWorldRequest.WorldId;

        if (receiver is UserSession session)
        {
            session.MoveToWorld(worldId);

            receiver.Send(new MessageWrapper
            {
                EnterWorldResponse = new EnterWorldResponse { Success = true }
            });
        }

        return Task.FromResult(true);
    }
}
