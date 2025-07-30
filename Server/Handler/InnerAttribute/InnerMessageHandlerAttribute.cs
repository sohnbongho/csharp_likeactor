using System;

namespace Server.Handler.InnerAttribute;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class InnerMessageHandlerAttribute : Attribute
{
    public Type MessageType { get; }

    public InnerMessageHandlerAttribute(Type messageType)
    {
        MessageType = messageType;
    }
}
