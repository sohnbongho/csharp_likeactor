using Library.Model;

namespace Library.MessageQueue.Attributes.Inner;

public interface IInnerMessageHandler
{
    bool Handle(IMessageQueueReceiver receiver, IInnerServerMessage message);
}

