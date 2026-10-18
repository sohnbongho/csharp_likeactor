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

    // Bind 전에는 null, 이후에는 다음 Bind까지 교체하지 않는다.
    // 종료 시에도 null로 만들지 않는다. 닫힘 여부는 IsClosed로 판단한다.
    private UserConnectionComponent? _connection;
    private readonly Action _onDisconnect;
    private readonly SocketAsyncEventArgs _sendEventArgs;
    private readonly byte[] _sendBuffer = new byte[SessionConstInfo.MaxBufferSize];
    private readonly ConcurrentQueue<MessageWrapper> _pendingSendQueue = new();

    // 0: idle, 1: sending. _sendEventArgs에 SendAsync가 걸려 있는 동안 반드시 1을 유지한다.
    // 소켓을 Close해도 pending SendAsync는 IOCP에서 비동기로 완료되므로, Reset/Bind에서 이 값을
    // 강제로 0으로 내리면 안 된다. 완료 콜백(OnSendCompleted)만이 0으로 내린다.
    private int _isSending;

    public SenderHandler(Action onDisconnect)
    {
        _onDisconnect = onDisconnect;
        _sendEventArgs = new SocketAsyncEventArgs();
        _sendEventArgs.Completed += OnSendCompleted;
    }

    public void Bind(UserConnectionComponent connection)
    {
        Reset();
        _connection = connection;
    }

    // 세션 종료 시 호출. 남은 큐만 비운다. 소켓과 sending 플래그는 건드리지 않는다.
    public void Reset()
    {
        while (_pendingSendQueue.TryDequeue(out _)) { }
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
        var conn = _connection;
        if (conn == null || conn.IsClosed)
            return false;

        if (_pendingSendQueue.Count >= SessionConstInfo.MaxSendQueueSize)
        {
            _logger.Warn(() => $"송신 큐 초과 ({SessionConstInfo.MaxSendQueueSize}), 세션 강제 종료");
            _onDisconnect();
            return false;
        }

        _pendingSendQueue.Enqueue(message);
        TryStartSending();
        return true;
    }

    // sending 플래그를 잡는 데 성공한 경우에만 큐 처리를 시작한다.
    private void TryStartSending()
    {
        if (_pendingSendQueue.IsEmpty)
            return;

        if (Interlocked.CompareExchange(ref _isSending, 1, 0) != 0)
            return; // 이미 처리 중이거나 구 소켓의 SendAsync 완료 대기 중

        ProcessSendQueue();
    }

    // 호출 전제: 현재 스레드가 _isSending 플래그를 보유하고 있다.
    private void ProcessSendQueue()
    {
        var conn = _connection;
        if (conn == null || conn.IsClosed)
        {
            Interlocked.Exchange(ref _isSending, 0);
            return;
        }

        while (true)
        {
            if (!_pendingSendQueue.TryDequeue(out var message))
            {
                // 큐가 비었으므로 sending 플래그를 내린다.
                Interlocked.Exchange(ref _isSending, 0);

                // Lost Wakeup 방지: 플래그를 내린 직후 다른 스레드가 Enqueue + CAS를 시도했다가
                // 아직 1이던 플래그를 보고 빠져나갔을 수 있다. 큐를 재확인해서 있으면 재시도.
                if (_pendingSendQueue.IsEmpty)
                    return;

                if (Interlocked.CompareExchange(ref _isSending, 1, 0) != 0)
                    return; // 다른 스레드가 이미 sending을 잡음

                continue;
            }

            try
            {
                if (!TrySerializeToBuffer(message, out int frameSize))
                    continue;

                PacketStats.IncrementSent();
                _sendEventArgs.SetBuffer(_sendBuffer, 0, frameSize);

                if (!conn.Socket.SendAsync(_sendEventArgs))
                {
                    // 동기 완료. OnSendCompleted가 다시 ProcessSendQueue를 호출하므로 여기서 종료
                    OnSendCompleted(conn.Socket, _sendEventArgs);
                }
                return;
            }
            catch (ObjectDisposedException)
            {
                // 세션이 이미 소켓을 닫음. 오류가 아니라 정상 종료 경로.
                // SendAsync가 예외를 던졌으므로 SAEA는 in-flight 상태가 아니다.
                Interlocked.Exchange(ref _isSending, 0);
                _onDisconnect();
                return;
            }
            catch (Exception ex)
            {
                _logger.Error(() => $"SendAsync Error", ex);
                Interlocked.Exchange(ref _isSending, 0);
                _onDisconnect();
                return;
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
        // 세션 교체 후 들어온 구 소켓의 완료 콜백.
        // SAEA는 이제 자유이므로 플래그를 내리고, 새 연결의 큐에 쌓인 메시지가 있으면 이어서 보낸다.
        if (!ReferenceEquals(sender, _connection?.Socket))
        {
            Interlocked.Exchange(ref _isSending, 0);
            TryStartSending();
            return;
        }

        if (e.SocketError != SocketError.Success)
        {
            _logger.Info(() => "Send Error");
            Interlocked.Exchange(ref _isSending, 0);
            _onDisconnect();
            return;
        }
        // 다음 메시지 처리
        ProcessSendQueue();
    }
}
