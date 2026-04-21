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
    private static readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();
    private readonly SenderHandler _sender;
    private readonly ReceiverHandler _receiver;
    private readonly MessageQueueWorker _messageQueueWorker;
    private readonly ulong _sessionId;
    private readonly UserObjectPoolManager _userManager;
    // 0: active, 1: disposed — Interlocked로 원자 전환해 double-dispose race 방지.
    private int _disposedFlag;

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
        _messageQueueWorker = workerManager.GetWorker(sessionId);

        _receiver = new ReceiverHandler(this, _messageQueueWorker);
        _sender = new SenderHandler();
    }

    public async Task ConnectAsync(string host, int port)
    {
        await _client.ConnectAsync(host, port);

        var socket = _client.Client;
        _sender.Bind(socket);
        _receiver.Bind(socket);
        _receiver.StartReceive();
    }

    public void Run()
    {
        _logger.Debug(() => $"[SessionId:{SessionId}] Run...");

        _sender.Send(new MessageWrapper
        {
            KeepAliveRequest = new KeepAliveRequest()
        });
    }

    public async Task<bool> OnRecvMessageAsync(IMessageQueue message)
    {
        if (Volatile.Read(ref _disposedFlag) != 0)
            return false;

        return await MessageQueueDispatcher.Instance.OnRecvMessageAsync(this, _sender, message);
    }

    public ValueTask<bool> EnqueueMessageAsync(IMessageQueue message)
    {
        return _messageQueueWorker.EnqueueAsync(this, message);
    }

    public bool Send(MessageWrapper message)
    {
        if(Volatile.Read(ref _disposedFlag) != 0)
            return false;

        return _sender.Send(message);
    }

    public void Disconnect() => Dispose();

    public void Dispose()
    {
        // 원자 전환: 0→1에 성공한 스레드만 정리 경로 진입.
        if (Interlocked.CompareExchange(ref _disposedFlag, 1, 0) != 0)
            return;

        _client?.Dispose();
    }

    public void Tick() { }
}
