using Library.Logger;
using Library.MessageQueue;
using Library.MessageQueue.Attributes.Remote;
using Messages;

namespace DummyClient.Session.Handler.Remote;

[RemoteMessageHandlerAsync(MessageWrapper.PayloadOneofCase.KeepAliveNoti)]
public class KeepAliveNotiHandler : IRemoteMessageHandlerAsync
{
    public async Task<bool> HandleAsync(IMessageQueueReceiver receiver, MessageWrapper message)
    {
        await Task.Delay(1000);

        // Channel→Dispatcher→Sender 한 번 더 거치지 않고 바로 송신 큐에 넣는다.
        receiver.Send(new MessageWrapper
        {
            KeepAliveRequest = new KeepAliveRequest()
        });

        return true;
    }
}
