using Library.MessageQueue;
using Library.MessageQueue.Attributes.Remote;
using Messages;

namespace GameServer.Actors.User.Handler.Remote;

// 전투 중 클라이언트 → 서버 메시지. 룸 액터에 넘기고 끝난다 (응답 없음, 결과는 스냅샷으로).

[RemoteMessageHandlerAttribute(MessageWrapper.PayloadOneofCase.BattleInputRequest)]
public class BattleInputRequestHandler : IRemoteMessageHandler
{
    public bool Handle(IMessageQueueReceiver receiver, MessageWrapper message)
    {
        if (receiver is not UserSession session) return true;
        // 핸들러 반환 후 MessageWrapper가 풀로 돌아갈 수 있으므로 값을 복사해 넘긴다
        var input = message.BattleInputRequest.Clone();
        session.Room?.Enqueue(r => r.OnInput(session, input));
        return true;
    }
}

[RemoteMessageHandlerAttribute(MessageWrapper.PayloadOneofCase.UpgradeSelectRequest)]
public class UpgradeSelectRequestHandler : IRemoteMessageHandler
{
    public bool Handle(IMessageQueueReceiver receiver, MessageWrapper message)
    {
        if (receiver is not UserSession session) return true;
        var req = message.UpgradeSelectRequest;
        int choiceId = req.ChoiceId;
        int index = req.Index;
        session.Room?.Enqueue(r => r.OnUpgradeSelect(session, choiceId, index));
        return true;
    }
}
