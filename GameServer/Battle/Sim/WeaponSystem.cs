using GameServer.Battle.Data;
using Messages;
using System.Numerics;

namespace GameServer.Battle.Sim;

// 무기 종류별 발동 규칙. 유니티 Weapon 파생 클래스들의 Update를 서버 틱으로 옮겼다.
// 연출은 WeaponFireEvent/ProjectileSpawnEvent로 클라이언트에 알리고, 피해만 여기서 계산한다.
//
// 1차 이식 정도:
//  - Arrow/Slash/Orbit: 규칙 그대로 (베기의 휘두르기 시간 중 타격 지연은 생략)
//  - Thrust/Halberd: 직선/휩쓸기 판정은 같고 동작 타이밍은 즉시 적용
//  - FireZone/Lightning/Rain/Nova: 범위·간격·수량 규칙만 옮김. 세부 타이밍은 유니티와 맞춰 조정 예정
public static class WeaponSystem
{
    private const float ThrustHalfWidthFallback = 0.35f;

    // WeaponFireEvent.motion
    private const int MotionNone = 0;
    private const int MotionSlashDown = 1;
    private const int MotionSlashUp = 2;
    private const int MotionThrust = 3;
    private const int MotionSweep = 4;

    // AreaFxEvent.kind
    private const int FxZone = 0;
    private const int FxRainTelegraph = 1;
    private const int FxLightning = 3;
    private const int FxNova = 4;
    private const float NovaFxSeconds = 0.4f;
    private const float LightningFxSeconds = 0.3f;

    public static void OnInitialized(BattleWorld world, PlayerEntity owner, WeaponInstance w)
    {
        // 시작 즉시 첫 발동이 가능하도록 (유니티: timer = Cooldown)
        w.Timer = w.Cooldown(owner);
        w.TickTimer = 0f;
        w.SwingIndex = 0;
    }

    // 궤도 칼날 각도는 클라이언트와 같은 식으로 경과 시간에서 결정한다 (전송 불필요)
    public static float OrbitAngle(BattleWorld world, WeaponInstance w, int bladeIndex, int bladeCount) =>
        (world.Elapsed * w.Def.RotateSpeed) % 360f + 360f * bladeIndex / bladeCount;

    public static void Step(BattleWorld world, PlayerEntity owner, WeaponInstance w, float dt)
    {
        switch (w.Def.Type)
        {
            case WeaponType.Arrow: StepArrow(world, owner, w, dt); break;
            case WeaponType.Slash: StepSlash(world, owner, w, dt); break;
            case WeaponType.Orbit: StepOrbit(world, owner, w, dt); break;
            case WeaponType.Thrust: StepThrust(world, owner, w, dt); break;
            case WeaponType.FireZone: StepFireZone(world, owner, w, dt); break;
            case WeaponType.Lightning: StepLightning(world, owner, w, dt); break;
            case WeaponType.Rain: StepRain(world, owner, w, dt); break;
            case WeaponType.Nova: StepNova(world, owner, w, dt); break;
            case WeaponType.Halberd: StepHalberd(world, owner, w, dt); break;
        }
    }

    // 쿨다운이 찼고 사거리 안에 적이 있으면 true. 적이 없으면 쿨다운을 소모하지 않고 기다린다.
    private static bool ReadyWithTarget(BattleWorld world, PlayerEntity owner, WeaponInstance w, float dt, int wanted, out int found)
    {
        found = 0;
        float cooldown = w.Cooldown(owner);
        w.Timer = MathF.Min(w.Timer + dt, cooldown);
        if (w.Timer < cooldown) return false;

        found = world.FindNearest(owner.Pos, w.Def.Range, wanted);
        if (found == 0) return false;

        w.Timer = 0f;
        return true;
    }

