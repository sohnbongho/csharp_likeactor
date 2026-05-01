using Library.Logger;
using StackExchange.Redis;

namespace Library.Db.Broadcast;

public class RedisBroadcastManager
{
    private static readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();
    private readonly ISubscriber _subscriber;
    private readonly RedisChannel _channel;

    public RedisBroadcastManager(ISubscriber subscriber, string channel)
    {
        _subscriber = subscriber;
        _channel = new RedisChannel(channel, RedisChannel.PatternMode.Literal);
    }

    public void Subscribe(Action<string> onReceived)
    {
        _subscriber.Subscribe(_channel, (_, msg) =>
        {
            try { onReceived(msg.ToString()); }
            catch (Exception ex) { _logger.Error(() => "브로드캐스트 수신 처리 실패", ex); }
        });

        _logger.Info(() => $"Redis Pub/Sub 구독 시작: channel={_channel}");
    }

    public async Task PublishAsync(string message)
    {
        await _subscriber.PublishAsync(_channel, message);
    }

    public void Unsubscribe()
    {
        _subscriber.Unsubscribe(_channel);
    }
}
