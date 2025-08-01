using Messages;

namespace Library.Network;

public class ReceiveParser
{
    private enum ReceiveState
    {
        Header,
        Body
    }

    private ReceiveState _state = ReceiveState.Header;
    private ushort _bodySize = 0;
    private int _remainedOffset = 0;
    private readonly byte[] _buffer;

    public ReceiveParser(int bufferSize)
    {
        _buffer = new byte[bufferSize];
    }

    public ArraySegment<byte> GetBufferSegment()
    {
        return new ArraySegment<byte>(_buffer, _remainedOffset, _buffer.Length - _remainedOffset);
    }

    public List<MessageWrapper> Parse(int bytesTransferred)
    {
        var messages = new List<MessageWrapper>();

        int readOffset = 0;
        int remainedSize = bytesTransferred;
        
        const int _headerSize = 2;
        const int _maxParsingCount = 10000;

        for( int i = 0; i < _maxParsingCount; i++ )
        {
            if (_state == ReceiveState.Header)
            {
                if (remainedSize < _headerSize)
                    break;

                _bodySize = BitConverter.ToUInt16(_buffer, readOffset);

                readOffset += _headerSize;
                remainedSize -= _headerSize;

                _state = ReceiveState.Body; // 헤더를 다 읽었으니 바디를 읽자
            }
            else if (_state == ReceiveState.Body)
            {
                if (remainedSize < _bodySize)
                {
                    break;
                }
                var message = MessageWrapper.Parser.ParseFrom(_buffer.AsSpan(readOffset, _bodySize));
                messages.Add(message);

                _state = ReceiveState.Header; // 바디를 다 읽었으니 헤더를 읽자
                readOffset += _bodySize;
                remainedSize -= _bodySize;

                // 남은 데이터 앞으로 당김                
                if (remainedSize > 0)
                {
                    Buffer.BlockCopy(_buffer, readOffset, _buffer, 0, remainedSize);
                    _remainedOffset = remainedSize;
                }
                else
                {
                    _remainedOffset = 0;
                }
                readOffset = 0;
            }
        }

        return messages;
    }
}

