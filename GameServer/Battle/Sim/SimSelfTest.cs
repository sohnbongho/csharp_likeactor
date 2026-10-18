using GameServer.Battle.Data;
using Messages;

namespace GameServer.Battle.Sim;

// `GameServer --simtest`: 네트워크 없이 BattleWorld만 돌려 규칙을 확인한다.
// 무작위에 기대지 않고 피해/경험치를 직접 넣어 부활·레벨업 선택 규칙을 확정적으로 검사한다.
public static class SimSelfTest
{
    public static int Run(string dataDir)
    {
        var data = GameDataStore.Load(dataDir);
        var stage = data.Stages[data.DefaultStageId];
        var world = new BattleWorld(data, stage, seed: 1);
        var heroes = data.Heroes.Values.Take(2).ToList();
        var p0 = world.AddPlayer(0, heroes[0]);
        var p1 = world.AddPlayer(1, heroes[1]);
        world.Start();

        int failures = 0;
        void Check(bool ok, string what)
        {
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {what}");
            if (!ok) failures++;
        }

        // 1) 사망 → 10초 뒤 자동 부활 (아군 옆, 체력 50%, 무적)
        world.DamagePlayer(p0, 100000f);
        Check(p0.IsDead && p0.ReviveTimer > 0f, "치명 피해를 받으면 사망하고 부활 타이머가 켜진다");
        Check(world.Events.Any(e => e.BodyCase == BattleEvent.BodyOneofCase.PlayerDied), "PlayerDied 이벤트 발생");
        world.ClearEvents();

        StepSeconds(world, BattleWorld.ReviveSeconds - 0.5f);
        Check(p0.IsDead && !world.Ended, "부활 전까지는 죽어 있고 게임은 계속된다 (아군 생존)");
        var snapBefore = world.BuildSnapshot(p1);
        var dead = snapBefore.Players.First(s => s.Slot == 0);
        Check(dead.ReviveMs > 0 && dead.ReviveMs <= 600, $"스냅샷 revive_ms={dead.ReviveMs}는 남은 시간을 담는다");

        StepSeconds(world, 1f);
        Check(!p0.IsDead, "10초 뒤 부활한다");
        Check(MathF.Abs(p0.Hp - MathF.Round(p0.MaxHp(data.Balance) * BattleWorld.ReviveHpRatio)) < 1f, $"부활 체력 {p0.Hp} = 최대의 50%");
        Check(p0.InvulnTimer > 0f, "부활 직후 무적");
        Check((p0.Pos - p1.Pos).Length() < 1.5f, "가장 가까운 아군 옆에서 부활");
        Check(world.Events.Any(e => e.BodyCase == BattleEvent.BodyOneofCase.PlayerRevived && e.PlayerRevived.Slot == 0), "PlayerRevived 이벤트 발생");
        world.ClearEvents();

        // 2) 레벨업 선택 중에는 그 플레이어만 정지·무적이고 게임은 계속 돈다
        float elapsedBefore = world.Elapsed;
        world.AddExp(p1, 1000f);
        Check(p1.Choosing, "레벨업하면 선택 중 상태가 된다");
        world.SetInput(1, 1, 1f, 0f, false);
        var posBefore = p1.Pos;
        StepSeconds(world, 1f);
        Check(world.Elapsed > elapsedBefore + 0.9f, "선택 중에도 게임 시간이 흐른다");
        Check(p1.Pos == posBefore, "선택 중인 플레이어는 움직이지 않는다");
        Check(!p1.CanTakeContactDamage && !p1.IsTargetable, "선택 중인 플레이어는 무적이며 적 표적에서 빠진다");
        var snap = world.BuildSnapshot(p0);
        Check(!snap.Stage.Paused && snap.Stage.PausedBySlots.Contains(1), "StageState.paused=false, paused_by_slots에 선택자만");
        Check((snap.Players.First(s => s.Slot == 1).Flags & PlayerEntity.FlagChoosing) != 0, "PlayerState flags 64(선택 중)");

        // 선택하면 다시 움직인다 (쌓인 레벨업이 여러 개면 다음 선택지가 이어진다)
        int guard = 0;
        while (p1.Choosing && guard++ < 50)
            world.SelectUpgrade(1, world.PendingChoiceId(1), 0);
        Check(!p1.Choosing, $"선택을 모두 마치면 선택 중 상태가 풀린다 (선택 {guard}회)");
        posBefore = p1.Pos;
        StepSeconds(world, 0.5f);
        Check(p1.Pos != posBefore, "선택이 끝나면 다시 움직인다");

        // 3) 15초 동안 고르지 않으면 자동 선택
        world.AddExp(p0, 1000f);
        Check(p0.Choosing, "두 번째 플레이어도 레벨업 선택 중");
        StepSeconds(world, BattleWorld.ChoiceTimeoutMs / 1000f + 0.1f);
        // 쌓인 레벨업이 많으면 연속으로 제시되므로 한 번의 타임아웃으로 전부 풀리지는 않는다
        Check(p0.Weapons.Count + p0.PassiveLevels.Count >= 2 || p0.Weapons.Any(w => w.Level > 1), "타임아웃 자동 선택이 적용됐다");

        // 4) 팀 부활 횟수가 남아 있으면 전원이 죽어도 끝나지 않는다
        Check(world.RevivesLeft == BattleWorld.TeamRevives - 1, $"부활 1회 소모 (남은 {world.RevivesLeft})");
        world.DamagePlayer(p0, 100000f);
        world.DamagePlayer(p1, 100000f);
        world.Step();
        Check(!world.Ended, "횟수가 남아 있으면 전원 사망이어도 전멸이 아니다");
        Check(world.BuildSnapshot(p0).Stage.RevivesLeft == world.RevivesLeft, "StageState.revives_left 전송");

        // 5) 혼자 플레이: 3회 부활 뒤 4번째 사망에 전멸
        var solo = new BattleWorld(data, stage, seed: 2);
        var s0 = solo.AddPlayer(0, heroes[0]);
        solo.Start();
        for (int i = 1; i <= BattleWorld.TeamRevives; i++)
        {
            solo.DamagePlayer(s0, 100000f);
            solo.Step();
            Check(s0.IsDead && !solo.Ended && s0.ReviveTimer > 0f, $"혼자 {i}번째 사망: 횟수가 남아 부활 대기");
            StepSeconds(solo, BattleWorld.ReviveSeconds + 0.1f);
            Check(!s0.IsDead && solo.RevivesLeft == BattleWorld.TeamRevives - i, $"혼자 {i}번째 부활 (남은 {solo.RevivesLeft})");
        }
        solo.DamagePlayer(s0, 100000f);
        Check(s0.ReviveTimer == 0f, "횟수 0이면 부활 카운트다운 없이 관전(revive_ms 0)");
        solo.Step();
        Check(solo.Ended && !solo.Cleared, "횟수 0에서 전원 사망이면 전멸");

        // 6) 횟수 0이어도 누군가 살아 있으면 죽은 사람은 관전, 게임은 계속
        var duo = new BattleWorld(data, stage, seed: 3);
        var d0 = duo.AddPlayer(0, heroes[0]);
        var d1 = duo.AddPlayer(1, heroes[1]);
        duo.Start();
        for (int i = 0; i < BattleWorld.TeamRevives; i++)
        {
            duo.DamagePlayer(d0, 100000f);
            StepSeconds(duo, BattleWorld.ReviveSeconds + 0.1f);
        }
        Check(!d0.IsDead && duo.RevivesLeft == 0, "횟수를 다 썼다");
        duo.DamagePlayer(d0, 100000f);
        StepSeconds(duo, BattleWorld.ReviveSeconds + 1f);
        Check(d0.IsDead && !duo.Ended && !d1.IsDead, "횟수 0: 죽은 사람은 관전, 살아 있는 아군이 있어 게임 계속");
        Check(duo.BuildSnapshot(d1).Players.First(s => s.Slot == 0).ReviveMs == 0, "관전자의 revive_ms는 0");

        Console.WriteLine(failures == 0 ? "시뮬 자체 검사 통과" : $"시뮬 자체 검사 실패 {failures}건");
        return failures == 0 ? 0 : 1;
    }

    private static void StepSeconds(BattleWorld world, float seconds)
    {
        int ticks = (int)MathF.Ceiling(seconds / BattleWorld.TickSeconds);
        for (int i = 0; i < ticks; i++) world.Step();
    }
}
