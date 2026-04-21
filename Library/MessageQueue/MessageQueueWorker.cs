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
        // SingleReader=true: 소비자는 ProcessLoopAsync 하나 → Channel이 lock-free 최적화 경로 사용.
        // AllowSynchronousContinuations=false: 생산자 스레드가 소비자 연속을 인라인으로 돌려 받는 상황 방지.
        _queue = Channel.CreateUnbounded<(IMessageQueueReceiver, IMessageQueue)>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                AllowSynchronousContinuations = false,
            });
    }
    public void Start()
    {
        _processingTask = Task.Run(ProcessLoopAsync);
    }

    public ValueTask<bool> EnqueueAsync(IMessageQueueReceiver receiver, IMessageQueue message)
    {
        // Unbounded 채널은 TryWrite가 즉시 성공 → 대부분의 경우 async state machine 할당을 피한다.
        if (_queue.Writer.TryWrite((receiver, message)))
            return new ValueTask<bool>(true);

        return EnqueueSlowAsync(receiver, message);
    }

    private async ValueTask<bool> EnqueueSlowAsync(IMessageQueueReceiver receiver, IMessageQueue message)
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

