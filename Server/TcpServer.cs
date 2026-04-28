using Library.ContInfo;
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
    private readonly ManualResetEvent _shutdownEvent = new(false);
    private readonly CancellationTokenSource _monitorCts = new();
    private Task? _monitorTask;

    public TcpServer(int port)
    {
        _port = port;
        _lobbyThreadManager = new LobbyThreadManager();
        _worldThreadManager = new WorldThreadManager();
        _userObjectPoolManager = new UserObjectPoolManager(_lobbyThreadManager, _worldThreadManager);
        _acceptor = new TCPAcceptor(port);
    }

    public void Init()
    {
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

        _monitorCts.Cancel();
        _monitorTask?.GetAwaiter().GetResult();

        _shutdownEvent.Set();
    }

    private async Task MonitorAsync(CancellationToken token)
    {
        var process = Process.GetCurrentProcess();
        var prevCpuTime = process.TotalProcessorTime;
        var prevTick = Stopwatch.GetTimestamp();

        while (!token.IsCancellationRequested)
        {
            try { await Task.Delay(10000, token); }
            catch (OperationCanceledException) { break; }

            process.Refresh();
            var currCpuTime = process.TotalProcessorTime;
            var currTick = Stopwatch.GetTimestamp();

            var elapsedSec = (currTick - prevTick) / (double)Stopwatch.Frequency;
            var cpuPercent = (currCpuTime - prevCpuTime).TotalSeconds
                             / (elapsedSec * Environment.ProcessorCount) * 100.0;
            var memoryMb = process.WorkingSet64 / 1024.0 / 1024.0;
            var sessions = _userObjectPoolManager.ActiveSessionCount;

            _logger.Info(() =>
                $"[모니터] 동접: {sessions}명 | CPU: {cpuPercent:F1}% | 메모리: {memoryMb:F0}MB");

            prevCpuTime = currCpuTime;
            prevTick = currTick;
        }
    }
}
