using Library.ContInfo;
using Library.Logger;
using Library.MessageQueue;
using Library.MessageQueue.Message;
using System.Net.Sockets;

namespace Library.Network;

public class ReceiverHandler : IDisposable
{
    private Socket? _socket;
    private readonly SocketAsyncEventArgs _receiveEventArgs;
    private static readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();
    private readonly IMessageQueueReceiver _receiver;
    private readonly MessageQueueWorker _messageQueueWorker;
    private readonly ReceiveParser _parser;

    public ReceiverHandler(IMessageQueueReceiver receiver, MessageQueueWorker worker)
    {
        _receiver = receiver;
        _messageQueueWorker = worker;

        _parser = new ReceiveParser(SessionConstInfo.MaxBufferSize);

        _receiveEventArgs = new SocketAsyncEventArgs();
        // 실제 버퍼 범위는 StartReceive()에서 _remainedOffset 반영해 매번 설정하므로 여기서는 생략.
        _receiveEventArgs.Completed += OnReceiveCompleted;
    }

    public void Bind(Socket? socket)
    {
        _socket = socket;
    }

    // 세션 종료 후 pool 반환 직전 호출. SAEA/parser 버퍼 등 재사용 자원은 보존한다.
    public void Reset()
    {
        if (_socket != null)
        {
            try { _socket.Shutdown(SocketShutdown.Both); } catch { }
            try { _socket.Close(); } catch { }
            _socket = null;
        }
        _parser.Reset();
    }

    public void StartReceive()
    {
        var buffer = _parser.GetBufferSegment();
        _receiveEventArgs.SetBuffer(buffer.Array, buffer.Offset, buffer.Count);

        if (_socket != null && !_socket.ReceiveAsync(_receiveEventArgs))
            OnReceiveCompleted(_socket, _receiveEventArgs);
    }

    private async void OnReceiveCompleted(object? sender, SocketAsyncEventArgs e)
    {
        try
        {
            if (e.BytesTransferred <= 0 || e.SocketError != SocketError.Success)
            {
                _logger.Info(() => $"Disconnect");
                Disconnected();
                return;
            }

            var messages = _parser.Parse(e.BytesTransferred);
            foreach (var msg in messages)
            {
                await _messageQueueWorker.EnqueueAsync(_receiver, RemoteReceiveMessage.Rent(msg));
            }

            StartReceive();
        }
        catch (Exception ex)
        {
            _logger.Error(() => "Exception OnReceiveCompleted", ex);
            Disconnected();
        }
    }

    private void Disconnected()
    {
        if (_socket != null)
        {
            try { _socket.Shutdown(SocketShutdown.Send); } catch { }
            _socket.Close();
            _socket = null;
        }

        if (_receiver is ISessionUsable sessionUsable)
        {
            sessionUsable.Disconnect();
        }
    }

    // 최종 해제: pool 자체가 파기될 때만 호출.
    public void Dispose()
    {
        Reset();

        _receiveEventArgs.Completed -= OnReceiveCompleted;
        _receiveEventArgs.Dispose();
        _parser.Dispose();
    }
}


