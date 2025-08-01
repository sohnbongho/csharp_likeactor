using Library.Logger;
using Library.MessageQueue;
using Library.MessageQueue.Attributes.Inner;
using Library.Model;
using Server.Model.Message;

namespace Server.Actors.User.Handler.Inner;

[InnerMessageHandlerAsyncAttribute(typeof(UserDisconnectMessage))]
public class UserDisconnectMessageHandler : IInnerMessageHandlerAsync
{
    private readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();
    public Task<bool> HandleAsync(IMessageQueueReceiver receiver, IInnerServerMessage message)
    {
        if(receiver is IDisposable disposable)
        {
            disposable.Dispose();
        }
        _logger.Debug(() => $"UserDisconnectMessageHandler");
        return Task.FromResult(true);
    }
}
