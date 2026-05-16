using Library.Model;
using System.Reflection;

namespace Library.MessageQueue.Attributes.Inner;

public class InnerMessageHandlerManager
{
    // 핸들러는 stateless이므로 전 세션이 단일 인스턴스를 공유한다.
    private static readonly Dictionary<Type, IInnerMessageHandler> _syncHandlers = new();
    private static readonly Dictionary<Type, IInnerMessageHandlerAsync> _asyncHandlers = new();
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

            _initialized = true;
        }
    }

    public async Task<bool> OnRecvMessageAsync(IMessageQueueReceiver receiver, IInnerServerMessage message)
    {
        if (_asyncHandlers.TryGetValue(message.GetType(), out var handler))
            return await handler.HandleAsync(receiver, message);
        return true;
    }

    public bool OnRecvMessage(IMessageQueueReceiver receiver, IInnerServerMessage message)
    {
        if (_syncHandlers.TryGetValue(message.GetType(), out var handler))
            return handler.Handle(receiver, message);
        return true;
    }

    public bool IsAsync(IInnerServerMessage message) => _asyncHandlers.ContainsKey(message.GetType());
}

