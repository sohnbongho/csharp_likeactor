using Library.ContInfo;
using Library.Logger;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Server.Acceptor;

public class TCPAcceptor : IDisposable
{
    private readonly Socket _listener;
    private readonly int _maxConnections;
    private readonly int _maxPoolCount;
    private readonly SocketAsyncEventArgsPool _acceptArgsPool;
    private static readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();
    private volatile bool _stopping;
    private readonly List<SocketAsyncEventArgs> _allArgs = new();

    // IP별 최근 연결 시각 목록 (버스트 플러드 감지용)
    private readonly ConcurrentDictionary<string, Queue<DateTime>> _connectionTimestamps = new();
    private readonly ConcurrentDictionary<string, DateTime> _bannedIps = new();
    private readonly HashSet<string> _allowlistedIps;

    // 만료된 IP 엔트리 정리 주기. IP 로테이션 공격으로 dict가 무한 증식하는 것을 방지.
    private long _lastSweepTicks;
    private const int SweepIntervalSeconds = 60;

    public event Action<Socket>? OnAccepted;

    public TCPAcceptor(int port, int maxConnections = SessionConstInfo.MaxAcceptSessionCount)
    {
        _maxConnections = maxConnections;
        _maxPoolCount = _maxConnections * 2;
        _acceptArgsPool = new SocketAsyncEventArgsPool(_maxConnections);
        _allowlistedIps = new HashSet<string> { "127.0.0.1", "::1" };

        _listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _listener.Bind(new IPEndPoint(IPAddress.Any, port));
        _listener.Listen(SessionConstInfo.MaxListenerBackLog); // backlog
    }

    public void Init()
    {
        for (int i = 0; i < _maxPoolCount; i++)
        {
            var args = new SocketAsyncEventArgs();
            args.Completed += AcceptCompleted;
            _allArgs.Add(args);
            _acceptArgsPool.Push(args);
        }
    }

    public void Start()
    {
        _stopping = false;
        for (int i = 0; i < _maxConnections; i++)
        {
            StartAccept();
        }
    }

    private void StartAccept()
    {
        if (_stopping)
            return;

        if (!_acceptArgsPool.TryPop(out var args))
            return;

        if (args == null)
            return;

        args.AcceptSocket = null;

        try
        {
            bool pending = _listener.AcceptAsync(args);
            if (!pending)
            {
                ProcessAccept(args);
            }
        }
        catch (ObjectDisposedException)
        {
            // 리스너가 이미 종료된 경우 조용히 무시
        }
        catch (SocketException se) when (se.SocketErrorCode == SocketError.OperationAborted)
        {
            // 종료 과정에서 발생하는 abort 오류 무시
        }
    }

    private void AcceptCompleted(object? sender, SocketAsyncEventArgs e)
    {
        ProcessAccept(e);
    }

    private void ProcessAccept(SocketAsyncEventArgs e)
    {
        if (_stopping)
        {
            _acceptArgsPool.Push(e);
            return;
        }

        if (e.SocketError == SocketError.OperationAborted)
        {
            return;
        }

        if (e.SocketError == SocketError.Success && e.AcceptSocket != null)
        {
            var remoteAddress = (e.AcceptSocket.RemoteEndPoint as IPEndPoint)?.Address;
            var ip = remoteAddress != null ? NormalizeIp(remoteAddress) : null;

            if (ip == null || ShouldBlock(ip))
                e.AcceptSocket.Close();
            else
                OnAccepted?.Invoke(e.AcceptSocket);
        }
        else
        {
            _logger.Warn(() =>$"Fail Accept.: {e.SocketError}");
        }

        _acceptArgsPool.Push(e);
        StartAccept();
    }

    private static string NormalizeIp(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            return address.MapToIPv4().ToString();
        return address.ToString();
    }

    private bool ShouldBlock(string ip)
    {
        if (_allowlistedIps.Contains(ip)) return false;
        if (IsBanned(ip))
        {
            _logger.Warn(() => $"밴된 IP 접속 시도: {ip}");
            return true;
        }
        if (IsFloodDetected(ip))
        {
            RegisterBan(ip);
            return true;
        }
        return false;
    }

    private bool IsBanned(string ip)
    {
        if (!_bannedIps.TryGetValue(ip, out var expiry))
            return false;
        if (DateTime.UtcNow < expiry)
            return true;
        _bannedIps.TryRemove(ip, out _);
        return false;
    }

    private void RegisterBan(string ip)
    {
        _bannedIps[ip] = DateTime.UtcNow.AddMinutes(SessionConstInfo.BanDurationMinutes);
        _logger.Warn(() => $"플러드 감지, {SessionConstInfo.BanDurationMinutes}분 밴: {ip}");
    }

    private bool IsFloodDetected(string ip)
    {
        var now = DateTime.UtcNow;
        var windowStart = now.AddSeconds(-SessionConstInfo.FloodWindowSeconds);

        var timestamps = _connectionTimestamps.GetOrAdd(ip, _ => new Queue<DateTime>());

        bool flooded;
        lock (timestamps)
        {
            while (timestamps.Count > 0 && timestamps.Peek() < windowStart)
                timestamps.Dequeue();

            if (timestamps.Count >= SessionConstInfo.MaxConnectionsPerWindow)
                flooded = true;
            else
            {
                timestamps.Enqueue(now);
                flooded = false;
            }
        }

        SweepExpiredIfDue(now, windowStart);
        return flooded;
    }

    // 주기적으로 만료 엔트리를 dict에서 제거. 단일 스레드만 스위프를 실행하도록 CAS로 방어.
    private void SweepExpiredIfDue(DateTime now, DateTime windowStart)
    {
        var last = Volatile.Read(ref _lastSweepTicks);
        if (now.Ticks - last < TimeSpan.TicksPerSecond * SweepIntervalSeconds)
            return;

        if (Interlocked.CompareExchange(ref _lastSweepTicks, now.Ticks, last) != last)
            return; // 다른 스레드가 이미 스위프 중

        foreach (var kv in _connectionTimestamps)
        {
            var q = kv.Value;
            lock (q)
            {
                while (q.Count > 0 && q.Peek() < windowStart)
                    q.Dequeue();

                // 빈 큐는 dict에서 제거. 락을 계속 들고 있어 concurrent enqueue와 race 없음.
                if (q.Count == 0)
                    _connectionTimestamps.TryRemove(kv.Key, out _);
            }
        }

        // 만료된 밴 정리
        foreach (var kv in _bannedIps)
        {
            if (now >= kv.Value)
                _bannedIps.TryRemove(kv.Key, out _);
        }
    }

    public void Stop()
    {
        try
        {
            _stopping = true;
            _listener.Close();
        }
        catch (Exception ex)
        {
            _logger.Error(() => $"Exception Stop", ex);
        }
    }

    public void Dispose()
    {
        Stop();

        // 이벤트 해제 후 모든 args 일괄 Dispose
        foreach (var args in _allArgs)
        {
            args.Completed -= AcceptCompleted;
            args.Dispose();
        }
        _allArgs.Clear();
    }
}

