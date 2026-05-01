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
        // TODO: ErrorResponse proto 메시지 추가 후 Send() 구현
        // receiver.Send(new Messages.MessageWrapper { ErrorResponse = ... });
        return Task.FromResult(true);
    }
}
