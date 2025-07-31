using Server.Model;

namespace Server.Handler;

public interface IMessageReceiver
{
    void OnRecvMessageHandle(IInnerServerMessage message);
}