    private static void Fire(BattleWorld world, PlayerEntity owner, WeaponInstance w, Vector2 pos, Vector2 dir, int index, int motion)
    {
        world.Events.Add(new BattleEvent
        {
            WeaponFire = new WeaponFireEvent
            {
                Slot = owner.Slot, WeaponId = w.Def.Id,
                X = BattleWorld.Q(pos.X), Y = BattleWorld.Q(pos.Y),
                DirX = BattleWorld.QDir(dir.X), DirY = BattleWorld.QDir(dir.Y),
                Index = index, Motion = motion,
            }
        });
    }

    private static Vector2 DirTo(Vector2 from, Vector2 to)
    {
        Vector2 d = to - from;
        float len = d.Length();
        return len > 1e-6f ? d / len : new Vector2(1f, 0f);
    }

    // 가장 가까운 적을 향해 투사체를 부채꼴로 발사
    private static void StepArrow(BattleWorld world, PlayerEntity owner, WeaponInstance w, float dt)
    {
        if (!ReadyWithTarget(world, owner, w, dt, 1, out _)) return;

        Vector2 origin = owner.Pos;
        Vector2 toTarget = world.NearestResult(0)!.Pos - origin;
        float baseAngle = MathF.Atan2(toTarget.Y, toTarget.X) * (180f / MathF.PI);
        int shots = w.Count;
        float damage = w.Damage(owner);
        float spread = world.Balance.ArrowSpreadDegrees;

        Fire(world, owner, w, origin, DirTo(origin, world.NearestResult(0)!.Pos), 0, MotionNone);
        for (int i = 0; i < shots; i++)
        {
            float angle = baseAngle + (i - (shots - 1) * 0.5f) * spread;
            world.SpawnProjectile(owner, w, origin, BattleWorld.DirectionOf(angle), damage);
        }
    }

    // 가까운 적 쪽으로 휘두르기. count가 2 이상이면 반대쪽도 (comboSwings면 같은 쪽 연속)
    private static void StepSlash(BattleWorld world, PlayerEntity owner, WeaponInstance w, float dt)
    {
        if (!ReadyWithTarget(world, owner, w, dt, 1, out _)) return;

        Vector2 origin = owner.Pos;
        Vector2 dir = DirTo(origin, world.NearestResult(0)!.Pos);
        float damage = w.Damage(owner);
        float radius = w.Def.Range * 0.6f;
        int swings = Math.Clamp(w.Count, 1, 4);

        for (int i = 0; i < swings; i++)
        {
            Vector2 d = (w.Def.ComboSwings || i % 2 == 0) ? dir : -dir;
            Vector2 center = origin + d * (w.Def.Range * 0.5f);
            // 연속 베기는 내려베기/올려베기를 번갈아 (의천검)
            int motion = (w.SwingIndex++ & 1) == 0 ? MotionSlashDown : MotionSlashUp;
            Fire(world, owner, w, origin, d, i, motion);
            world.StrikeCircle(owner, w, center, radius, origin, damage, 1f);
        }
    }

    // 플레이어 주위를 도는 칼날. 닿은 적에게 tickInterval마다 피해
    private static void StepOrbit(BattleWorld world, PlayerEntity owner, WeaponInstance w, float dt)
    {
        w.TickTimer += dt;
        if (w.TickTimer < w.Def.TickInterval) return;
        w.TickTimer = 0f;

        int n = w.Count;
        float damage = w.Damage(owner);
        Vector2 center = owner.Pos;
        for (int i = 0; i < n; i++)
        {
            Vector2 pos = center + BattleWorld.DirectionOf(OrbitAngle(world, w, i, n)) * w.Def.Range;
            world.StrikeCircle(owner, w, pos, w.Def.Size, center, damage, 0f);
        }
    }

    // 가까운 적 쪽으로 긴 창을 내질러 직선상의 적을 꿰뚫음
    private static void StepThrust(BattleWorld world, PlayerEntity owner, WeaponInstance w, float dt)
    {
        if (!ReadyWithTarget(world, owner, w, dt, 1, out _)) return;

        Vector2 origin = owner.Pos;
        Vector2 dir = DirTo(origin, world.NearestResult(0)!.Pos);
        float damage = w.Damage(owner);
        float halfWidth = w.Def.Size > 0f ? w.Def.Size : ThrustHalfWidthFallback;
        int thrusts = Math.Clamp(w.Count, 1, 4);

        for (int i = 0; i < thrusts; i++)
        {
            Vector2 d = i % 2 == 0 ? dir : -dir;
            Fire(world, owner, w, origin, d, i, MotionThrust);
            world.StrikeLine(owner, w, origin, d, w.Def.Range, halfWidth, damage);
        }
    }

