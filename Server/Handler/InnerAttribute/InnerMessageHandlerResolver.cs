using Server.Model;
using System.Reflection;

namespace Server.Handler.InnerAttribute;

public interface IInnerMessageHandler
{
    Task HandleAsync(IInnerServerMessage message);
}

public class InnerMessageHandlerResolver
{
    private readonly Dictionary<Type, IInnerMessageHandler> _handlers = new();

    public InnerMessageHandlerResolver()
    {
        RegisterHandlers();
    }

    private void RegisterHandlers()
    {
        var types = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .Where(t => typeof(IInnerMessageHandler).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);

        foreach (var type in types)
        {
            var attr = type.GetCustomAttribute<InnerMessageHandlerAttribute>();
            if (attr != null)
            {
                var instance = (IInnerMessageHandler)Activator.CreateInstance(type)!;
                _handlers[attr.MessageType] = instance;
            }
        }
    }

    public async Task<bool> HandleAsync(IInnerServerMessage message)
    {
        var type = message.GetType();
        if (_handlers.TryGetValue(type, out var handler))
        {
            await handler.HandleAsync(message);
            return true;
        }

        Console.WriteLine($"[경고] 핸들러 없음: {type.Name}");
        return false;
    }
}

