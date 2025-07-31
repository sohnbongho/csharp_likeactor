using Library.MessageQueue;
using Server.Model;

namespace Server.Handler.InnerAttribute;

public interface IInnerMessageHandlerAsync
{
    Task<bool> HandleAsync(IMessageQueueReceiver receiver, IInnerServerMessage message);
}

