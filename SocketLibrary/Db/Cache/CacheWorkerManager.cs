using StackExchange.Redis;

namespace Library.Db.Cache;

public class CacheWorkerManager
{
    private readonly CacheWorker[] _workers;
    private readonly ConnectionMultiplexer _multiplexer;
    private long _roundRobinIndex;

    public CacheWorkerManager(DbConfig config)
    {
        var options = ConfigurationOptions.Parse(config.RedisConnectionString);
        options.AbortOnConnectFail = false;
        _multiplexer = ConnectionMultiplexer.Connect(options);

        _workers = new CacheWorker[config.CacheWorkerCount];
        for (int i = 0; i < config.CacheWorkerCount; i++)
            _workers[i] = new CacheWorker(_multiplexer.GetDatabase(), config.CacheChannelCapacity);
    }

    public void Start()
    {
        foreach (var w in _workers) w.Start();
    }

    public async Task StopAsync()
    {
        foreach (var w in _workers) await w.StopAsync();
        await _multiplexer.CloseAsync();
        _multiplexer.Dispose();
    }

    // false 반환 시 채널 만석 → 호출자가 클라이언트에 에러 응답 처리
    public bool Enqueue(ICacheRequest request)
    {
        var idx = (int)((Interlocked.Increment(ref _roundRobinIndex) & long.MaxValue) % _workers.Length);
        return _workers[idx].TryEnqueue(request);
    }

    public async Task<bool> CheckConnectionAsync()
    {
        try
        {
            await _multiplexer.GetDatabase().PingAsync();
            return true;
        }
        catch { return false; }
    }

    // RedisBroadcastManager와 ConnectionMultiplexer 공유
    public ISubscriber GetSubscriber() => _multiplexer.GetSubscriber();

    // AdminApi 등 직접 Redis I/O가 필요한 곳에서 사용
    public IDatabase GetDatabase() => _multiplexer.GetDatabase();
}
