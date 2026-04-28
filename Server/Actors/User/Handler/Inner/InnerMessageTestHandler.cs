using Library.Logger;
using Library.MessageQueue;
using Library.MessageQueue.Attributes.Inner;
using Library.Model;
using Server.Model.Message;

namespace Server.Actors.User.Handler.Inner;

[InnerMessageHandlerAsyncAttribute(typeof(InnerTestMessage))]
public class InnerMessageTestHandler : IInnerMessageHandlerAsync
{
    public Task<bool> HandleAsync(IMessageQueueReceiver receiver, IInnerServerMessage message)
    {
        return Task.FromResult(true);
    }
}

