using Library.MessageQueue.Message;
using Messages;

namespace Library.MessageQueue;

public interface IMessageQueueReceiver
{    
    Task<bool> OnRecvMessageAsync(IMessageQueue messageWrapper);
    
}
