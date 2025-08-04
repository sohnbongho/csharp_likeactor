using Library.MessageQueue.Message;

namespace Library.MessageQueue;

public class MessageQueueWorkerManager : IAsyncDisposable
{
    private readonly MessageQueueWorker[] _workers;
    private readonly int _workerCount;

    public MessageQueueWorkerManager(int workerCount)
    {
        _workerCount = workerCount;
        _workers = new MessageQueueWorker[workerCount];

        for (int i = 0; i < workerCount; i++)
        {
            _workers[i] = new MessageQueueWorker();
        }
    }
    public void Start()
    {
        for (int i = 0; i < _workerCount; i++)
        {
            _workers[i].Start();
        }
    }
    public MessageQueueWorker GetWorker(ulong sessionId)
    {
        int index = GetWorkerIndex(sessionId);
        return _workers[index];
    }

    private int GetWorkerIndex(ulong sessionId)
    {
        return (int)(sessionId % (ulong)_workerCount);
    }
    

    public async Task EnqueueAsync(IMessageQueueReceiver user, ulong sessionId, IMessageQueue message)
    {
        int index = GetWorkerIndex(sessionId);
        await _workers[index].EnqueueAsync(user, message);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var worker in _workers)
        {
            await worker.DisposeAsync();
        }
    }    
}
