using Library.Logger;
using Library.MessageQueue;
using Messages;
using Server.Handler;
using Server.Handler.RemoteAttribute;
using Server.Model;

namespace Server.Actors.User.Handler.Network;


[RemoteMessageHandlerAsyncAttribute(MessageWrapper.PayloadOneofCase.KeepAliveRequest)]
public class KeepAliveRequestHandler : IRemoteMessageHandlerAsync
{
    private readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();
    public async Task<bool> HandleAsync(IMessageQueueReceiver receiver, MessageWrapper message)
    {
        _logger.Debug(() => $"KeepAliveRequestHandler");        
        return true;
    }
}
