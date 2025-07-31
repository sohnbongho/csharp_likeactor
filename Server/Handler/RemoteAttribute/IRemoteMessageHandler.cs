using Library.MessageQueue;
using Messages;

namespace Server.Handler.RemoteAttribute;

public interface IRemoteMessageHandler
{
    bool Handle(IMessageQueueReceiver receiver, MessageWrapper message);
}

