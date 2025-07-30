using Google.Protobuf;
using Library.ContInfo;
using Library.Logger;
using Messages;
using System.Buffers;
using System.Net.Sockets;

namespace Library.Network;

public class SenderHandler : IDisposable
{
    private readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();
    private byte[] _receiveBuffer;

    public SenderHandler(int bufferSize = SessionConstInfo.MaxBufferSize)
    {
        _receiveBuffer = new byte[bufferSize];
    }


    public async Task<bool> SendAsync(NetworkStream stream, MessageWrapper message)
    {
        if (stream == null || !stream.CanWrite)
        {
            _logger.Warn(() => "[Send 실패] stream이 닫혀있거나 null입니다.");
            return false;
        }

        var buffer = _receiveBuffer;

        try
        {
            // 메시지를 직렬화
            using var ms = new MemoryStream();
            message.WriteTo(ms);
            var body = ms.ToArray();

            // 총 길이 = 2바이트 length prefix + body
            ushort bodyLength = (ushort)body.Length;
            int totalSize = 2 + bodyLength;

            if (_receiveBuffer.Length < totalSize)
            {
                _receiveBuffer = new byte[totalSize];
            }

            buffer = _receiveBuffer;

            // 길이 헤더
            var lengthBytes = BitConverter.GetBytes(bodyLength);
            Buffer.BlockCopy(lengthBytes, 0, buffer, 0, 2);

            // 메시지 바디
            Buffer.BlockCopy(body, 0, buffer, 2, bodyLength);

            // 전송
            await stream.WriteAsync(buffer, 0, totalSize);
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
    }
}