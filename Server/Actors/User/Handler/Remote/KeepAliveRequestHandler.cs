using Library.Logger;
using Library.MessageQueue;
using Library.MessageQueue.Attributes.Remote;
using Library.MessageQueue.Message;
using Messages;

namespace Server.Actors.User.Handler.Remote;


[RemoteMessageHandlerAsyncAttribute(MessageWrapper.PayloadOneofCase.KeepAliveRequest)]
public class KeepAliveRequestHandler : IRemoteMessageHandlerAsync
{
    private readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();
    public async Task<bool> HandleAsync(IMessageQueueReceiver receiver, MessageWrapper message)
    {
        _logger.Debug(() => $"[SessionId:{receiver.SessionId}]KeepAliveRequestHandler");

        {
            var messageWrapper = new MessageWrapper
            {
                KeepAliveNoti = new KeepAliveNoti()
            };
            await receiver.EnqueueMessageAsync(new RemoteSendMessage
            {
                MessageWrapper = messageWrapper,
            });
        }

        return true;
    }
}
