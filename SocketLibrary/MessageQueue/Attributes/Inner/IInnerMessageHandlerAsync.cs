using Library.Model;

namespace Library.MessageQueue.Attributes.Inner;

public interface IInnerMessageHandlerAsync
{
    Task<bool> HandleAsync(IMessageQueueReceiver receiver, IInnerServerMessage message);
}

