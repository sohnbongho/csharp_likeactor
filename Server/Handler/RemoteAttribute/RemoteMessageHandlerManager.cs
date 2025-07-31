using Library.MessageQueue;
using Messages;
using System.Reflection;

namespace Server.Handler.RemoteAttribute;

public class RemoteMessageHandlerManager
{
    private readonly Dictionary<MessageWrapper.PayloadOneofCase, IRemoteMessageHandler> _syncHandlers = new();
    private readonly Dictionary<MessageWrapper.PayloadOneofCase, IRemoteMessageHandlerAsync> _asyncHandlers = new();

    public RemoteMessageHandlerManager()
    {
    }

    public void RegisterHandlers()
    {
        var allTypes = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes());

        foreach (var type in allTypes)
        {
            if (typeof(IRemoteMessageHandler).IsAssignableFrom(type))
            {
                var attr = type.GetCustomAttribute<RemoteMessageHandlerAttribute>();
                if (attr != null)
                    _syncHandlers[attr.MessageType] = (IRemoteMessageHandler)Activator.CreateInstance(type)!;
            }

            if (typeof(IRemoteMessageHandlerAsync).IsAssignableFrom(type))
            {
                var attr = type.GetCustomAttribute<RemoteMessageHandlerAsyncAttribute>();
                if (attr != null)
                    _asyncHandlers[attr.MessageType] = (IRemoteMessageHandlerAsync)Activator.CreateInstance(type)!;
            }
        }
    }

    public async Task<bool> OnRecvMessageAsync(IMessageQueueReceiver receiver, MessageWrapper message)
    {
        var payloadCase = message.PayloadCase;
        if (_asyncHandlers.TryGetValue(payloadCase, out var handler))
        {
            return await handler.HandleAsync(receiver, message);
        }

        return true;
    }
    public bool OnRecvMessage(IMessageQueueReceiver receiver, MessageWrapper message)
    {
        var payloadCase = message.PayloadCase;
        if (_syncHandlers.TryGetValue(payloadCase, out var handler))
        {
            return handler.Handle(receiver, message);
        }
        return true;
    }

    public bool IsAsync(MessageWrapper.PayloadOneofCase type)
    {
        return _asyncHandlers.ContainsKey(type);
    }


}

