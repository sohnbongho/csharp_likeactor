using Messages;

namespace Library.MessageQueue.Message;

public interface IMessageQueue
{

}
public class RemoteReceiveMessage : IMessageQueue
{
    public MessageWrapper Message { get; set; } = null!;

}
public class RemoteReceiveMessageAsync : IMessageQueue
{
    public MessageWrapper Message { get; set; } = null!;

}

public class RemoteSendMessageAsync : IMessageQueue
{
    public MessageWrapper Message { get; set; } = null!;
}
