namespace Library.MessageQueue.Attributes.Inner;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class InnerMessageHandlerAttribute : Attribute
{
    public Type MessageType { get; }

    public InnerMessageHandlerAttribute(Type messageType)
    {
        MessageType = messageType;
    }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class InnerMessageHandlerAsyncAttribute : Attribute
{
    public Type MessageType { get; }

    public InnerMessageHandlerAsyncAttribute(Type messageType)
    {
        MessageType = messageType;
    }
}