    // 가까운 적 위치에 불길 장판 (count개, duration 동안 tickInterval마다 피해)
    private static void StepFireZone(BattleWorld world, PlayerEntity owner, WeaponInstance w, float dt)
    {
        if (!ReadyWithTarget(world, owner, w, dt, w.Count, out int found)) return;

        for (int i = 0; i < found; i++)
        {
            Vector2 pos = world.NearestResult(i)!.Pos;
            world.AddAreaFx(owner, w, pos, w.Def.Size, w.Def.Duration, FxZone);
            world.AddZone(owner, w, pos, w.Def.Size, w.Def.Duration);
        }
    }

    // 가까운 적 위에 벼락 (count개, 범위 피해)
    private static void StepLightning(BattleWorld world, PlayerEntity owner, WeaponInstance w, float dt)
    {
        if (!ReadyWithTarget(world, owner, w, dt, w.Count, out int found)) return;

        float damage = w.Damage(owner);
        for (int i = 0; i < found; i++)
        {
            Vector2 pos = world.NearestResult(i)!.Pos;
            world.AddAreaFx(owner, w, pos, w.Def.Size, LightningFxSeconds, FxLightning);
            world.StrikeCircle(owner, w, pos, w.Def.Size, pos, damage, 1f);
        }
    }

    // 적 주변에 화살비 예고 후 폭격 (폭격 연출은 BattleWorld.StepStrikes가 보낸다)
    private static void StepRain(BattleWorld world, PlayerEntity owner, WeaponInstance w, float dt)
    {
        if (!ReadyWithTarget(world, owner, w, dt, w.Count, out int found)) return;

        for (int i = 0; i < found; i++)
        {
            Vector2 pos = world.NearestResult(i)!.Pos;
            world.AddAreaFx(owner, w, pos, w.Def.Size, BattleWorld.RainDelaySeconds, FxRainTelegraph);
            world.AddDelayedStrike(owner, w, pos, w.Def.Size);
        }
    }

    // 플레이어 중심 충격파 (넉백). 사거리 안에 적이 있을 때만
    private static void StepNova(BattleWorld world, PlayerEntity owner, WeaponInstance w, float dt)
    {
        if (!ReadyWithTarget(world, owner, w, dt, 1, out _)) return;

        Vector2 origin = owner.Pos;
        float damage = w.Damage(owner);
        world.AddAreaFx(owner, w, origin, w.Def.Range, NovaFxSeconds, FxNova);
        world.StrikeCircle(owner, w, origin, w.Def.Range, origin, damage, 1f);
    }

    // 극: 찌르기와 넓은 휩쓸기를 번갈아
    private static void StepHalberd(BattleWorld world, PlayerEntity owner, WeaponInstance w, float dt)
    {
        if (!ReadyWithTarget(world, owner, w, dt, 1, out _)) return;

        Vector2 origin = owner.Pos;
        Vector2 dir = DirTo(origin, world.NearestResult(0)!.Pos);
        float damage = w.Damage(owner);
        bool sweep = (w.SwingIndex++ & 1) == 1;

        if (sweep)
        {
            Fire(world, owner, w, origin, dir, 1, MotionSweep);
            world.StrikeCircle(owner, w, origin + dir * (w.Def.Range * 0.5f), w.Def.Range * 0.6f, origin, damage, 1.5f);
        }
        else
        {
            float halfWidth = w.Def.Size > 0f ? w.Def.Size : ThrustHalfWidthFallback;
            Fire(world, owner, w, origin, dir, 0, MotionThrust);
            world.StrikeLine(owner, w, origin, dir, w.Def.Range, halfWidth, damage);
        }
    }
}
