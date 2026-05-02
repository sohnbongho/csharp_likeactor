using Library.ContInfo;
using Library.Db;
using Library.Db.Broadcast;
using Library.Db.Cache;
using Library.Db.Sql;
using Library.Logger;
using Library.World;
using Server.Acceptor;
using Server.Actors;
using System.Diagnostics;

namespace Server;

public class TcpServer
{
    private readonly int _port;
    private static readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();
    private readonly UserObjectPoolManager _userObjectPoolManager;
    private readonly LobbyThreadManager _lobbyThreadManager;
    private readonly WorldThreadManager _worldThreadManager;
    private readonly TCPAcceptor _acceptor;
    private readonly SqlWorkerManager _sqlWorkerManager;
    private readonly CacheWorkerManager _cacheWorkerManager;
    private readonly RedisBroadcastManager _broadcastManager;
    private readonly ManualResetEvent _shutdownEvent = new(false);
    private readonly CancellationTokenSource _monitorCts = new();
    private Task? _monitorTask;

    public TcpServer(int port, DbConfig dbConfig)
    {
        _port = port;
        _lobbyThreadManager = new LobbyThreadManager();
        _worldThreadManager = new WorldThreadManager();
        _sqlWorkerManager = new SqlWorkerManager(dbConfig);
        _userObjectPoolManager = new UserObjectPoolManager(_lobbyThreadManager, _worldThreadManager, _sqlWorkerManager);
        _acceptor = new TCPAcceptor(port);
        _cacheWorkerManager = new CacheWorkerManager(dbConfig);
        _broadcastManager = new RedisBroadcastManager(
            _cacheWorkerManager.GetSubscriber(), dbConfig.RedisBroadcastChannel);
    }

    public void Init()
    {
        _sqlWorkerManager.Start();
        _cacheWorkerManager.Start();
        _broadcastManager.Subscribe(msg =>
        {
            // TODO: ServerNotice proto 추가 후 아래 코드 활성화
            // _userObjectPoolManager.BroadcastAll(new Messages.MessageWrapper
            //     { ServerNotice = new Messages.ServerNotice { Message = msg } });
            _logger.Info(() => $"[공지 수신] {msg}");
        });

        _lobbyThreadManager.Start();
        _worldThreadManager.Start();
        _userObjectPoolManager.Init();
        _acceptor.OnAccepted += socket =>
        {
            _userObjectPoolManager.AcceptUser(socket);
        };
        _acceptor.Init();
        _monitorTask = Task.Run(() => MonitorAsync(_monitorCts.Token));
    }

    public void Start()
    {
        var mysqlOk = _sqlWorkerManager.CheckConnectionAsync().GetAwaiter().GetResult();
        var redisOk  = _cacheWorkerManager.CheckConnectionAsync().GetAwaiter().GetResult();
        _logger.Info(() => $"[DB Connect] MySQL: {(mysqlOk ? "Conneted" : "DisConnected")} | Redis: {(redisOk ? "Conneted" : "DisConnected")}");

        _logger.Info(() => $"Server Start Listen Port:{_port}...");
        _acceptor.Start();
        _shutdownEvent.WaitOne();
    }

    public void Stop()
    {
        _logger.Info(() => $"Stop Server");

        _acceptor.Dispose();
        _userObjectPoolManager.ShutdownAll();

        _lobbyThreadManager.StopAsync().GetAwaiter().GetResult();
        _worldThreadManager.StopAllAsync().GetAwaiter().GetResult();

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
        var (prevRecv, prevSent) = Library.Network.PacketStats.Snapshot();

        while (!token.IsCancellationRequested)
        {
            try { await Task.Delay(10000, token); }
            catch (OperationCanceledException) { break; }

            process.Refresh();
            var currCpuTime = process.TotalProcessorTime;
            var currTick = Stopwatch.GetTimestamp();
            var (currRecv, currSent) = Library.Network.PacketStats.Snapshot();

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
