using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Messages;
using UnityEngine;

namespace Game.Network
{
    public class TcpGameClient
    {
        private TcpClient _client;
        private NetworkStream _stream;
        private CancellationTokenSource _cts;
        private readonly ReceiveParser _parser = new ReceiveParser();
        private readonly ConcurrentQueue<MessageWrapper> _receiveQueue = new ConcurrentQueue<MessageWrapper>();
        private readonly ConcurrentQueue<MessageWrapper> _sendQueue = new ConcurrentQueue<MessageWrapper>();
        private readonly byte[] _sendBuffer = new byte[ReceiveParser.MaxBufferSize];
        private int _isSending;

        public bool IsConnected => _client != null && _client.Connected;
        public ConcurrentQueue<MessageWrapper> ReceiveQueue => _receiveQueue;

        public async Task<bool> ConnectAsync(string host, int port)
        {
            try
            {
                _client = new TcpClient();
                await _client.ConnectAsync(host, port);
                _stream = _client.GetStream();
                _cts = new CancellationTokenSource();

                _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TcpGameClient] 연결 실패: {ex.Message}");
                return false;
            }
        }

        public void Send(MessageWrapper message)
        {
            _sendQueue.Enqueue(message);
            if (Interlocked.CompareExchange(ref _isSending, 1, 0) == 0)
                _ = Task.Run(ProcessSendQueueAsync);
        }

        private async Task ProcessSendQueueAsync()
        {
            try
            {
                while (_sendQueue.TryDequeue(out var message))
                {
                    int bodyLength = message.CalculateSize();
                    if (bodyLength > ReceiveParser.MaxMessageBodySize)
                    {
                        Debug.LogWarning($"[TcpGameClient] 메시지 크기 초과 ({bodyLength}), 드롭");
                        continue;
                    }

                    _sendBuffer[0] = (byte)(bodyLength & 0xFF);
                    _sendBuffer[1] = (byte)((bodyLength >> 8) & 0xFF);
                    message.WriteTo(_sendBuffer.AsSpan(2, bodyLength));

                    await _stream.WriteAsync(_sendBuffer, 0, 2 + bodyLength);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TcpGameClient] 송신 오류: {ex.Message}");
                Disconnect();
            }
            finally
            {
                Interlocked.Exchange(ref _isSending, 0);
                if (!_sendQueue.IsEmpty && Interlocked.CompareExchange(ref _isSending, 1, 0) == 0)
                    _ = Task.Run(ProcessSendQueueAsync);
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && _client != null && _client.Connected)
                {
                    var seg = _parser.GetBufferSegment();
                    int read = await _stream.ReadAsync(seg.Array, seg.Offset, seg.Count, token);
                    if (read <= 0)
                    {
                        Disconnect();
                        return;
                    }

                    var messages = _parser.Parse(read);
                    foreach (var msg in messages)
                        _receiveQueue.Enqueue(msg);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.LogError($"[TcpGameClient] 수신 오류: {ex.Message}");
                Disconnect();
            }
        }

        public void Disconnect()
        {
            try { _cts?.Cancel(); } catch { }
            try { _stream?.Close(); } catch { }
            try { _client?.Close(); } catch { }
            _stream = null;
            _client = null;
            _parser.Reset();
        }
    }
}
