using Google.Protobuf;
using Library.ContInfo;
using Library.Logger;
using Messages;
using System.Net.Sockets;

namespace Library.Network;

public class SenderHandler : IDisposable
{
    private readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();
    private Socket? _socket;
    private readonly SocketAsyncEventArgs _sendEventArgs;
    private readonly byte[] _sendBuffer = new byte[SessionConstInfo.MaxBufferSize];
    private readonly Queue<MessageWrapper> _pendingSendQueue = new();
    private bool _isSending = false;

    public SenderHandler()
    {
        _sendEventArgs = new SocketAsyncEventArgs();
        _sendEventArgs.Completed += OnSendCompleted;
    }
    public void Bind(Socket? socket)
    {
        _socket = socket;
    }
    public void Dispose()
    {
        _socket = null;
    }

    public bool Send(MessageWrapper message)
    {
        lock (_pendingSendQueue)
        {
            _pendingSendQueue.Enqueue(message);
            if (_isSending) return true; // 이미 처리 중이면 큐에만 넣음
            _isSending = true;
        }

        return ProcessSendQueue();


    }
    private bool ProcessSendQueue()
    {
        if (_socket == null)
            return false;

        MessageWrapper? message;

        lock (_pendingSendQueue)
        {
            if (_pendingSendQueue.Count == 0)
            {
                _isSending = false;
                return true;
            }
            message = _pendingSendQueue.Dequeue();
        }

        // 직렬화 및 SocketAsyncEventArgs 처리
        // ...
        try
        {
            using var ms = new MemoryStream();
            message.WriteTo(ms);
            var body = ms.ToArray();
            ushort bodyLength = (ushort)body.Length;

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
            _isSending = false;
            return;
        }
        // 다음 메시지 처리
        ProcessSendQueue();
    }
}

