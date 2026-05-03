using Library.MessageQueue;
using Library.MessageQueue.Attributes.Remote;
using Messages;

namespace Server.Actors.User.Handler.Remote;

[RemoteMessageHandlerAsyncAttribute(MessageWrapper.PayloadOneofCase.KeepAliveRequest)]
public class KeepAliveRequestHandler : IRemoteMessageHandlerAsync
{
    public Task<bool> HandleAsync(IMessageQueueReceiver receiver, MessageWrapper message)
    {
        if (receiver is UserSession session)
            session.UpdateKeepAlive();

        return Task.FromResult(true);
    }
}
