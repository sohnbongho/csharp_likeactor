using Library.Logger;
using Library.MessageQueue;
using Library.MessageQueue.Attributes.Remote;
using Library.MessageQueue.Message;
using Messages;

namespace DummyClient.Session.Handler.Remote;

[RemoteMessageHandlerAsync(MessageWrapper.PayloadOneofCase.KeepAliveNoti)]
public class KeepAliveNotiHandler : IRemoteMessageHandlerAsync
{
    private readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();
    public async Task<bool> HandleAsync(IMessageQueueReceiver receiver, MessageWrapper message)
    {
        _logger.Debug(() => $"[SessionId:{receiver.SessionId}]KeepAliveRequestHandler");

        await Task.Delay(1000);

        {
            var messageWrapper = new MessageWrapper
            {
                KeepAliveRequest = new KeepAliveRequest()
            };
            await receiver.EnqueueMessageAsync(new RemoteSendMessage
            {
                MessageWrapper = messageWrapper,
            });
        }

        return true;
    }
}
