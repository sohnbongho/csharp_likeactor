using Library.Logger;
using Messages;
using System.Buffers;
using System.Net.Sockets;

namespace Server.Session.User.Network;

public class ReceiverHandler : IDisposable
{    
    private readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();    
    private Action<MessageWrapper>? _onMessage;
    private Func<MessageWrapper, Task<bool>>? _onMessageAsync;

    public ReceiverHandler(Action<MessageWrapper>? onMessage= null, 
        Func<MessageWrapper, Task<bool>>? onMessageAsync = null)
    {        
        _onMessage = onMessage;
        _onMessageAsync = onMessageAsync;
    }
    public void Dispose()
    {        
    }

    public async Task<bool> OnReceiveAsync(NetworkStream stream)
    {
        byte[] header = new byte[2]; // 헤더를 읽자
        var readed = await ReadExactAsync(stream, header, 0, 2);
        if (readed == false)
        {
            _logger.Debug(() => "[연결 종료] 클라이언트가 연결을 끊었습니다.");
            return false;
        }

        // 바디 길이 추출 
        ushort bodyLength = BitConverter.ToUInt16(header, 0);

        // 바디 버퍼 대여
        byte[] bodyBuffer = ArrayPool<byte>.Shared.Rent(bodyLength);

        try
        {
            // 정확히 bodyLength만큼 읽기
            bool bodyRead = await ReadExactAsync(stream, bodyBuffer, 0, bodyLength);
            if (!bodyRead)
            {
                _logger.Debug(() => "[연결 종료] 메시지 수신 중 끊김");
                return false;
            }

            // 메시지 핸들러로 위임
            OnHandle(bodyBuffer.AsMemory(0, bodyLength));
        }
        catch (Exception ex)
        {
            _logger.Error(() => $"fail OnReceive", ex);
        }
        finally
        {
            // 버퍼 반환
            ArrayPool<byte>.Shared.Return(bodyBuffer);
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

    public void OnHandle(ReadOnlyMemory<byte> data)
    {
        try
        {
            // 1. protobuf 메시지 파싱
            var message = MessageWrapper.Parser.ParseFrom(data.Span);
            if(_onMessage != null)
                _onMessage(message);

        }
        catch (Exception ex)
        {
            _logger.Error(() => $"[ReceiverHandler 오류] ", ex);
        }
    }

    
}
