using Server.Model;
using System.Reflection;

namespace Server.Handler.InnerAttribute;

public interface IInnerMessageHandler
{
    bool Handle(IMessageReceiver receiver, IInnerServerMessage message);
}

public interface IInnerMessageHandlerAsync
{
    Task<bool> HandleAsync(IMessageReceiver receiver, IInnerServerMessage message);
}

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
}

