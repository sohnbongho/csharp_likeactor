using Messages;
using Server.Model;

namespace Server.Handler;

public interface IMessageReceiver
{
    void OnRecvMessageHandle(IInnerServerMessage message);
    Task<bool> SendAsync(MessageWrapper message); // TODO: 테스트용
}
