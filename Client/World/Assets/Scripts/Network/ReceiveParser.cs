using System;
using System.Collections.Generic;
using System.IO;
using Messages;

namespace Game.Network
{
    public class ReceiveParser
    {
        public const int MaxBufferSize = 8192;
        public const int MaxMessageBodySize = MaxBufferSize - 2;

        private enum ReceiveState { Header, Body }

        private ReceiveState _state = ReceiveState.Header;
        private ushort _bodySize = 0;
        private int _remainedOffset = 0;
        private readonly byte[] _buffer;

        public ReceiveParser()
        {
            _buffer = new byte[MaxBufferSize];
        }

        public ArraySegment<byte> GetBufferSegment()
        {
            return new ArraySegment<byte>(_buffer, _remainedOffset, _buffer.Length - _remainedOffset);
        }

        public List<MessageWrapper> Parse(int bytesTransferred)
        {
            var messages = new List<MessageWrapper>(4);

            int readOffset = 0;
            int remainedSize = _remainedOffset + bytesTransferred;
            _remainedOffset = 0;

            const int headerSize = 2;
            const int maxParsingCount = 10000;

            for (int i = 0; i < maxParsingCount; i++)
            {
                if (_state == ReceiveState.Header)
                {
                    if (remainedSize < headerSize) break;

                    _bodySize = BitConverter.ToUInt16(_buffer, readOffset);
                    if (_bodySize == 0 || _bodySize > MaxMessageBodySize)
                        throw new InvalidDataException($"유효하지 않은 메시지 크기: {_bodySize}");

                    readOffset += headerSize;
                    remainedSize -= headerSize;
                    _state = ReceiveState.Body;
                }
                else
                {
                    if (remainedSize < _bodySize) break;

                    var seg = new ArraySegment<byte>(_buffer, readOffset, _bodySize);
                    var message = MessageWrapper.Parser.ParseFrom(seg);
                    messages.Add(message);

                    _state = ReceiveState.Header;
                    readOffset += _bodySize;
                    remainedSize -= _bodySize;

                    if (remainedSize > 0)
                        Buffer.BlockCopy(_buffer, readOffset, _buffer, 0, remainedSize);

                    readOffset = 0;
                }
            }

            if (remainedSize > 0)
            {
                if (readOffset > 0)
                    Buffer.BlockCopy(_buffer, readOffset, _buffer, 0, remainedSize);
                _remainedOffset = remainedSize;
            }

            return messages;
        }

        public void Reset()
        {
            _state = ReceiveState.Header;
            _bodySize = 0;
            _remainedOffset = 0;
        }
    }
}
