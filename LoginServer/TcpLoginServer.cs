using Library.Acceptor;
using Library.ContInfo;
using Library.Db;
using Library.Db.Broadcast;
using Library.Db.Cache;
using Library.Db.Sql;
using Library.Logger;
using Library.Network;
using Library.World;
using LoginServer.Actors;
using LoginServer.AdminApi;
using System.Diagnostics;

namespace LoginServer;

public class TcpLoginServer
{
    private readonly int _port;
    private readonly DbConfig _dbConfig;
    private readonly AdminApiConfig _adminApiConfig;
    private readonly string _authTokenPrefix;
    private readonly int _authTokenTtlSeconds;
    private static readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();
    private readonly UserObjectPoolManager _userObjectPoolManager;
    private readonly LobbyThreadManager _lobbyThreadManager;
    private readonly TCPAcceptor _acceptor;
    private readonly SqlWorkerManager _sqlWorkerManager;
    private readonly CacheWorkerManager _cacheWorkerManager;
    private readonly RedisBroadcastManager _broadcastManager;
    private readonly AdminApiHost _adminApiHost = new();
    private readonly ManualResetEvent _shutdownEvent = new(false);
    private readonly CancellationTokenSource _monitorCts = new();
    private Task? _monitorTask;

    public TcpLoginServer(int port, DbConfig dbConfig, AdminApiConfig adminApiConfig, string authTokenPrefix, int authTokenTtlSeconds)
    {
        _port = port;
        _dbConfig = dbConfig;
        _adminApiConfig = adminApiConfig;
        _authTokenPrefix = authTokenPrefix;
        _authTokenTtlSeconds = authTokenTtlSeconds;
        _lobbyThreadManager = new LobbyThreadManager();
        _sqlWorkerManager = new SqlWorkerManager(dbConfig);
        _cacheWorkerManager = new CacheWorkerManager(dbConfig);
        _userObjectPoolManager = new UserObjectPoolManager(
            _lobbyThreadManager, _sqlWorkerManager,
            _cacheWorkerManager.GetDatabase(), authTokenPrefix, authTokenTtlSeconds);
        _acceptor = new TCPAcceptor(port);
        _broadcastManager = new RedisBroadcastManager(
            _cacheWorkerManager.GetSubscriber(), dbConfig.RedisBroadcastChannel);
    }

    public void Init()
    {
        _sqlWorkerManager.Start();
        _cacheWorkerManager.Start();
        _broadcastManager.Subscribe(msg =>
        {
            _logger.Info(() => $"[공지 수신] {msg}");
        });

        _lobbyThreadManager.Start();
        _userObjectPoolManager.Init();
        _acceptor.OnAccepted += socket =>
        {
            _userObjectPoolManager.AcceptUser(socket);
        };
        _acceptor.Init();
        _monitorTask = Task.Run(() => MonitorAsync(_monitorCts.Token));

        _adminApiHost.StartAsync(
            _adminApiConfig, _dbConfig,
            _userObjectPoolManager, _sqlWorkerManager, _cacheWorkerManager, _broadcastManager
        ).GetAwaiter().GetResult();
    }

    public void Start()
    {
        _logger.Info(() => $"LoginServer Start Listen Port:{_port}...");
        _acceptor.Start();
        _shutdownEvent.WaitOne();
    }

    public void Stop()
    {
        _logger.Info(() => $"Stop LoginServer");

        _adminApiHost.StopAsync().GetAwaiter().GetResult();

        _acceptor.Dispose();
        _userObjectPoolManager.ShutdownAll();

        _lobbyThreadManager.StopAsync().GetAwaiter().GetResult();

        _broadcastManager.Unsubscribe();
        _sqlWorkerManager.StopAsync().GetAwaiter().GetResult();
        _cacheWorkerManager.StopAsync().GetAwaiter().GetResult();

        _monitorCts.Cancel();
        _monitorTask?.GetAwaiter().GetResult();

        _shutdownEvent.Set();
    }

    private async Task MonitorAsync(CancellationToken token)
    {
        var process = Process.GetCurrentProcess();
        var prevCpuTime = process.TotalProcessorTime;
        var prevTick = Stopwatch.GetTimestamp();
        var (prevRecv, prevSent) = PacketStats.Snapshot();

        while (!token.IsCancellationRequested)
        {
            try { await Task.Delay(10000, token); }
            catch (OperationCanceledException) { break; }

            process.Refresh();
            var currCpuTime = process.TotalProcessorTime;
            var currTick = Stopwatch.GetTimestamp();
            var (currRecv, currSent) = PacketStats.Snapshot();

            var elapsedSec = (currTick - prevTick) / (double)Stopwatch.Frequency;
            var cpuPercent = (currCpuTime - prevCpuTime).TotalSeconds
                             / (elapsedSec * Environment.ProcessorCount) * 100.0;
            var memoryMb = process.WorkingSet64 / 1024.0 / 1024.0;
            var sessions = _userObjectPoolManager.ActiveSessionCount;
            var recvDelta = currRecv - prevRecv;
            var sentDelta = currSent - prevSent;

            _logger.Info(() =>
                $"[모니터] 동접: {sessions}명 | CPU: {cpuPercent:F1}% | 메모리: {memoryMb:F0}MB | 수신: {recvDelta}패킷/10s | 송신: {sentDelta}패킷/10s");

            prevCpuTime = currCpuTime;
            prevTick = currTick;
            prevRecv = currRecv;
            prevSent = currSent;
        }
    }
}
