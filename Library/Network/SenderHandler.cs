using Google.Protobuf;
using Library.ContInfo;
using Library.Logger;
using Library.MessageQueue;
using Library.MessageQueue.Message;
using Messages;
using System.Net.Sockets;

namespace Library.Network;

public class SenderHandler : IDisposable
{
    private readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();
    private byte[] _sendBuffer;

    private readonly IMessageQueueReceiver _receiver;
    private readonly MessageQueueWorker _messageQueueWorker;
        
    private NetworkStream? _stream;

    public SenderHandler(
        IMessageQueueReceiver receiver,
        MessageQueueWorker messageQueueWorker,
        int bufferSize = SessionConstInfo.MaxBufferSize)
    {
        _receiver = receiver;
        _messageQueueWorker = messageQueueWorker;
        _sendBuffer = new byte[bufferSize];
    }
    public void SetStream(NetworkStream? stream)
    {
        _stream = stream;
    }
    public async Task<bool> AddQueueAsync(MessageWrapper message)
    {
        await _messageQueueWorker.EnqueueAsync(_receiver, new RemoteSendMessageAsync
        {
            Message = message
        });
        return true;
    }

    public async Task<bool> SendAsync(MessageWrapper message)
    {
        if (_stream == null || !_stream.CanWrite)
        {
            _logger.Warn(() => "[Send 실패] stream이 닫혀있거나 null입니다.");
            return false;
        }

        var buffer = _sendBuffer;

        try
        {
            // 메시지를 직렬화
            using var ms = new MemoryStream();
            message.WriteTo(ms);
            var body = ms.ToArray();

            // 총 길이 = 2바이트 length prefix + body
            ushort bodyLength = (ushort)body.Length;
            int totalSize = 2 + bodyLength;

            if (_sendBuffer.Length < totalSize)
            {
                _sendBuffer = new byte[totalSize];
            }

            buffer = _sendBuffer;

            // 길이 헤더
            var lengthBytes = BitConverter.GetBytes(bodyLength);
            Buffer.BlockCopy(lengthBytes, 0, buffer, 0, 2);

            // 메시지 바디
            Buffer.BlockCopy(body, 0, buffer, 2, bodyLength);

            // 전송
            await _stream.WriteAsync(buffer, 0, totalSize);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(() => "[Send 실패]", ex);
            return false;
        }
    }

    public void Dispose()
    {
        _stream = null;        
    }
}