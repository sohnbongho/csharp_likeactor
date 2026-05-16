using Library.Logger;
using Library.MessageQueue.Message;
using MySqlConnector;
using System.Threading.Channels;

namespace Library.Db.Sql;

public class SqlWorker
{
    private static readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();
    private readonly Channel<ISqlRequest> _channel;
    private readonly string _connectionString;
    private readonly DbConfig _config;
    private readonly CancellationToken _token;
    private Task? _task;

    public SqlWorker(DbConfig config, CancellationToken token)
    {
        _config = config;
        _connectionString = config.MySqlConnectionString;
        _token = token;
        _channel = Channel.CreateBounded<ISqlRequest>(new BoundedChannelOptions(config.SqlChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true
        });
    }

    public bool TryEnqueue(ISqlRequest request) => _channel.Writer.TryWrite(request);

    public void Start() => _task = Task.Run(RunAsync);

    public async Task StopAsync()
    {
        _channel.Writer.Complete();
        if (_task != null)
        {
            try { await _task; }
            catch (OperationCanceledException) { }
        }
    }

    private async Task RunAsync()
    {
        try
        {
            await foreach (var request in _channel.Reader.ReadAllAsync(_token))
                await ProcessRequestAsync(request);
        }
        catch (OperationCanceledException) { }
    }

    private async Task ProcessRequestAsync(ISqlRequest request)
    {
        int attempt = 0;
        while (true)
        {
            try
            {
                await using var conn = new MySqlConnection(_connectionString);
                await conn.OpenAsync(_token);
                await request.ExecuteAsync(conn);
                return;
            }
            catch (Exception ex)
            {
                _logger.Error(() => $"SqlWorker 실행 실패 (attempt={attempt}, critical={request.IsCritical})", ex);

                if (!request.IsCritical)
                {
                    if (request.Session != null)
                        await request.Session.EnqueueMessageAsync(
                            new InnerReceiveMessage { Message = new DbErrorMessage() });
                    return;
                }

                if (_token.IsCancellationRequested)
                {
                    _logger.Warn(() => "서버 종료로 중요 DB 요청 재시도 중단");
                    return;
                }

                var delayMs = Math.Min(
                    _config.RetryBaseDelayMs * (1 << Math.Min(attempt, 10)),
                    _config.RetryMaxDelayMs);

                try { await Task.Delay(delayMs, _token); }
                catch (OperationCanceledException)
                {
                    _logger.Warn(() => "서버 종료로 중요 DB 요청 재시도 중단");
                    return;
                }

                attempt++;
            }
        }
    }
}
