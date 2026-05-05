using Library.MessageQueue;
using Library.MessageQueue.Attributes.Remote;
using Messages;

namespace Server.Actors.User.Handler.Remote;

[RemoteMessageHandlerAsyncAttribute(MessageWrapper.PayloadOneofCase.MoveRequest)]
public class MoveRequestHandler : IRemoteMessageHandlerAsync
{
    public Task<bool> HandleAsync(IMessageQueueReceiver receiver, MessageWrapper message)
    {
        if (receiver is not UserSession session)
            return Task.FromResult(true);

        var req = message.MoveRequest;
        session.UpdatePosition(req.X, req.Y);

        receiver.Send(new MessageWrapper
        {
            MoveResponse = new MoveResponse { Success = true, X = req.X, Y = req.Y }
        });

        return Task.FromResult(true);
    }
}
