namespace Library.Db;

public sealed class DbConfig
{
    public string MySqlConnectionString { get; init; } = "";
    public string RedisConnectionString { get; init; } = "";
    public string RedisBroadcastChannel  { get; init; } = "server:notice";
    public int    SqlWorkerCount         { get; init; } = 8;
    public int    SqlChannelCapacity     { get; init; } = 500;
    public int    CacheWorkerCount       { get; init; } = 4;
    public int    CacheChannelCapacity   { get; init; } = 1000;
    public int    RetryBaseDelayMs       { get; init; } = 500;
    public int    RetryMaxDelayMs        { get; init; } = 30000;
}
