using Library.Db;
using Library.MessageQueue;
using Library.MessageQueue.Attributes.Inner;
using Library.Model;

namespace Server.Actors.User.Handler.Inner;

[InnerMessageHandlerAsync(typeof(DbErrorMessage))]
public class DbErrorHandler : IInnerMessageHandlerAsync
{
    public Task<bool> HandleAsync(IMessageQueueReceiver receiver, IInnerServerMessage message)
    {
        receiver.Send(new Messages.MessageWrapper { ErrorResponse = new Messages.ErrorResponse { ErrorCode = 3 } });
        return Task.FromResult(true);
    }
}
