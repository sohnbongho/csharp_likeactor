using GameServer.Actors.User;
using GameServer.Actors.User.DbRequest.Sql;
using GameServer.Battle.Data;
using GameServer.Battle.Sim;
using Library.ContInfo;
using Library.Logger;
using Library.Worker.Interface;
using Messages;
using System.Collections.Concurrent;

namespace GameServer.Rooms;

// 협동 방 액터. 대기(멤버/준비/장수/스테이지) → 카운트다운 → 전투(BattleWorld 시뮬) → 대기로 돈다.
// 세션 스레드는 Enqueue로 명령만 넣고, 모든 상태 변경과 전송은 룸 스레드(TickAsync)에서 한다.
// 세션은 풀에 반환돼 재사용될 수 있으므로, 멤버가 가리키는 세션이 아직 그 세션인지 SessionId로 확인한다.
public sealed class Room : ITickable
{
    public const int MaxMembers = 4;
    public const int CountdownMs = 3000;
    private const int MaxCommandsPerTick = 256;
    private const int SnapshotEveryTicks = BattleWorld.TickRate / BattleWorld.SnapshotRate;
    // 스냅샷 분할 기준. 길이 헤더/여유를 빼고 바디 상한보다 조금 작게 잡는다.
    private const int SnapshotBudgetBytes = SessionConstInfo.MaxMessageBodySize - 200;

    private static readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();

    private enum Phase { Waiting, Countdown, Battle }

    private sealed class Member
    {
        public UserSession Session = null!;
        public ulong SessionId;
        public string UserId = "";
        public string DisplayName = "";
        public string HeroId = "";
        public bool Ready;
        public bool InBattle;   // 현재 판의 참가자인가 (전투 중 들어온 사람은 false, 다음 판부터)

        // 세션이 끊겨 풀로 돌아간 뒤 다른 유저가 재사용했으면 더 이상 이 멤버의 세션이 아니다
        public bool IsCurrent => Session.SessionId == SessionId;

        public void Send(MessageWrapper message)
        {
            if (IsCurrent) Session.Send(message);
        }
    }

    private readonly RoomManager _manager;
    private readonly GameDataStore _data;
    private readonly ConcurrentQueue<Action<Room>> _commands = new();
    private readonly Member?[] _members = new Member?[MaxMembers];

    private Phase _phase = Phase.Waiting;
    private int _hostSlot;
    private string _stageId;
    private string _castleId = "";
    private BattleWorld? _world;
    private float _countdownRemaining;
    private int _tickCounter;
    private bool _closed;

    public ulong RoomId { get; }
    public string Code { get; }
    public string WorldId { get; }       // 맵 위치 기반 방이면 성 id, 초대 코드 방이면 ""
    public bool IsClosed => Volatile.Read(ref _closedFlag) != 0;
    public ulong SessionId => RoomId; // ITickable 키

    private int _closedFlag;

    public Room(RoomManager manager, GameDataStore data, ulong roomId, string code, string worldId)
    {
        _manager = manager;
        _data = data;
        RoomId = roomId;
        Code = code;
        WorldId = worldId;
        _stageId = data.DefaultStageId;
        _castleId = worldId; // 월드 방은 그 성을 전투 배경으로 쓴다 (방장이 RoomSetStageRequest로 바꿀 수 있음)
    }

    public enum JoinMode { Create, JoinByCode, EnterWorld }

    // 다른 스레드에서 호출. 룸 스레드가 다음 틱에 실행한다.
    public void Enqueue(Action<Room> command) => _commands.Enqueue(command);

    // ───────────────────────── 틱 ─────────────────────────

    public ValueTask TickAsync()
    {
        int drained = 0;
        while (drained++ < MaxCommandsPerTick && _commands.TryDequeue(out var command))
        {
            if (_closed) break;
            try { command(this); }
            catch (Exception ex) { _logger.Error(() => $"[방 {Code}] 명령 처리 오류", ex); }
        }
        if (_closed) return ValueTask.CompletedTask;

        switch (_phase)
        {
            case Phase.Countdown:
                _countdownRemaining -= BattleWorld.TickSeconds;
                if (_countdownRemaining <= 0f)
                    _phase = Phase.Battle;
                break;

            case Phase.Battle:
                StepBattle();
                break;
        }
        return ValueTask.CompletedTask;
    }

