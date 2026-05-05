using Library.Logger;
using Library.MessageQueue;
using Library.MessageQueue.Attributes.Remote;
using Messages;

namespace DummyClient.Session.Handler.Remote;

[RemoteMessageHandlerAsync(MessageWrapper.PayloadOneofCase.MoveResponse)]
public class MoveResponseHandler : IRemoteMessageHandlerAsync
{
    private static readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();

    public Task<bool> HandleAsync(IMessageQueueReceiver receiver, MessageWrapper message)
    {
        var resp = message.MoveResponse;
        if (receiver is UserSession session)
            _logger.Debug(() => $"[MoveResponse] {session.UserId} ({resp.X}, {resp.Y}) success={resp.Success}");

        return Task.FromResult(true);
    }
}
