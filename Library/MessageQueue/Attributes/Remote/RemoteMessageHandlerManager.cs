using Messages;
using System.Reflection;

namespace Library.MessageQueue.Attributes.Remote;

public class RemoteMessageHandlerManager
{
    private static readonly Dictionary<MessageWrapper.PayloadOneofCase, IRemoteMessageHandler> _cachedSyncHandlers = new();
    private static readonly Dictionary<MessageWrapper.PayloadOneofCase, IRemoteMessageHandlerAsync> _cachedAsyncHandlers = new();
    
    private readonly Dictionary<MessageWrapper.PayloadOneofCase, IRemoteMessageHandler> _syncHandlers = new();
    private readonly Dictionary<MessageWrapper.PayloadOneofCase, IRemoteMessageHandlerAsync> _asyncHandlers = new();    

    public RemoteMessageHandlerManager()
    {        
    }

    public void RegisterHandlers()
    {
        if (_cachedSyncHandlers.Any() == false && _cachedAsyncHandlers.Any() == false)
        {
            RegisterCachedHandlers();
        }

        foreach (var kv in _cachedSyncHandlers)
        {
            var instance = (IRemoteMessageHandler)Activator.CreateInstance(kv.Value.GetType())!;
            _syncHandlers[kv.Key] = instance;
        }

        foreach (var kv in _cachedAsyncHandlers)
        {
            var instance = (IRemoteMessageHandlerAsync)Activator.CreateInstance(kv.Value.GetType())!;
            _asyncHandlers[kv.Key] = instance;
        }
    }
    private void RegisterCachedHandlers()
    {
        var allTypes = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes());

        foreach (var type in allTypes)
        {
            if (typeof(IRemoteMessageHandler).IsAssignableFrom(type))
            {
                var attr = type.GetCustomAttribute<RemoteMessageHandlerAttribute>();
                if (attr != null)
                    _cachedSyncHandlers[attr.MessageType] = (IRemoteMessageHandler)Activator.CreateInstance(type)!;
            }

            if (typeof(IRemoteMessageHandlerAsync).IsAssignableFrom(type))
            {
                var attr = type.GetCustomAttribute<RemoteMessageHandlerAsyncAttribute>();
                if (attr != null)
                    _cachedAsyncHandlers[attr.MessageType] = (IRemoteMessageHandlerAsync)Activator.CreateInstance(type)!;
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

