using Messages;

namespace Library.MessageQueue.Attributes.Remote;

public interface IRemoteMessageHandlerAsync
{
    Task<bool> HandleAsync(IMessageQueueReceiver receiver, MessageWrapper message);
}

