using Google.Protobuf;
using Library.ContInfo;
using Library.Logger;
using Messages;
using System.Collections.Concurrent;
using System.Net.Sockets;

namespace Library.Network;

public class SenderHandler : IDisposable
{
    private static readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();
    private Socket? _socket;
    private Action? _onDisconnect;
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

    // 세션 종료 후 pool 반환 직전 호출. SAEA/버퍼 등 재사용 자원은 보존한다.
    // _socket=null이면 ProcessSendQueue가 자동으로 조기 종료하므로 별도 플래그 불필요.
    public void Reset()
    {
        _socket = null;
        _onDisconnect = null;
        while (_pendingSendQueue.TryDequeue(out _)) { }
        Interlocked.Exchange(ref _isSending, 0);
    }

    // 최종 해제: pool 자체가 파기될 때만 호출.
    public void Dispose()
    {
        Reset();
        _sendEventArgs.Completed -= OnSendCompleted;
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
                if (!TrySerializeToBuffer(message, out int frameSize))
                    continue;

                _sendEventArgs.SetBuffer(_sendBuffer, 0, frameSize);

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

    // 메시지를 _sendBuffer에 직렬화한다. 성공하면 true와 프레임 전체 크기(헤더+바디)를 반환.
    // 크기 초과 시 경고 후 false를 반환해 해당 메시지를 드롭한다.
    private bool TrySerializeToBuffer(MessageWrapper message, out int frameSize)
    {
        int bodyLength = message.CalculateSize();
        if (bodyLength > SessionConstInfo.MaxMessageBodySize)
        {
            _logger.Warn(() => $"메시지 크기 초과 ({bodyLength} bytes, 최대 {SessionConstInfo.MaxMessageBodySize}), 해당 메시지 드롭");
            frameSize = 0;
            return false;
        }

        _sendBuffer[0] = (byte)(bodyLength & 0xFF);
        _sendBuffer[1] = (byte)((bodyLength >> 8) & 0xFF);
        message.WriteTo(_sendBuffer.AsSpan(2, bodyLength));

        frameSize = 2 + bodyLength;
        return true;
    }

    private void OnSendCompleted(object? sender, SocketAsyncEventArgs e)
    {
        // 세션 교체 후 들어온 stale 완료 콜백 차단:
        // sender는 SendAsync를 호출했던 당시의 Socket. 현재 _socket과 다르면 구 세션의 완료.
        // Reset으로 _socket=null이 된 뒤(또는 새 세션이 Bind로 다른 socket을 할당한 뒤) 호출되는 경우를 모두 커버.
        if (!ReferenceEquals(sender, _socket))
            return;

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
