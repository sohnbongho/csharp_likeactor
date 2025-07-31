using Messages;

namespace Library.MessageQueue.Attributes.Remote;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class RemoteMessageHandlerAttribute : Attribute
{
    public MessageWrapper.PayloadOneofCase MessageType { get; }

    public RemoteMessageHandlerAttribute(MessageWrapper.PayloadOneofCase messageType)
    {
        MessageType = messageType;
    }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class RemoteMessageHandlerAsyncAttribute : Attribute
{
    public MessageWrapper.PayloadOneofCase MessageType { get; }

    public RemoteMessageHandlerAsyncAttribute(MessageWrapper.PayloadOneofCase messageType)
    {
        MessageType = messageType;
    }
}
