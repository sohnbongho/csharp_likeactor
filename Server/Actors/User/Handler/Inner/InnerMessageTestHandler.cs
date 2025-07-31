using Library.Logger;
using Library.MessageQueue;
using Library.MessageQueue.Attributes.Inner;
using Library.Model;
using Server.Model;

namespace Server.Actors.User.Handler.Inner;

[InnerMessageHandlerAsyncAttribute(typeof(InnerTestMessage))]
public class InnerMessageTestHandler : IInnerMessageHandlerAsync
{
    private readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();
    public Task<bool> HandleAsync(IMessageQueueReceiver receiver, IInnerServerMessage message)
    {
        _logger.Debug(() => $"InnerMessageTestHandler");
        return Task.FromResult(true);
    }
}

