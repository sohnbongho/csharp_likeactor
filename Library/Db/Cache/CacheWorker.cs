using Library.Logger;
using Library.MessageQueue.Message;
using StackExchange.Redis;
using System.Threading.Channels;

namespace Library.Db.Cache;

public class CacheWorker
{
    private static readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();
    private readonly Channel<ICacheRequest> _channel;
    private readonly IDatabase _redis;
    private Task? _task;

    public CacheWorker(IDatabase redis, int channelCapacity)
    {
        _redis = redis;
        _channel = Channel.CreateBounded<ICacheRequest>(new BoundedChannelOptions(channelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true
        });
    }

    public bool TryEnqueue(ICacheRequest request) => _channel.Writer.TryWrite(request);

    public void Start() => _task = Task.Run(RunAsync);

    public async Task StopAsync()
    {
        _channel.Writer.Complete();
        if (_task != null) await _task;
    }

    private async Task RunAsync()
    {
        await foreach (var request in _channel.Reader.ReadAllAsync())
        {
            try
            {
                await request.ExecuteAsync(_redis);
            }
            catch (Exception ex)
            {
                _logger.Error(() => "CacheWorker 실행 실패", ex);
                if (request.Session != null)
                    await request.Session.EnqueueMessageAsync(
                        new InnerReceiveMessage { Message = new DbErrorMessage() });
            }
        }
    }
}
