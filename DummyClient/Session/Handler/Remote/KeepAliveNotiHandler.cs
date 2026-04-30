using Library.MessageQueue;
using Library.MessageQueue.Attributes.Remote;
using Messages;

namespace DummyClient.Session.Handler.Remote;

[RemoteMessageHandlerAsync(MessageWrapper.PayloadOneofCase.KeepAliveNoti)]
public class KeepAliveNotiHandler : IRemoteMessageHandlerAsync
{
    public Task<bool> HandleAsync(IMessageQueueReceiver receiver, MessageWrapper message)
    {
        receiver.Send(new MessageWrapper
        {
            KeepAliveRequest = new KeepAliveRequest()
        });

        return Task.FromResult(true);
    }
}
