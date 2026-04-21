using Library.ContInfo;
using Messages;

namespace Library.Network;

public class ReceiveParser : IDisposable
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
        // 한 번의 수신에서 보통 1~수 개 메시지가 나오므로 초기 용량으로 List 리사이즈 할당 회피.
        var messages = new List<MessageWrapper>(4);

        // 이전 호출에서 남긴 partial 바이트(_remainedOffset)는 이미 버퍼 앞부분에 존재하며,
        // 소켓은 GetBufferSegment()가 반환한 offset(_remainedOffset)부터 새 데이터를 기록했다.
        // 따라서 이번 파싱이 보아야 할 총 바이트는 (이전 잔여) + (이번 신규) 이다.
        int readOffset = 0;
        int remainedSize = _remainedOffset + bytesTransferred;
        _remainedOffset = 0;

        const int _headerSize = 2;
        const int _maxParsingCount = 10000;

        for (int i = 0; i < _maxParsingCount; i++)
        {
            if (_state == ReceiveState.Header)
            {
                if (remainedSize < _headerSize)
                    break;

                _bodySize = BitConverter.ToUInt16(_buffer, readOffset);

                if (_bodySize == 0 || _bodySize > SessionConstInfo.MaxMessageBodySize)
                    throw new InvalidDataException($"유효하지 않은 메시지 크기: {_bodySize} (허용 범위: 1~{SessionConstInfo.MaxMessageBodySize})");

                readOffset += _headerSize;
                remainedSize -= _headerSize;

                _state = ReceiveState.Body; // 헤더를 다 읽었으니 바디를 읽자
            }
            else if (_state == ReceiveState.Body)
            {
                if (remainedSize < _bodySize)
                    break;

                var message = MessageWrapper.Parser.ParseFrom(_buffer.AsSpan(readOffset, _bodySize));
                messages.Add(message);

                _state = ReceiveState.Header; // 바디를 다 읽었으니 헤더를 읽자
                readOffset += _bodySize;
                remainedSize -= _bodySize;

                // 완료된 메시지 다음의 잔여 데이터를 버퍼 앞으로 당겨둔다.
                if (remainedSize > 0)
                    Buffer.BlockCopy(_buffer, readOffset, _buffer, 0, remainedSize);

                readOffset = 0;
            }
        }

        // partial header / partial body 상태로 루프를 빠져나온 경우,
        // 남은 바이트가 버퍼 중간(readOffset 이후)에 있을 수 있으므로 앞으로 당기고 _remainedOffset을 설정한다.
        if (remainedSize > 0)
        {
            if (readOffset > 0)
                Buffer.BlockCopy(_buffer, readOffset, _buffer, 0, remainedSize);
            _remainedOffset = remainedSize;
        }

        return messages;
    }

    // 세션 재사용 시 state machine만 초기화 (버퍼는 유지)
    public void Reset()
    {
        _state = ReceiveState.Header;
        _bodySize = 0;
        _remainedOffset = 0;
    }

    public void Dispose() => Reset();
}

