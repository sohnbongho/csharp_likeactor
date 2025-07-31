using Library.Logger;
using Library.MessageQueue;
using Library.MessageQueue.Message;
using Messages;
using Server.Handler.RemoteAttribute;

namespace Server.Actors.User.Handler.Network;


[RemoteMessageHandlerAsyncAttribute(MessageWrapper.PayloadOneofCase.KeepAliveRequest)]
public class KeepAliveRequestHandler : IRemoteMessageHandlerAsync
{
    private readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();
    public async Task<bool> HandleAsync(IMessageQueueReceiver receiver, MessageWrapper message)
    {
        _logger.Debug(() => $"KeepAliveRequestHandler");

        var messageWrapper = new MessageWrapper
        {
            KeepAliveNoti = new KeepAliveNoti()
        };
        await receiver.EnqueueAsync(new RemoteSendMessage
        {
            MessageWrapper = messageWrapper,
        });
        return true;
    }
}
