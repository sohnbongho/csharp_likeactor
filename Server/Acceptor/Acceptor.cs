using Library.ContInfo;
using Library.Logger;
using System.Net;
using System.Net.Sockets;

namespace Server.Acceptor;

public class TCPAcceptor : IDisposable
{
    private readonly Socket _listener;
    private readonly int _maxConnections;
    private readonly int _maxPoolCount;
    private readonly SocketAsyncEventArgsPool _acceptArgsPool;
    private readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();
    private volatile bool _stopping;
    private readonly List<SocketAsyncEventArgs> _allArgs = new();

    public event Action<Socket>? OnAccepted;

    public TCPAcceptor(int port, int maxConnections = SessionConstInfo.MaxAcceptSessionCount)
    {
        _maxConnections = maxConnections;
        _maxPoolCount = _maxConnections * 2;
        _acceptArgsPool = new SocketAsyncEventArgsPool(_maxConnections);

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
            OnAccepted?.Invoke(e.AcceptSocket);
        }
        else
        {
            _logger.Warn(() =>$"Fail Accept.: {e.SocketError}");
        }

        _acceptArgsPool.Push(e);
        StartAccept();
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

