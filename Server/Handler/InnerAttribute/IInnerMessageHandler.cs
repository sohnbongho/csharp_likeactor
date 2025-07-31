using Library.MessageQueue;
using Server.Model;

namespace Server.Handler.InnerAttribute;

public interface IInnerMessageHandler
{
    bool Handle(IMessageQueueReceiver receiver, IInnerServerMessage message);
}

