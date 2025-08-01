using Library.Model;
using Messages;

namespace Library.MessageQueue.Message;

public interface IMessageQueue
{

}
public class RemoteReceiveMessage : IMessageQueue
{
    public MessageWrapper MessageWrapper { get; set; } = null!;

}


public class RemoteSendMessage : IMessageQueue
{
    public MessageWrapper MessageWrapper { get; set; } = null!;
}

public class RemoteSendCompleteMessage : IMessageQueue
{
    
}

public class InnerReceiveMessage : IMessageQueue
{
    public IInnerServerMessage Message { get; set; } = null!;

}