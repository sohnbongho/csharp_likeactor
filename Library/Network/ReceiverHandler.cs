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
    private readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();
    private readonly IMessageQueueReceiver _receiver;
    private readonly MessageQueueWorker _messageQueueWorker;
    private readonly ReceiveParser _parser;

    public ReceiverHandler(IMessageQueueReceiver receiver, MessageQueueWorker worker)
    {
        _receiver = receiver;
        _messageQueueWorker = worker;

        _parser = new ReceiveParser(SessionConstInfo.MaxBufferSize);

        _receiveEventArgs = new SocketAsyncEventArgs();
        _receiveEventArgs.SetBuffer(_parser.GetBufferSegment().Array, 0, SessionConstInfo.MaxBufferSize);
        _receiveEventArgs.Completed += OnReceiveCompleted;
    }

    public void Bind(Socket? socket)
    {
        _socket = socket;
    }

    public void StartReceive()
    {
        var buffer = _parser.GetBufferSegment();
        _receiveEventArgs.SetBuffer(buffer.Array, buffer.Offset, buffer.Count);

        if (_socket != null && !_socket.ReceiveAsync(_receiveEventArgs))
            OnReceiveCompleted(_socket, _receiveEventArgs);
    }

    private void OnReceiveCompleted(object? sender, SocketAsyncEventArgs e)
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
                _ = _messageQueueWorker.EnqueueAsync(_receiver, new RemoteReceiveMessage
                {
                    MessageWrapper = msg
                });
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
        if(_socket != null )
        {
            _socket.Shutdown(SocketShutdown.Both);
            _socket.Close();
            _socket = null;
        }        

        if (_receiver is ISessionUsable sessionUsable)
        {
            sessionUsable.Disconnect();
        }
    }

    public void Dispose()
    {
        _parser.Dispose();
        _socket = null;
    }
}


