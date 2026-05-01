using MySqlConnector;

namespace Library.Db.Sql;

public class SqlWorkerManager
{
    private readonly SqlWorker[] _workers;
    private readonly string _connectionString;
    private long _roundRobinIndex;
    private readonly CancellationTokenSource _cts = new();

    public SqlWorkerManager(DbConfig config)
    {
        _connectionString = config.MySqlConnectionString;
        _workers = new SqlWorker[config.SqlWorkerCount];
        for (int i = 0; i < config.SqlWorkerCount; i++)
            _workers[i] = new SqlWorker(config, _cts.Token);
    }

    public async Task<bool> CheckConnectionAsync()
    {
        try
        {
            await using var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync();
            return true;
        }
        catch { return false; }
    }

    public void Start()
    {
        foreach (var w in _workers) w.Start();
    }

    public async Task StopAsync()
    {
        _cts.Cancel();
        foreach (var w in _workers) await w.StopAsync();
    }

    // false 반환 시 채널 만석 → 호출자가 클라이언트에 에러 응답 처리
    public bool Enqueue(ISqlRequest request)
    {
        var idx = (int)((Interlocked.Increment(ref _roundRobinIndex) & long.MaxValue) % _workers.Length);
        return _workers[idx].TryEnqueue(request);
    }
}
