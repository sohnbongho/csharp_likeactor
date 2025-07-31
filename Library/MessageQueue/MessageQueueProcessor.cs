using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Library.MessageQueue;

public class MessageQueueProcessor<T> where T : class
{
    private readonly MessageQueue<T> _queue = new();
    private readonly Dictionary<Type, Action<T>> _handlers = new();

    public void Enqueue(T msg) => _queue.Enqueue(msg);

    public void Tick()
    {
        while (_queue.TryDequeue(out var msg))
        {
            if (msg == null)
                continue;

            if (_handlers.TryGetValue(msg.GetType(), out var handler))
                handler(msg);
        }
    }

    public void RegisterHandler<TMsg>(Action<TMsg> handler) where TMsg : T
    {
        _handlers[typeof(TMsg)] = msg => handler((TMsg)msg);
    }
}
