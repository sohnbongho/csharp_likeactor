using Messages;
using System.Reflection;

namespace Library.MessageQueue.Attributes.Remote;

public class RemoteMessageHandlerManager
{
    // 핸들러는 stateless이므로 전 세션이 단일 인스턴스를 공유한다.
    private static readonly Dictionary<MessageWrapper.PayloadOneofCase, IRemoteMessageHandler> _syncHandlers = new();
    private static readonly Dictionary<MessageWrapper.PayloadOneofCase, IRemoteMessageHandlerAsync> _asyncHandlers = new();
    private static readonly object _initLock = new();
    private static volatile bool _initialized;

    public void RegisterHandlers()
    {
        if (_initialized)
            return;

        lock (_initLock)
        {
            if (_initialized)
                return;

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

            _initialized = true;
        }
    }

    public async Task<bool> OnRecvMessageAsync(IMessageQueueReceiver receiver, MessageWrapper message)
    {
        if (_asyncHandlers.TryGetValue(message.PayloadCase, out var handler))
            return await handler.HandleAsync(receiver, message);
        return true;
    }

    public bool OnRecvMessage(IMessageQueueReceiver receiver, MessageWrapper message)
    {
        if (_syncHandlers.TryGetValue(message.PayloadCase, out var handler))
            return handler.Handle(receiver, message);
        return true;
    }

    public bool IsAsync(MessageWrapper.PayloadOneofCase type) => _asyncHandlers.ContainsKey(type);
}

