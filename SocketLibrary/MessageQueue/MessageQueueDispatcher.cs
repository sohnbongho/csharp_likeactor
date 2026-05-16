using Library.MessageQueue.Attributes.Inner;
using Library.MessageQueue.Attributes.Remote;
using Library.MessageQueue.Message;
using Library.Network;

namespace Library.MessageQueue;

// 핸들러 맵이 모두 static이므로 Dispatcher도 세션 전역 싱글톤으로 충분하다.
// 세션 10,000개 × Dispatcher/Manager 3 객체 = 30,000 객체 할당을 제거.
public sealed class MessageQueueDispatcher
{
    public static MessageQueueDispatcher Instance { get; } = new();

    private readonly InnerMessageHandlerManager _innerMessageHandlers = new();
    private readonly RemoteMessageHandlerManager _remoteMessageHandlers = new();

    private MessageQueueDispatcher()
    {
        // RegisterHandlers는 내부적으로 double-checked lock + initialized 플래그로 1회만 실제 등록.
        _innerMessageHandlers.RegisterHandlers();
        _remoteMessageHandlers.RegisterHandlers();
    }

    public async Task<bool> OnRecvMessageAsync(IMessageQueueReceiver receiver, SenderHandler sender, IMessageQueue message)
    {
        if (message is RemoteReceiveMessage receiveMessage)
        {
            var messageWrapper = receiveMessage.MessageWrapper;
            try
            {
                if (_remoteMessageHandlers.IsAsync(messageWrapper.PayloadCase))
                    await _remoteMessageHandlers.OnRecvMessageAsync(receiver, messageWrapper);
                else
                    _remoteMessageHandlers.OnRecvMessage(receiver, messageWrapper);
            }
            finally
            {
                // 핸들러 예외 여부와 무관하게 envelope은 반드시 pool로 반환.
                RemoteReceiveMessage.Return(receiveMessage);
            }
        }
        else if (message is RemoteSendMessage sendMessage)
        {
            sender.Send(sendMessage.MessageWrapper);
        }
        else if (message is InnerReceiveMessage innerReceiveMessage)
        {
            var innerMessage = innerReceiveMessage.Message;
            if (_innerMessageHandlers.IsAsync(innerMessage))
                await _innerMessageHandlers.OnRecvMessageAsync(receiver, innerMessage);
            else
                _innerMessageHandlers.OnRecvMessage(receiver, innerMessage);
        }
        return true;
    }
}
