using Library.Db;
using Library.MessageQueue;
using Library.MessageQueue.Attributes.Inner;
using Library.Model;
using Messages;

namespace GameServer.Actors.User.Handler.Inner;

[InnerMessageHandlerAsync(typeof(DbErrorMessage))]
public class DbErrorHandler : IInnerMessageHandlerAsync
{
    public Task<bool> HandleAsync(IMessageQueueReceiver receiver, IInnerServerMessage message)
    {
        receiver.Send(new MessageWrapper { ErrorResponse = new ErrorResponse { ErrorCode = 3 } });
        return Task.FromResult(true);
    }
}
