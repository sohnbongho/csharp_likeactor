using Library.MessageQueue.Message;
using Messages;

namespace Library.MessageQueue;

public interface IMessageQueueReceiver
{
    ulong SessionId { get; }
    Task<bool> EnqueueMessageAsync(IMessageQueue message);
    Task<bool> OnRecvMessageAsync(IMessageQueue messageWrapper);    
}
