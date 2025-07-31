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
