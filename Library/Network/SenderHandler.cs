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
        while (true)
        {
            // 로컬 변수로 캡처 — 이후 Dispose()가 _socket을 null로 바꿔도 NullReferenceException 방지
            var socket = _socket;
            if (socket == null)
            {
                Interlocked.Exchange(ref _isSending, 0);
                return false;
            }

            if (!_pendingSendQueue.TryDequeue(out var message))
            {
                // 큐가 비었으므로 sending 플래그를 내린다.
                Interlocked.Exchange(ref _isSending, 0);

                // Lost Wakeup 방지: 플래그를 내린 직후 다른 스레드가 Enqueue + CAS를 시도했다가
                // 아직 1이던 플래그를 보고 빠져나갔을 수 있다. 큐를 재확인해서 있으면 재시도.
                if (_pendingSendQueue.IsEmpty)
                    return true;
                if (Interlocked.CompareExchange(ref _isSending, 1, 0) != 0)
                    return true; // 다른 스레드가 이미 sending을 잡음
                continue;
            }

            try
            {
                // MemoryStream/ToArray/BitConverter.GetBytes 할당을 피하고 _sendBuffer에 직접 기록한다.
                int bodyLength = message.CalculateSize();

                if (bodyLength > SessionConstInfo.MaxMessageBodySize)
                {
                    _logger.Warn(() => $"메시지 크기 초과 ({bodyLength} bytes, 최대 {SessionConstInfo.MaxMessageBodySize}), 해당 메시지 드롭");
                    // 파이프라인 정지 방지: 크기 초과 메시지는 드롭하고 다음 메시지를 계속 처리한다.
                    continue;
                }

                // 2바이트 little-endian 길이 헤더
                _sendBuffer[0] = (byte)(bodyLength & 0xFF);
                _sendBuffer[1] = (byte)((bodyLength >> 8) & 0xFF);

                // protobuf 직렬화 결과를 바로 _sendBuffer의 [2..]에 기록 (Span 기반 zero-alloc)
                message.WriteTo(_sendBuffer.AsSpan(2, bodyLength));

                _sendEventArgs.SetBuffer(_sendBuffer, 0, 2 + bodyLength);

                if (!socket.SendAsync(_sendEventArgs))
                {
                    // 동기 완료 — OnSendCompleted가 다시 ProcessSendQueue를 호출하므로 여기서 종료
                    OnSendCompleted(socket, _sendEventArgs);
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(() => $"SendAsync Error", ex);
                Interlocked.Exchange(ref _isSending, 0);
                return false;
            }
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

