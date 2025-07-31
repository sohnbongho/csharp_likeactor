using Server.Model;

namespace Server.Handler.InnerAttribute;

public class InnerMessageDispatcher
{
    private readonly InnerMessageHandlerManager _handlerManager;
    private readonly Queue<(IMessageReceiver receiver, IInnerServerMessage message)> _queue = new();

    public InnerMessageDispatcher(InnerMessageHandlerManager handlerManager)
    {
        _handlerManager = handlerManager;
    }

    public void Enqueue(IMessageReceiver receiver, IInnerServerMessage message)
    {
        lock (_queue)
            _queue.Enqueue((receiver, message));
    }

    public async Task TickAsync()
    {
        Queue<(IMessageReceiver, IInnerServerMessage)> snapshot;

        lock (_queue)
        {
            snapshot = new Queue<(IMessageReceiver, IInnerServerMessage)>(_queue);
            _queue.Clear();
        }

        while (snapshot.Count > 0)
        {
            var (receiver, message) = snapshot.Dequeue();
            var type = message.GetType();

            //if (_handlerManager.TryGetAsync(type, out var asyncHandler))
            //{
            //    await asyncHandler.HandleAsync(receiver, message);
            //}
            //else if (_handlerManager.TryGetSync(type, out var handler))
            //{
            //    handler.Handle(receiver, message);
            //}            
        }
    }
}
