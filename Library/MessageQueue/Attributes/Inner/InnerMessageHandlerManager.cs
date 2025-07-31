using Library.Model;
using System.Reflection;

namespace Library.MessageQueue.Attributes.Inner;

public class InnerMessageHandlerManager
{
    private readonly Dictionary<Type, IInnerMessageHandler> _syncHandlers = new();
    private readonly Dictionary<Type, IInnerMessageHandlerAsync> _asyncHandlers = new();

    public InnerMessageHandlerManager()
    {
    }

    public void RegisterHandlers()
    {
        var allTypes = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes());

        foreach (var type in allTypes)
        {
            if (typeof(IInnerMessageHandler).IsAssignableFrom(type))
            {
                var attr = type.GetCustomAttribute<InnerMessageHandlerAttribute>();
                if (attr != null)
                    _syncHandlers[attr.MessageType] = (IInnerMessageHandler)Activator.CreateInstance(type)!;
            }

            if (typeof(IInnerMessageHandlerAsync).IsAssignableFrom(type))
            {
                var attr = type.GetCustomAttribute<InnerMessageHandlerAsyncAttribute>();
                if (attr != null)
                    _asyncHandlers[attr.MessageType] = (IInnerMessageHandlerAsync)Activator.CreateInstance(type)!;
            }
        }
    }
    public async Task<bool> OnRecvMessageAsync(IMessageQueueReceiver receiver, IInnerServerMessage message)
    {
        var messageType = message.GetType();
        if (_asyncHandlers.TryGetValue(messageType, out var handler))
        {
            return await handler.HandleAsync(receiver, message);
        }

        return true;
    }
    public bool OnRecvMessage(IMessageQueueReceiver receiver, IInnerServerMessage message)
    {
        var messageType = message.GetType();
        if (_syncHandlers.TryGetValue(messageType, out var handler))
        {
            return handler.Handle(receiver, message);
        }
        return true;
    }

    public bool IsAsync(IInnerServerMessage message)
    {
        var messageType = message.GetType();
        return _asyncHandlers.ContainsKey(messageType);
    }
}

