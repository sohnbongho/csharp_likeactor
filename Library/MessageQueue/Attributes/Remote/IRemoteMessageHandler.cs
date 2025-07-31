using Messages;

namespace Library.MessageQueue.Attributes.Remote;

public interface IRemoteMessageHandler
{
    bool Handle(IMessageQueueReceiver receiver, MessageWrapper message);
}

