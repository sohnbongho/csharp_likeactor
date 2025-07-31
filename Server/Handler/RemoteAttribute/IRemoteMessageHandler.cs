using Messages;

namespace Server.Handler.RemoteAttribute;

public interface IRemoteMessageHandler
{
    bool Handle(IMessageReceiver receiver, MessageWrapper message);
}

