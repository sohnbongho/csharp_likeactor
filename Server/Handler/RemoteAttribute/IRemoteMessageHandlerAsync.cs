using Library.MessageQueue;
using Messages;

namespace Server.Handler.RemoteAttribute;

public interface IRemoteMessageHandlerAsync
{
    Task<bool> HandleAsync(IMessageQueueReceiver receiver, MessageWrapper message);
}

