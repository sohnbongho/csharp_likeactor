using Library.ContInfo;
using Library.Logger;
using Library.MessageQueue;
using Library.MessageQueue.Message;
using Messages;
using System.Net.Sockets;

namespace Library.Network;

public class ReceiverHandler : IDisposable
{
    private readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();
    private Func<MessageWrapper.PayloadOneofCase, bool>? _messageAsyncChecker;
    private readonly MessageQueueWorker _messageQueueWorker;
    private readonly IMessageQueueReceiver _receiver;

    private byte[] _receiveBuffer;

    public ReceiverHandler(IMessageQueueReceiver receiver,
        MessageQueueWorker messageQueueWorker,
        Func<MessageWrapper.PayloadOneofCase, bool>? messageAsyncChecker = null,
        int bufferSize = SessionConstInfo.MaxBufferSize)
    {
        _receiver = receiver;
        _receiveBuffer = new byte[bufferSize];
        _messageAsyncChecker = messageAsyncChecker;
        _messageQueueWorker = messageQueueWorker;
    }
    public void Dispose()
    {
    }

    public async Task<bool> OnReceiveAsync(NetworkStream stream)
    {
        byte[] header = new byte[2]; // 헤더를 읽자
        var headerRead = await ReadExactAsync(stream, header, 0, 2);
        if (headerRead == false)
        {
            _logger.Debug(() => "[연결 종료] 클라이언트가 연결을 끊었습니다.");
            return false;
        }

        // 바디 길이 추출 
        ushort bodyLength = BitConverter.ToUInt16(header, 0);
        if (_receiveBuffer.Length < bodyLength)
        {
            _receiveBuffer = new byte[bodyLength];
        }
        try
        {
            // 정확히 bodyLength만큼 읽기
            bool bodyRead = await ReadExactAsync(stream, _receiveBuffer, 0, bodyLength);
            if (!bodyRead)
            {
                _logger.Debug(() => "[연결 종료] 메시지 수신 중 끊김");
                return false;
            }

            // 메시지 핸들러로 위임
            await OnHandleAsync(_receiveBuffer.AsMemory(0, bodyLength));
        }
        catch (Exception ex)
        {
            _logger.Error(() => $"fail OnReceive", ex);
        }

        return true;
    }

    private async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, int offset, int size)
    {
        int totalRead = 0;

        while (totalRead < size)
        {
            int read = await stream.ReadAsync(buffer, offset + totalRead, size - totalRead);
            if (read == 0)
            {
                // 연결 종료 (peer closed)
                return false;
            }

            totalRead += read;
        }

        return true;
    }
    public async Task<bool> OnHandleAsync(ReadOnlyMemory<byte> data)
    {
        try
        {
            // protobuf 메시지 파싱
            var message = MessageWrapper.Parser.ParseFrom(data.Span);
            if (_messageAsyncChecker != null && _messageAsyncChecker(message.PayloadCase))
            {
                await _messageQueueWorker.EnqueueAsync(_receiver, new RemoteReceiveMessageAsync
                {
                    Message = message,

                });
            }
            else
            {
                await _messageQueueWorker.EnqueueAsync(_receiver, new RemoteReceiveMessage
                {
                    Message = message,
                });
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(() => $"[ReceiverHandler 오류] ", ex);
            return false;
        }
    }
}
