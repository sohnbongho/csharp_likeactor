using Library.MessageQueue.Attributes.Inner;
using Library.MessageQueue.Attributes.Remote;
using Library.MessageQueue.Message;
using Library.Network;

namespace Library.MessageQueue;

public class MessageQueueDispatcher
{
    private readonly InnerMessageHandlerManager _innerMessageHandlers;
    private readonly RemoteMessageHandlerManager _remoteMessageHandlers;

    public MessageQueueDispatcher()
    {
        _innerMessageHandlers = new();
        _remoteMessageHandlers = new();
    }
    public void RegisterHandlers()
    {
        _innerMessageHandlers.RegisterHandlers();
        _remoteMessageHandlers.RegisterHandlers();
    }

    public async Task<bool> OnRecvMessageAsync(IMessageQueueReceiver receiver, SenderHandler? sender, IMessageQueue message)
    {
        if (message is RemoteReceiveMessage receiveMessage)
        {
            var messageWrapper = receiveMessage.MessageWrapper;
            if (_remoteMessageHandlers.IsAsync(messageWrapper.PayloadCase))
            {
                await _remoteMessageHandlers.OnRecvMessageAsync(receiver, messageWrapper);
            }
            else
            {
                _remoteMessageHandlers.OnRecvMessage(receiver, messageWrapper);
            }
        }
        else if (message is RemoteSendMessage sendMessage)
        {
            if (sender != null)
            {
                sender.Send(sendMessage.MessageWrapper);
            }
        }        
        else if (message is InnerReceiveMessage innerReceiveMessage)
        {
            var messageWrapper = innerReceiveMessage.Message;
            if (_innerMessageHandlers.IsAsync(messageWrapper))
            {
                await _innerMessageHandlers.OnRecvMessageAsync(receiver, messageWrapper);
            }
            else
            {
                _innerMessageHandlers.OnRecvMessage(receiver, messageWrapper);
            }
        }
        return true;
    }
}
