using GameServer.Rooms;
using Library.MessageQueue;
using Library.MessageQueue.Attributes.Remote;
using Messages;

namespace GameServer.Actors.User.Handler.Remote;

// 협동 방 메시지. 세션 스레드에서 받아 룸 액터에 명령으로 넘긴다. 응답은 룸 스레드가 보낸다.

[RemoteMessageHandlerAttribute(MessageWrapper.PayloadOneofCase.RoomCreateRequest)]
public class RoomCreateRequestHandler : IRemoteMessageHandler
{
    public bool Handle(IMessageQueueReceiver receiver, MessageWrapper message)
    {
        if (receiver is not UserSession session || session.AccountData == null) return true;

        if (session.Room != null)
        {
            session.Send(new MessageWrapper { RoomCreateResponse = new RoomCreateResponse { Success = false, ErrorCode = 1 } });
            return true;
        }

        var heroId = message.RoomCreateRequest.HeroId;
        if (string.IsNullOrEmpty(heroId)) heroId = RoomManager.Instance.Data.DefaultHeroId;

        var room = RoomManager.Instance.Create();
        room.Enqueue(r => r.Join(session, heroId, isCreate: true));
        return true;
    }
}

[RemoteMessageHandlerAttribute(MessageWrapper.PayloadOneofCase.RoomJoinRequest)]
public class RoomJoinRequestHandler : IRemoteMessageHandler
{
    public bool Handle(IMessageQueueReceiver receiver, MessageWrapper message)
    {
        if (receiver is not UserSession session || session.AccountData == null) return true;

        var req = message.RoomJoinRequest;
        if (session.Room != null)
        {
            session.Send(new MessageWrapper { RoomJoinResponse = new RoomJoinResponse { Success = false, ErrorCode = 4 } });
            return true;
        }

        var room = RoomManager.Instance.Find(req.RoomCode);
        if (room == null)
        {
            session.Send(new MessageWrapper { RoomJoinResponse = new RoomJoinResponse { Success = false, ErrorCode = 1 } });
            return true;
        }

        var heroId = string.IsNullOrEmpty(req.HeroId) ? RoomManager.Instance.Data.DefaultHeroId : req.HeroId;
        room.Enqueue(r => r.Join(session, heroId, isCreate: false));
        return true;
    }
}

[RemoteMessageHandlerAttribute(MessageWrapper.PayloadOneofCase.RoomLeaveRequest)]
public class RoomLeaveRequestHandler : IRemoteMessageHandler
{
    public bool Handle(IMessageQueueReceiver receiver, MessageWrapper message)
    {
        if (receiver is not UserSession session) return true;

        var room = session.Room;
        if (room == null)
        {
            session.Send(new MessageWrapper { RoomLeaveResponse = new RoomLeaveResponse { Success = false } });
            return true;
        }
        room.Enqueue(r => r.Leave(session, respond: true));
        return true;
    }
}

[RemoteMessageHandlerAttribute(MessageWrapper.PayloadOneofCase.RoomReadyRequest)]
public class RoomReadyRequestHandler : IRemoteMessageHandler
{
    public bool Handle(IMessageQueueReceiver receiver, MessageWrapper message)
    {
        if (receiver is not UserSession session) return true;
        bool ready = message.RoomReadyRequest.Ready;
        session.Room?.Enqueue(r => r.SetReady(session, ready));
        return true;
    }
}

[RemoteMessageHandlerAttribute(MessageWrapper.PayloadOneofCase.RoomSetHeroRequest)]
public class RoomSetHeroRequestHandler : IRemoteMessageHandler
{
    public bool Handle(IMessageQueueReceiver receiver, MessageWrapper message)
    {
        if (receiver is not UserSession session) return true;
        var heroId = message.RoomSetHeroRequest.HeroId;
        session.Room?.Enqueue(r => r.SetHero(session, heroId));
        return true;
    }
}

[RemoteMessageHandlerAttribute(MessageWrapper.PayloadOneofCase.RoomSetStageRequest)]
public class RoomSetStageRequestHandler : IRemoteMessageHandler
{
    public bool Handle(IMessageQueueReceiver receiver, MessageWrapper message)
    {
        if (receiver is not UserSession session) return true;
        var req = message.RoomSetStageRequest;
        var stageId = req.StageId;
        var castleId = req.CastleId;
        session.Room?.Enqueue(r => r.SetStage(session, stageId, castleId));
        return true;
    }
}

[RemoteMessageHandlerAttribute(MessageWrapper.PayloadOneofCase.RoomStartRequest)]
public class RoomStartRequestHandler : IRemoteMessageHandler
{
    public bool Handle(IMessageQueueReceiver receiver, MessageWrapper message)
    {
        if (receiver is not UserSession session) return true;

        var room = session.Room;
        if (room == null)
        {
            session.Send(new MessageWrapper { RoomStartResponse = new RoomStartResponse { Success = false, ErrorCode = 1 } });
            return true;
        }
        room.Enqueue(r => r.Start(session));
        return true;
    }
}
