using Library.Logger;
using Library.MessageQueue;
using Library.MessageQueue.Attributes.Remote;
using Messages;

namespace DummyClient.Session.Handler.Remote;

// 협동 테스트 모드(--coop)의 서버 응답/알림 처리. 상태는 UserSession의 volatile 필드에 기록하고
// 진행(참가→준비→시작)은 CoopDirector가 그 상태를 보고 지시한다.

[RemoteMessageHandlerAttribute(MessageWrapper.PayloadOneofCase.RoomCreateResponse)]
public class RoomCreateResponseHandler : IRemoteMessageHandler
{
    private static readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();

    public bool Handle(IMessageQueueReceiver receiver, MessageWrapper message)
    {
        if (receiver is not UserSession session) return true;
        var resp = message.RoomCreateResponse;
        session.RoomError = resp.ErrorCode;
        if (resp.Success)
        {
            session.RoomCode = resp.Room.RoomCode;
            session.InRoom = true;
            session.IsHost = true;
            _logger.Info(() => $"[방 생성] {session.UserId} 코드={resp.Room.RoomCode} 스테이지={resp.Room.StageId}");
        }
        else
        {
            _logger.Warn(() => $"[방 생성 실패] {session.UserId} error={resp.ErrorCode}");
        }
        return true;
    }
}

[RemoteMessageHandlerAttribute(MessageWrapper.PayloadOneofCase.RoomEnterResponse)]
public class RoomEnterResponseHandler : IRemoteMessageHandler
{
    private static readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();

    public bool Handle(IMessageQueueReceiver receiver, MessageWrapper message)
    {
        if (receiver is not UserSession session) return true;
        var resp = message.RoomEnterResponse;
        session.RoomError = resp.ErrorCode;
        if (resp.Success)
        {
            session.RoomCode = resp.Room.RoomCode;
            session.InRoom = true;
            if (resp.Room.Members.Count == 1)
                _logger.Info(() => $"[월드 입장] {session.UserId} 월드={resp.Room.WorldId} 코드={resp.Room.RoomCode} (첫 입장, 방장)");
        }
        else
        {
            _logger.Warn(() => $"[월드 입장 실패] {session.UserId} error={resp.ErrorCode}");
        }
        return true;
    }
}

[RemoteMessageHandlerAttribute(MessageWrapper.PayloadOneofCase.RoomJoinResponse)]
public class RoomJoinResponseHandler : IRemoteMessageHandler
{
    private static readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();

    public bool Handle(IMessageQueueReceiver receiver, MessageWrapper message)
    {
        if (receiver is not UserSession session) return true;
        var resp = message.RoomJoinResponse;
        session.RoomError = resp.ErrorCode;
        if (resp.Success)
        {
            session.RoomCode = resp.Room.RoomCode;
            session.InRoom = true;
        }
        else
        {
            _logger.Warn(() => $"[방 참가 실패] {session.UserId} error={resp.ErrorCode}");
        }
        return true;
    }
}

[RemoteMessageHandlerAttribute(MessageWrapper.PayloadOneofCase.RoomStateNoti)]
public class RoomStateNotiHandler : IRemoteMessageHandler
{
    private static readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();

    public bool Handle(IMessageQueueReceiver receiver, MessageWrapper message)
    {
        if (receiver is not UserSession session || !session.IsHost) return true;
        var room = message.RoomStateNoti.Room;
        _logger.Debug(() => $"[방 상태] {room.RoomCode} 인원={room.Members.Count} 준비={room.Members.Count(m => m.Ready)}");
        return true;
    }
}

[RemoteMessageHandlerAttribute(MessageWrapper.PayloadOneofCase.RoomStartResponse)]
public class RoomStartResponseHandler : IRemoteMessageHandler
{
    private static readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();

    public bool Handle(IMessageQueueReceiver receiver, MessageWrapper message)
    {
        if (receiver is not UserSession session) return true;
        var resp = message.RoomStartResponse;
        session.StartError = resp.ErrorCode;
        if (!resp.Success)
            _logger.Warn(() => $"[시작 실패] {session.UserId} error={resp.ErrorCode}");
        return true;
    }
}

