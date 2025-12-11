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
    private readonly SocketAsyncEventArgs _sendEventArgs;
    private readonly byte[] _sendBuffer = new byte[SessionConstInfo.MaxBufferSize];
    private readonly ConcurrentQueue<MessageWrapper> _pendingSendQueue = new();
    private int _isSending = 0; // 0: idle, 1: sending

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
        while (_pendingSendQueue.TryDequeue(out _)) { }
        Interlocked.Exchange(ref _isSending, 0);
    }

    public bool Send(MessageWrapper message)
    {
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
            // 소켓 오류가 발생하면 세션 종료를 트리거하기 위해 송신을 중단
            (_socket as ISessionUsable)?.Disconnect();
            Interlocked.Exchange(ref _isSending, 0);
            return;
        }
        // 다음 메시지 처리
        ProcessSendQueue();
    }
}

