using Library.ContInfo;
using Library.Logger;
using Library.MessageQueue;
using Library.MessageQueue.Message;
using System.Net.Sockets;

namespace Library.Network;

public class ReceiverHandler : IDisposable
{
    private static readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();

    // Bind 전에는 null, 이후에는 다음 Bind까지 교체하지 않는다.
    // 종료 시에도 null로 만들지 않는다. 닫힘 여부는 IsClosed / 소켓 자체 상태로 판단한다.
    private UserConnectionComponent? _connection;
    private readonly SocketAsyncEventArgs _receiveEventArgs;
    private readonly IMessageQueueReceiver _receiver;
    private readonly Action _onDisconnect;
    private readonly ReceiveParser _parser;

    // _receiveEventArgs에 ReceiveAsync가 걸려 있는지 여부 (0: idle, 1: in-flight).
    // 소켓을 Close해도 pending ReceiveAsync는 IOCP에서 비동기로 완료되므로,
    // 그 완료 콜백이 오기 전에 같은 SAEA로 새 ReceiveAsync를 걸면 InvalidOperationException이 난다.
    private int _receiving;

    public ReceiverHandler(IMessageQueueReceiver receiver, Action onDisconnect)
    {
        _receiver = receiver;
        _onDisconnect = onDisconnect;
        _parser = new ReceiveParser(SessionConstInfo.MaxBufferSize);
        _receiveEventArgs = new SocketAsyncEventArgs();
        _receiveEventArgs.Completed += OnReceiveCompleted;
    }

    // 새 연결을 바인드한다. parser 초기화는 여기서만 수행한다.
    public void Bind(UserConnectionComponent connection)
    {
        _parser.Reset();
        _connection = connection;
    }

    public void StartReceive()
    {
        var conn = _connection;
        if (conn == null || conn.IsClosed)
            return;

        // 구 소켓의 ReceiveAsync가 아직 완료되지 않았으면 SAEA를 쓸 수 없다.
        // 그 완료 콜백(OnReceiveCompleted)이 stale을 감지하고 StartReceive를 다시 호출한다.
        if (Interlocked.CompareExchange(ref _receiving, 1, 0) != 0)
            return;

        var buffer = _parser.GetBufferSegment();
        _receiveEventArgs.SetBuffer(buffer.Array, buffer.Offset, buffer.Count);

        try
        {
            if (!conn.Socket.ReceiveAsync(_receiveEventArgs))
                OnReceiveCompleted(conn.Socket, _receiveEventArgs);
        }
        catch (ObjectDisposedException)
        {
            // 세션이 이미 소켓을 닫음
            ReleaseReceiving();
            _onDisconnect();
        }
    }

    private void ReleaseReceiving() => Interlocked.Exchange(ref _receiving, 0);

    private async void OnReceiveCompleted(object? sender, SocketAsyncEventArgs e)
    {
        // 세션 교체 후 들어온 구 소켓의 완료 콜백.
        // SAEA는 이제 자유이므로, 새 연결이 StartReceive를 시도했다가 밀려났을 수 있어 다시 시작해 준다.
        if (!ReferenceEquals(sender, _connection?.Socket))
        {
            ReleaseReceiving();
            StartReceive();
            return;
        }

        if (e.BytesTransferred <= 0 || e.SocketError != SocketError.Success)
        {
            ReleaseReceiving();
            _onDisconnect();
            return;
        }

        try
        {
            var messages = _parser.Parse(e.BytesTransferred);
            foreach (var msg in messages)
            {
                PacketStats.IncrementReceived();
                var envelope = RemoteReceiveMessage.Rent(msg);
                if (!await _receiver.EnqueueMessageAsync(envelope))
                {
                    RemoteReceiveMessage.Return(envelope);
                    ReleaseReceiving();
                    _onDisconnect();
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(() => "Exception OnReceiveCompleted", ex);
            ReleaseReceiving();
            _onDisconnect();
            return;
        }

        // parser 사용이 끝난 뒤에 플래그를 내려야 Bind의 _parser.Reset()과 경합하지 않는다.
        ReleaseReceiving();
        StartReceive();
    }

    // 최종 해제: pool 자체가 파기될 때만 호출.
    public void Dispose()
    {
        _receiveEventArgs.Completed -= OnReceiveCompleted;
        _receiveEventArgs.Dispose();
        _parser.Dispose();
    }
}