    private void StepBattle()
    {
        var world = _world!;
        world.Step();
        _tickCounter++;

        if (_tickCounter % SnapshotEveryTicks == 0 || world.Ended)
        {
            foreach (var m in _members)
            {
                if (m == null || !m.InBattle) continue;
                var viewer = world.GetPlayer(SlotOf(m));
                if (viewer == null) continue;
                SendSnapshot(m, world.BuildSnapshot(viewer));
            }
            world.ClearEvents();
        }

        if (world.Ended)
            FinishBattle();
    }

    // 이벤트가 많아 바디 한계를 넘으면 같은 tick의 메시지 여러 개로 나눈다 (문서 3.2절 규칙)
    private void SendSnapshot(Member m, BattleSnapshotNoti snap)
    {
        if (snap.CalculateSize() <= SnapshotBudgetBytes)
        {
            m.Send(new MessageWrapper { BattleSnapshotNoti = snap });
            return;
        }

        var events = snap.Events.ToArray();
        snap.Events.Clear();
        int size = snap.CalculateSize();
        var current = snap;

        foreach (var ev in events)
        {
            int evSize = ev.CalculateSize() + 3; // 태그 + 길이 접두 여유
            if (size + evSize > SnapshotBudgetBytes && current.Events.Count > 0)
            {
                m.Send(new MessageWrapper { BattleSnapshotNoti = current });
                current = new BattleSnapshotNoti { Tick = snap.Tick, ElapsedMs = snap.ElapsedMs, Stage = snap.Stage };
                size = current.CalculateSize();
            }
            current.Events.Add(ev);
            size += evSize;
        }
        m.Send(new MessageWrapper { BattleSnapshotNoti = current });
    }

    // ───────────────────────── 멤버 ─────────────────────────

    private int SlotOf(Member m) => Array.IndexOf(_members, m);

    private Member? FindMember(UserSession session)
    {
        foreach (var m in _members)
            if (m != null && m.SessionId == session.SessionId && ReferenceEquals(m.Session, session)) return m;
        return null;
    }

    private int MemberCount
    {
        get
        {
            int n = 0;
            foreach (var m in _members) if (m != null) n++;
            return n;
        }
    }

    // 방 생성자의 첫 참가, 초대 코드 참가, 월드 입장이 모두 여기로 온다.
    // 월드 입장은 전투 중에도 허용된다 (대기 멤버로 머물다 다음 판 참여). 코드 참가는 전투 중이면 거절.
    public void Join(UserSession session, string heroId, JoinMode mode)
    {
        if (_closed && mode == JoinMode.EnterWorld)
        {
            // 마지막 멤버가 나가 방이 닫히는 사이에 들어온 입장: 새 방으로 다시 보낸다
            _manager.GetOrCreateForWorld(WorldId).Enqueue(r => r.Join(session, heroId, mode));
            return;
        }

        int error = 0;
        if (_closed) error = 1;
        else if (session.AccountData == null) error = 5;
        else if (session.Room != null || FindMember(session) != null) error = 4;
        else if (_phase != Phase.Waiting && mode != JoinMode.EnterWorld) error = 3;
        else if (MemberCount >= MaxMembers) error = 2;
        else if (!_data.Heroes.ContainsKey(heroId)) error = 5;

        if (error != 0)
        {
            switch (mode)
            {
                case JoinMode.Create:
                    // 생성 응답의 오류 코드: 1=이미 방에 있음 2=서버오류 3=장수 없음
                    session.Send(new MessageWrapper { RoomCreateResponse = new RoomCreateResponse { Success = false, ErrorCode = error == 4 ? 1 : error == 5 ? 3 : 2 } });
                    Close();
                    break;
                case JoinMode.JoinByCode:
                    session.Send(new MessageWrapper { RoomJoinResponse = new RoomJoinResponse { Success = false, ErrorCode = error } });
                    break;
                default:
                    session.Send(new MessageWrapper { RoomEnterResponse = new RoomEnterResponse { Success = false, ErrorCode = error == 1 ? 6 : error } });
                    break;
            }
            return;
        }

        int slot = Array.IndexOf(_members, null);
        var member = new Member
        {
            Session = session,
            SessionId = session.SessionId,
            UserId = session.AccountData!.UserId,
            DisplayName = session.AccountData.UserId,
            HeroId = heroId,
        };
        _members[slot] = member;
        if (MemberCount == 1) _hostSlot = slot;
        session.AttachRoom(this, slot);

        var info = BuildInfo();
        switch (mode)
        {
            case JoinMode.Create:
                member.Send(new MessageWrapper { RoomCreateResponse = new RoomCreateResponse { Success = true, Room = info } });
                break;
            case JoinMode.JoinByCode:
                member.Send(new MessageWrapper { RoomJoinResponse = new RoomJoinResponse { Success = true, Room = info } });
                break;
            default:
                member.Send(new MessageWrapper { RoomEnterResponse = new RoomEnterResponse { Success = true, Room = info } });
                break;
        }
        BroadcastState(info, except: member);

        _logger.Info(() => $"[방 {Code}] 참가: {member.UserId} 슬롯 {slot} ({MemberCount}/{MaxMembers})");
    }