[RemoteMessageHandlerAttribute(MessageWrapper.PayloadOneofCase.BattleStartNoti)]
public class BattleStartNotiHandler : IRemoteMessageHandler
{
    private static readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();

    public bool Handle(IMessageQueueReceiver receiver, MessageWrapper message)
    {
        if (receiver is not UserSession session) return true;
        var start = message.BattleStartNoti;
        session.MySlot = start.MySlot;
        session.InBattle = true;
        session.BattleEnded = false;
        if (session.IsHost)
            _logger.Info(() => $"[전투 시작] 방={session.RoomCode} 스테이지={start.StageId} 인원={start.Players.Count} 틱={start.TickRate}Hz 스냅샷={start.SnapshotRate}Hz 카운트다운={start.CountdownMs}ms");
        return true;
    }
}

[RemoteMessageHandlerAttribute(MessageWrapper.PayloadOneofCase.BattleSnapshotNoti)]
public class BattleSnapshotNotiHandler : IRemoteMessageHandler
{
    public bool Handle(IMessageQueueReceiver receiver, MessageWrapper message)
    {
        if (receiver is not UserSession session) return true;
        var snap = message.BattleSnapshotNoti;
        session.SnapshotCount++;
        session.EventCount += snap.Events.Count;
        session.LastSnapshotTick = snap.Tick;
        session.SnapshotBytes += snap.CalculateSize();
        if (snap.Enemies != null) session.LastEnemyInView = snap.Enemies.Ids.Count;
        foreach (var ev in snap.Events)
        {
            if (ev.BodyCase == BattleEvent.BodyOneofCase.PlayerRevived && ev.PlayerRevived.Slot == session.MySlot)
                session.ReviveCount++;
        }
        foreach (var p in snap.Players)
        {
            if (p.Slot != session.MySlot) continue;
            session.LastLevel = p.Level;
            session.LastHp = p.Hp;
            session.LastKills = p.Kills;
        }
        return true;
    }
}

[RemoteMessageHandlerAttribute(MessageWrapper.PayloadOneofCase.LevelUpChoiceNoti)]
public class LevelUpChoiceNotiHandler : IRemoteMessageHandler
{
    private static readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();

    public bool Handle(IMessageQueueReceiver receiver, MessageWrapper message)
    {
        if (receiver is not UserSession session) return true;
        var noti = message.LevelUpChoiceNoti;
        session.ChoiceCount++;
        // 첫 선택지를 고른다 (진화가 있으면 항상 맨 앞)
        int index = 0;
        var chosen = noti.Options.Count > 0 ? noti.Options[index] : null;
        _logger.Debug(() => $"[레벨업 선택] {session.UserId} choice={noti.ChoiceId} 선택={chosen?.Title} (후보 {noti.Options.Count})");
        session.Send(new MessageWrapper { UpgradeSelectRequest = new UpgradeSelectRequest { ChoiceId = noti.ChoiceId, Index = index } });
        return true;
    }
}

[RemoteMessageHandlerAttribute(MessageWrapper.PayloadOneofCase.BattleEndNoti)]
public class BattleEndNotiHandler : IRemoteMessageHandler
{
    private static readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();

    public bool Handle(IMessageQueueReceiver receiver, MessageWrapper message)
    {
        if (receiver is not UserSession session) return true;
        var end = message.BattleEndNoti;
        session.InBattle = false;
        session.BattleEnded = true;
        if (session.IsHost)
        {
            var results = string.Join(", ", end.Results.Select(r => $"슬롯{r.Slot} {r.HeroId} Lv{r.Level} 킬{r.Kills} 점수{r.Score} 골드{r.Gold}{(r.Alive ? "" : " 사망")}"));
            _logger.Info(() => $"[전투 종료] 방={session.RoomCode} {(end.Cleared ? "클리어" : "전멸")} {end.SurviveSeconds}초 | {results}");
        }
        return true;
    }
}
