using Library.ContInfo;
using Library.Logger;
using Library.MessageQueue.Message;
using System.Threading.Channels;

namespace Library.MessageQueue;

public class MessageQueueWorker : IAsyncDisposable
{
    private readonly Channel<(IMessageQueueReceiver receiver, IMessageQueue message)> _queue;
    private readonly CancellationTokenSource _cts = new();
    private Task? _processingTask = null;
    private readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();

    public MessageQueueWorker()
    {
        _queue = Channel.CreateUnbounded<(IMessageQueueReceiver, IMessageQueue)>();        
    }
    public void Start()
    {
        _processingTask = Task.Run(ProcessLoopAsync);
    }

    public async Task<bool> EnqueueAsync(IMessageQueueReceiver receiver, IMessageQueue message)
    {
        await _queue.Writer.WriteAsync((receiver, message));
        return true;
    }

    private async Task ProcessLoopAsync()
    {
        try
        {
            // WaitToReadAsync가 아이템이 없을 때 자연스럽게 대기하므로 busy-spin 걱정 없음.
            // 예전 Task.Delay(10)은 순수 레이턴시 오버헤드라 제거.
            while (await _queue.Reader.WaitToReadAsync(_cts.Token))
            {
                while (_queue.Reader.TryRead(out var queue))
                {
                    var (receiver, message) = queue;
                    try
                    {
                        await receiver.OnRecvMessageAsync(message);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(() => $"Fail QueueWorker ", ex);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Info(() => "End QueueWorker ");
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _queue.Writer.Complete();

        try
        {
            if(_processingTask != null)
                await _processingTask;
        }
        catch (OperationCanceledException)
        {
        }

        _cts.Dispose();
    }

    
}