    // 명시적 나가기(RoomLeaveRequest)와 연결 끊김(UserSession.Dispose) 모두 여기로 온다.
    public void Leave(UserSession session, bool respond)
    {
        var member = FindMember(session);
        if (member == null)
        {
            if (respond) session.Send(new MessageWrapper { RoomLeaveResponse = new RoomLeaveResponse { Success = false } });
            return;
        }

        int slot = SlotOf(member);
        _members[slot] = null;
        if (member.IsCurrent) session.DetachRoom();
        if (respond) member.Send(new MessageWrapper { RoomLeaveResponse = new RoomLeaveResponse { Success = true } });

        _logger.Info(() => $"[방 {Code}] 퇴장: {member.UserId} 슬롯 {slot} ({MemberCount}/{MaxMembers})");

        if (MemberCount == 0)
        {
            Close();
            return;
        }

        if (_hostSlot == slot)
            _hostSlot = Array.FindIndex(_members, m => m != null);

        if (_world != null && member.InBattle)
            _world.DisconnectPlayer(slot);

        BroadcastState(BuildInfo(), except: null);
    }

    public void SetReady(UserSession session, bool ready)
    {
        var member = FindMember(session);
        if (member == null || _phase != Phase.Waiting) return;
        member.Ready = ready;
        BroadcastState(BuildInfo(), except: null);
    }

    public void SetHero(UserSession session, string heroId)
    {
        var member = FindMember(session);
        if (member == null || _phase != Phase.Waiting) return;
        if (!_data.Heroes.ContainsKey(heroId)) return;
        member.HeroId = heroId;
        BroadcastState(BuildInfo(), except: null);
    }

    public void SetStage(UserSession session, string stageId, string castleId)
    {
        var member = FindMember(session);
        if (member == null || _phase != Phase.Waiting || SlotOf(member) != _hostSlot) return;
        if (!_data.Stages.ContainsKey(stageId)) return;
        _stageId = stageId;
        _castleId = castleId ?? "";
        BroadcastState(BuildInfo(), except: null);
    }

    public void Start(UserSession session)
    {
        var member = FindMember(session);
        if (member == null) return;

        int error = 0;
        if (SlotOf(member) != _hostSlot) error = 1;
        else if (_phase != Phase.Waiting) error = 4;
        else if (!_data.Stages.TryGetValue(_stageId, out _)) error = 3;
        else
        {
            foreach (var m in _members)
                if (m != null && !ReferenceEquals(m, member) && !m.Ready) { error = 2; break; }
        }

        if (error != 0)
        {
            member.Send(new MessageWrapper { RoomStartResponse = new RoomStartResponse { Success = false, ErrorCode = error } });
            return;
        }

        member.Send(new MessageWrapper { RoomStartResponse = new RoomStartResponse { Success = true } });
        BeginBattle();
    }

    // ───────────────────────── 전투 ─────────────────────────

    private void BeginBattle()
    {
        var stage = _data.Stages[_stageId];
        var world = new BattleWorld(_data, stage, unchecked((int)RoomId * 7919 + Environment.TickCount));
        world.OnLevelUpChoice = (slot, noti) => _members[slot]?.Send(new MessageWrapper { LevelUpChoiceNoti = noti });

        var start = new BattleStartNoti
        {
            StageId = _stageId,
            TickRate = BattleWorld.TickRate,
            SnapshotRate = BattleWorld.SnapshotRate,
            CountdownMs = CountdownMs,
            CastleId = _castleId,
        };
        for (int slot = 0; slot < MaxMembers; slot++)
        {
            var m = _members[slot];
            if (m == null) continue;
            m.InBattle = true;
            world.AddPlayer(slot, _data.Heroes[m.HeroId]);
            start.Players.Add(new BattlePlayerInit { Slot = slot, UserId = m.UserId, DisplayName = m.DisplayName, HeroId = m.HeroId });
        }
        world.Start();

        _world = world;
        _tickCounter = 0;
        _countdownRemaining = CountdownMs / 1000f;
        _phase = Phase.Countdown;

        foreach (var m in _members)
        {
            if (m == null) continue;
            start.MySlot = SlotOf(m);
            m.Send(new MessageWrapper { BattleStartNoti = start.Clone() });
        }
        BroadcastState(BuildInfo(), except: null);

        _logger.Info(() => $"[방 {Code}] 전투 시작: {_stageId}, {MemberCount}명");
    }

