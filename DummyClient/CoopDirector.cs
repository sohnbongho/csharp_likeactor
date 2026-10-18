using DummyClient.Session;
using Library.Logger;
using Messages;

namespace DummyClient;

// 협동 테스트 모드: 인증된 세션을 roomSize명씩 묶어 방 생성 → 코드 참가 → 준비 → 시작을 지휘하고,
// 전투 중 통계를 주기적으로 찍은 뒤 전투가 끝나면 한 판 더 돌린다.
public sealed class CoopDirector
{
    private static readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();
    private static readonly string[] HeroIds = { "Hero_LiuBei", "Hero_GuanYu", "Hero_ZhangFei", "Hero_CaoCao", "Hero_LvBu" };

    private readonly int _roomSize;
    private readonly int _rounds;

    public CoopDirector(int roomSize, int rounds)
    {
        _roomSize = Math.Clamp(roomSize, 1, 4);
        _rounds = Math.Max(1, rounds);
    }

    public async Task RunAsync(IReadOnlyList<UserSession> sessions, CancellationToken token)
    {
        // 전원 인증 대기 (최대 30초)
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (sessions.Any(s => !s.IsAuthenticated) && DateTime.UtcNow < deadline)
            await Task.Delay(100, token);

        var ready = sessions.Where(s => s.IsAuthenticated).ToList();
        _logger.Info(() => $"[협동] 인증 {ready.Count}/{sessions.Count}명, 방 크기 {_roomSize}, {_rounds}라운드");

        var groups = ready.Chunk(_roomSize).ToList();
        var tasks = groups.Select((g, i) => RunGroupAsync(g, i, token)).ToList();
        _ = Task.Run(() => MonitorAsync(groups, token), token);
        await Task.WhenAll(tasks);
        _logger.Info(() => "[협동] 전 그룹 완료");
    }

    private async Task RunGroupAsync(UserSession[] group, int groupIndex, CancellationToken token)
    {
        for (int round = 1; round <= _rounds && !token.IsCancellationRequested; round++)
        {
            foreach (var s in group) s.ResetCoopState();
            var host = group[0];

            // 1) 월드(성) 입장: 그룹마다 다른 성으로 들어가면 서버가 성마다 방을 하나씩 만든다.
            //    첫 사람이 먼저 들어가 방장이 되고, 나머지는 같은 성으로 들어가 합류한다.
            string worldId = $"Castle_Test{groupIndex:D3}";
            host.Send(new MessageWrapper { RoomEnterRequest = new RoomEnterRequest { WorldId = worldId, HeroId = HeroIds[0] } });
            if (!await WaitAsync(() => host.RoomError >= 0, 5000, token) || !host.InRoom)
            {
                _logger.Warn(() => $"[협동 #{groupIndex}] 월드 입장 실패/시간초과 error={host.RoomError}");
                return;
            }
            host.IsHost = true;

            for (int i = 1; i < group.Length; i++)
            {
                var hero = HeroIds[i % HeroIds.Length];
                group[i].Send(new MessageWrapper { RoomEnterRequest = new RoomEnterRequest { WorldId = worldId, HeroId = hero } });
            }
            if (!await WaitAsync(() => group.All(s => s.RoomError >= 0), 5000, token) || group.Any(s => !s.InRoom))
            {
                _logger.Warn(() => $"[협동 #{groupIndex}] 합류 실패: {string.Join(",", group.Where(s => !s.InRoom).Select(s => $"{s.UserId}(error={s.RoomError})"))}");
                return;
            }
            if (group.Skip(1).Any(s => s.RoomCode != host.RoomCode))
            {
                _logger.Warn(() => $"[협동 #{groupIndex}] 같은 월드인데 방이 다름: {string.Join(",", group.Select(s => s.RoomCode))}");
                return;
            }

            // 3) 준비 → 시작 (준비가 아직 반영되지 않았으면 error 2로 거절되므로 재시도)
            for (int i = 1; i < group.Length; i++)
                group[i].Send(new MessageWrapper { RoomReadyRequest = new RoomReadyRequest { Ready = true } });

            bool started = false;
            for (int attempt = 0; attempt < 10 && !started; attempt++)
            {
                await Task.Delay(300, token);
                host.StartError = -1;
                host.Send(new MessageWrapper { RoomStartRequest = new RoomStartRequest() });
                if (!await WaitAsync(() => host.StartError >= 0, 3000, token)) continue;
                started = host.StartError == 0;
            }
            if (!started)
            {
                _logger.Warn(() => $"[협동 #{groupIndex}] 시작 실패 error={host.StartError}");
                return;
            }

            // 4) 전투 종료 대기 (스테이지 60초 + 레벨업 정지 여유)
            if (!await WaitAsync(() => host.BattleEnded, 300_000, token))
                _logger.Warn(() => $"[협동 #{groupIndex}] 전투 종료 알림 없음 (시간초과)");

            await Task.Delay(1000, token);
        }

        // 방 나가기
        foreach (var s in group)
            s.Send(new MessageWrapper { RoomLeaveRequest = new RoomLeaveRequest() });
    }

    private async Task MonitorAsync(List<UserSession[]> groups, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try { await Task.Delay(5000, token); }
            catch (OperationCanceledException) { break; }

            int shown = 0;
            foreach (var g in groups)
            {
                var host = g[0];
                if (!host.InBattle) continue;
                if (shown++ >= 3) break; // 그룹이 많으면 앞 3개만
                var members = string.Join(" | ", g.Select(s => $"s{s.MySlot} Lv{s.LastLevel} HP{s.LastHp} 킬{s.LastKills} 적{s.LastEnemyInView} 부활{s.ReviveCount}"));
                _logger.Info(() => $"[협동 {host.RoomCode}] 틱={host.LastSnapshotTick} 스냅샷={host.SnapshotCount} 이벤트={host.EventCount} 선택={host.ChoiceCount} 수신={host.SnapshotBytes / 1024}KB | {members}");
            }
        }
    }

    private static async Task<bool> WaitAsync(Func<bool> condition, int timeoutMs, CancellationToken token)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) return false;
            await Task.Delay(50, token);
        }
        return true;
    }
}
