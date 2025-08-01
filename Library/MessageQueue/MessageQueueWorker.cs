using Library.ContInfo;
using Library.Logger;
using Library.MessageQueue.Message;
using System.Threading.Channels;

namespace Library.MessageQueue;

public class MessageQueueWorker : IAsyncDisposable
{
    private readonly Channel<(IMessageQueueReceiver receiver, IMessageQueue message)> _queue;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _processingTask;
    private readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();

    public MessageQueueWorker()
    {
        _queue = Channel.CreateUnbounded<(IMessageQueueReceiver, IMessageQueue)>();
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

                await Task.Delay(ThreadConstInfo.MessageQueueThreadDelay); // CPU 보호용
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
            await _processingTask;
        }
        catch (OperationCanceledException)
        {
        }

        _cts.Dispose();
    }
}