    private void FinishBattle()
    {
        var world = _world!;
        var end = new BattleEndNoti { Cleared = world.Cleared, SurviveSeconds = (int)world.Elapsed };
        foreach (var p in world.Players)
        {
            int score = world.ScoreOf(p);
            end.Results.Add(new BattlePlayerResult
            {
                Slot = p.Slot, Kills = p.Kills, Level = p.Level, Score = score,
                Alive = !p.IsDead && p.Connected, HeroId = p.Hero.Id,
                Gold = GoldReward(world, p),
            });
        }

        foreach (var m in _members)
        {
            if (m == null) continue;
            m.Send(new MessageWrapper { BattleEndNoti = end });
            m.Ready = false;

            var p = m.InBattle ? world.GetPlayer(SlotOf(m)) : null;
            m.InBattle = false;
            if (p != null && m.IsCurrent && m.Session.AccountData != null)
            {
                m.Session.EnqueueSqlRequest(new SaveScoreSqlRequest(
                    m.Session, m.Session.AccountData.AccountId, world.ScoreOf(p), p.Kills, (int)world.Elapsed));
            }
        }

        _logger.Info(() => $"[방 {Code}] 전투 종료: {(world.Cleared ? "클리어" : "전멸")} {world.Elapsed:F1}초");

        _world = null;
        _phase = Phase.Waiting;
        BroadcastState(BuildInfo(), except: null);
    }

    // 유니티 RunRewards.Calculate와 같은 식 (배율 1). 영구 강화의 골드 배율은 메타 데이터를 서버로 옮긴 뒤 곱한다.
    private static int GoldReward(BattleWorld world, PlayerEntity p) =>
        (int)MathF.Floor(p.Kills * 0.5f + MathF.Floor(world.Elapsed) + (world.Cleared ? 50f : 0f));

    public void OnInput(UserSession session, BattleInputRequest input)
    {
        var member = FindMember(session);
        if (member == null || !member.InBattle || _world == null) return;
        _world.SetInput(SlotOf(member), input.Seq, input.MoveX, input.MoveY, input.Skill);
    }

    public void OnUpgradeSelect(UserSession session, int choiceId, int index)
    {
        var member = FindMember(session);
        if (member == null || !member.InBattle || _world == null) return;
        _world.SelectUpgrade(SlotOf(member), choiceId, index);
    }

    // ───────────────────────── 방 상태 ─────────────────────────

    private RoomInfo BuildInfo()
    {
        var info = new RoomInfo
        {
            RoomCode = Code, HostSlot = _hostSlot, StageId = _stageId, CastleId = _castleId,
            WorldId = WorldId, InBattle = _phase != Phase.Waiting,
        };
        for (int slot = 0; slot < MaxMembers; slot++)
        {
            var m = _members[slot];
            if (m == null) continue;
            info.Members.Add(new RoomMember { Slot = slot, UserId = m.UserId, DisplayName = m.DisplayName, HeroId = m.HeroId, Ready = m.Ready, InBattle = m.InBattle });
        }
        return info;
    }

    private void BroadcastState(RoomInfo info, Member? except)
    {
        foreach (var m in _members)
        {
            if (m == null || ReferenceEquals(m, except)) continue;
            m.Send(new MessageWrapper { RoomStateNoti = new RoomStateNoti { Room = info } });
        }
    }

    private void Close()
    {
        if (_closed) return;
        _closed = true;
        Volatile.Write(ref _closedFlag, 1);
        _world = null;
        for (int i = 0; i < MaxMembers; i++)
        {
            var m = _members[i];
            if (m != null && m.IsCurrent) m.Session.DetachRoom();
            _members[i] = null;
        }
        _manager.Remove(this);
    }
}
