using Library.Logger;
using Library.MessageQueue;
using Library.MessageQueue.Message;
using Library.Network;
using Library.Worker.Interface;
using Messages;
using System.Net.Sockets;

namespace DummyClient.Session;

public class UserSession : IDisposable, IMessageQueueReceiver, ISessionUsable, ITickable
{
    private readonly TcpClient _client;
    private NetworkStream _stream = null!;
    private int _counter = 0;    
    private readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();
    private readonly SenderHandler _sender;
    private readonly ReceiverHandler _receiver;
    private readonly MessageQueueWorker _messageQueueWorker;
    private readonly ulong _sessionId;
    private readonly UserObjectPoolManager _userManager;
    private Task? _task;
    private CancellationTokenSource? _cts;

    public ulong SessionId => _sessionId;

    public static UserSession Of(TcpClient client, ulong 
        sessionId, 
        UserObjectPoolManager userObjectPoolManager, 
        MessageQueueWorkerManager messageQueueWorkerManager)
    {
        return new UserSession(client, sessionId, userObjectPoolManager, messageQueueWorkerManager);
    }

    public UserSession(TcpClient client, ulong sessionId, UserObjectPoolManager userManager,
        MessageQueueWorkerManager workerManager)
    {
        _client = client;
        _sessionId = sessionId;
        _userManager = userManager;
        var messageQueueWorker = workerManager.GetWorker(sessionId);
        _messageQueueWorker = messageQueueWorker;

        _receiver = new ReceiverHandler(this, _messageQueueWorker);
        _sender = new SenderHandler();
    }    
    
    public async Task ConnectAsync(string host, int port)
    {
        await _client.ConnectAsync(host, port);
        _stream = _client.GetStream();

        var socket = _client.Client;

        _sender.Bind(socket);

        _receiver.Bind(socket);
        _receiver.StartReceive();
        
    }
    public void Run()
    {
        _logger.Debug(() => $"[SessionId:{SessionId}] Run...");

        _cts = new CancellationTokenSource();
        _task = StartAsync(_cts.Token);
    }

    private async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var message = new MessageWrapper
                {
                    KeepAliveRequest = new KeepAliveRequest { }
                };

                _sender.Send(message);
                _logger.Debug(() => $"[SessionId:{SessionId}][송신] KeepAliveRequest #{++_counter} 전송");

                await Task.Delay(1000, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Info(() => $"[SessionId:{SessionId}][종료] KeepAlive 루프가 취소되었습니다.");
        }
        catch (Exception ex)
        {
            _logger.Error(() => $"[SessionId:{SessionId}][오류] KeepAlive 루프 실행 중 예외 발생: {ex}", ex);
        }
    }


    public void OnRecvMessage(MessageWrapper messageWrapper)
    {
        _logger.Debug(() => $"[SessionId:{SessionId}] OnRecvMessage type:{messageWrapper.PayloadCase.ToString()}");
    }

    public Task<bool> OnRecvMessageAsync(IMessageQueue message)
    {
        if (message is RemoteReceiveMessage receiveMessage)
        {
            OnRecvMessage(receiveMessage.MessageWrapper);
        }
        else if (message is RemoteSendMessage sendMessage)
        {
            if (_sender != null)
            {
                _sender.Send(sendMessage.MessageWrapper);
            }
        }
        return Task.FromResult(true);
    }

    public async Task<bool> EnqueueMessageAsync(IMessageQueue message)
    {
        await _messageQueueWorker.EnqueueAsync(this, message);
        return true;
    }
    public void Stop()
    {
        _cts?.Cancel();
        try
        {
            _task?.Wait();
        }
        catch (AggregateException ex) when (ex.InnerException is OperationCanceledException)
        {
            // 무시 가능
        }
        catch (Exception ex)
        {
            _logger.Error(() => $"[SessionId:{SessionId}] [오류] Stop 중 예외", ex);
        }
    }
    public void Disconnect()
    {
        Stop();
        Dispose();
    }
    public void Dispose()
    {
        _client?.Dispose();
        _stream?.Dispose();
    }

    public void Tick()
    {
        
    }

    
}
