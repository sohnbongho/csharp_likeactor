using Library.MessageQueue.Message;
using Messages;

namespace Library.MessageQueue;

public interface IMessageQueueReceiver
{
    ulong SessionId { get; }
    ValueTask<bool> EnqueueMessageAsync(IMessageQueue message);
    Task<bool> OnRecvMessageAsync(IMessageQueue messageWrapper);

    // 핸들러에서 응답을 보낼 때 Channel→Dispatcher→Sender 3단계를 건너뛰고 바로 송신한다.
    // 호출 스레드는 이미 해당 세션의 msgq 워커이므로 sender의 내부 CAS만 거친다.
    bool Send(MessageWrapper message);
}
