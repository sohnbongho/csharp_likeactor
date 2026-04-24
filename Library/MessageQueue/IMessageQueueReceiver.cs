using Library.MessageQueue.Message;
using Messages;

namespace Library.MessageQueue;

public interface IMessageQueueReceiver
{
    ulong SessionId { get; }
    ValueTask<bool> EnqueueMessageAsync(IMessageQueue message);
    bool Send(MessageWrapper message);
}
