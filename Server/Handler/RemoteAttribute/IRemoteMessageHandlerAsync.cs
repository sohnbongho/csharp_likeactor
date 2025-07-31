using Messages;

namespace Server.Handler.RemoteAttribute;

public interface IRemoteMessageHandlerAsync
{
    Task<bool> HandleAsync(IMessageReceiver receiver, MessageWrapper message);
}

