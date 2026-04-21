using Google.Protobuf;
using Library.ContInfo;
using Library.Logger;
using Messages;
using System.Collections.Concurrent;
using System.Net.Sockets;

namespace Library.Network;

public class SenderHandler : IDisposable
{
    private readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();
    private Socket? _socket;
    private Action? _onDisconnect;
    private bool _disposed;
    private readonly SocketAsyncEventArgs _sendEventArgs;
    private readonly byte[] _sendBuffer = new byte[SessionConstInfo.MaxBufferSize];
    private readonly ConcurrentQueue<MessageWrapper> _pendingSendQueue = new();
    private int _isSending = 0; // 0: idle, 1: sending

    public SenderHandler()
    {
        _sendEventArgs = new SocketAsyncEventArgs();
        _sendEventArgs.Completed += OnSendCompleted;
    }
    public void Bind(Socket? socket, Action? onDisconnect = null)
    {
        _socket = socket;
        _onDisconnect = onDisconnect;
    }
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _sendEventArgs.Completed -= OnSendCompleted;

        _socket = null;
        _onDisconnect = null;
        while (_pendingSendQueue.TryDequeue(out _)) { }
        Interlocked.Exchange(ref _isSending, 0);

        _sendEventArgs.Dispose();
    }

    public bool Send(MessageWrapper message)
    {
        if (_pendingSendQueue.Count >= SessionConstInfo.MaxSendQueueSize)
        {
            _logger.Warn(() => $"송신 큐 초과 ({SessionConstInfo.MaxSendQueueSize}), 세션 강제 종료");
            _onDisconnect?.Invoke();
            return false;
        }

        _pendingSendQueue.Enqueue(message);
        if (Interlocked.CompareExchange(ref _isSending, 1, 0) == 1)
            return true; // 이미 처리 중이면 큐에만 넣음

        return ProcessSendQueue();


    }
    private bool ProcessSendQueue()
    {
        if (_socket == null)
        {
            Interlocked.Exchange(ref _isSending, 0);
            return false;
        }

        if (!_pendingSendQueue.TryDequeue(out var message))
        {
            Interlocked.Exchange(ref _isSending, 0);
            return true;
        }

        try
        {
            using var ms = new MemoryStream();
            message.WriteTo(ms);
            var body = ms.ToArray();

            if (body.Length > SessionConstInfo.MaxMessageBodySize)
            {
                _logger.Warn(() => $"메시지 크기 초과 ({body.Length} bytes, 최대 {SessionConstInfo.MaxMessageBodySize}), 해당 메시지 드롭");
                Interlocked.Exchange(ref _isSending, 0);
                return false;
            }

            ushort bodyLength = (ushort)body.Length; // 위에서 검증했으므로 안전

            // length prefix
            var lengthBytes = BitConverter.GetBytes(bodyLength);
            Buffer.BlockCopy(lengthBytes, 0, _sendBuffer, 0, 2);
            Buffer.BlockCopy(body, 0, _sendBuffer, 2, bodyLength);

            _sendEventArgs.SetBuffer(_sendBuffer, 0, 2 + bodyLength);

            if (!_socket.SendAsync(_sendEventArgs))
            {
                OnSendCompleted(_socket, _sendEventArgs);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(() => $"SendAsync Error", ex);
            return false;
        }
    }

    private void OnSendCompleted(object? sender, SocketAsyncEventArgs e)
    {
        if (e.SocketError != SocketError.Success)
        {
            _logger.Info(() => "Send Error");
            Interlocked.Exchange(ref _isSending, 0);
            _onDisconnect?.Invoke();
            return;
        }
        // 다음 메시지 처리
        ProcessSendQueue();
    }
}

