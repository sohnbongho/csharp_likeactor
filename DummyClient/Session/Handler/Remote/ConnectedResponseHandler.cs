using Google.Protobuf;
using Library.MessageQueue;
using Library.MessageQueue.Attributes.Remote;
using Messages;

namespace DummyClient.Session.Handler.Remote;

[RemoteMessageHandlerAsync(MessageWrapper.PayloadOneofCase.ConnectedResponse)]
public class ConnectedResponseHandler : IRemoteMessageHandlerAsync
{
    public Task<bool> HandleAsync(IMessageQueueReceiver receiver, MessageWrapper message)
    {
        if (receiver is not UserSession session)
            return Task.FromResult(true);

        if (session.Phase == SessionPhase.GameServer)
        {
            session.Send(new MessageWrapper
            {
                GameConnectRequest = new GameConnectRequest { AuthToken = session.AuthToken }
            });
        }
        else
        {
            session.Send(new MessageWrapper
            {
                LoginRequest = new LoginRequest
                {
                    UserId = session.UserId,
                    PasswordHash = ByteString.CopyFrom(session.ClientHashBytes)
                }
            });
        }

        return Task.FromResult(true);
    }
}
