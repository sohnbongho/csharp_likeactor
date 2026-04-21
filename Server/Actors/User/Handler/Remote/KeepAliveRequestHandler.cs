using Library.Logger;
using Library.MessageQueue;
using Library.MessageQueue.Attributes.Remote;
using Messages;

namespace Server.Actors.User.Handler.Remote;


[RemoteMessageHandlerAsyncAttribute(MessageWrapper.PayloadOneofCase.KeepAliveRequest)]
public class KeepAliveRequestHandler : IRemoteMessageHandlerAsync
{
    private readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();
    public Task<bool> HandleAsync(IMessageQueueReceiver receiver, MessageWrapper message)
    {
        _logger.Debug(() => $"[SessionId:{receiver.SessionId}]KeepAliveRequestHandler");

        // Channel→Dispatcher→Sender 한 번 더 거치지 않고 바로 송신 큐에 넣는다.
        receiver.Send(new MessageWrapper
        {
            KeepAliveNoti = new KeepAliveNoti()
        });

        return Task.FromResult(true);
    }
}
