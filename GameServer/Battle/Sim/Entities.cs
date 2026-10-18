using GameServer.Battle.Data;
using System.Numerics;

namespace GameServer.Battle.Sim;

// 서버 시뮬레이션 엔티티. 유니티의 MonoBehaviour 상태 중 규칙에 필요한 것만 옮겼다.
// 모든 엔티티는 룸 스레드에서만 접근하므로 잠금이 없다.

public enum Facing { Down = 0, Up = 1, Left = 2, Right = 3 }

public sealed class PlayerEntity
{
    public const int FlagDead = 1;
    public const int FlagInvulnerable = 2;
    public const int FlagMoving = 4;
    public const int FlagSkillInvulnerable = 8;

    public int Slot;
    public HeroDef Hero = null!;
    public bool Connected = true;

    public Vector2 Pos;
    public Vector2 MoveInput;      // 길이 1 이하
    public bool SkillPressed;
    public Facing Facing = Facing.Down;
    public bool Moving;

    public float Hp;
    public bool IsDead;
    public float InvulnTimer;
    public float SkillInvulnTimer;
    public float RegenAccumulator;

    public int Level = 1;
    public int Exp;
    public int Kills;
    public int LastInputSeq;

    public readonly List<WeaponInstance> Weapons = new();
    public readonly HashSet<string> EvolvedBases = new();
    public readonly Dictionary<string, int> PassiveLevels = new();

    // PlayerStats.Recalculate 결과
    public float DamageMultiplier = 1f;
    public float CooldownMultiplier = 1f;
    public float MoveSpeedMultiplier = 1f;
    public float PickupRadiusMultiplier = 1f;
    public float ExpMultiplier = 1f;
    public float MaxHpBonus;
    public float RegenPerSecond;

    public float MaxHp(BalanceDef b) => b.PlayerMaxHp + MaxHpBonus;
    public bool CanTakeContactDamage => !IsDead && InvulnTimer <= 0f && SkillInvulnTimer <= 0f;
    public bool IsTargetable => Connected && !IsDead;

    public int Flags
    {
        get
        {
            int f = 0;
            if (IsDead) f |= FlagDead;
            if (InvulnTimer > 0f) f |= FlagInvulnerable;
            if (Moving) f |= FlagMoving;
            if (SkillInvulnTimer > 0f) f |= FlagSkillInvulnerable;
            return f;
        }
    }

    public int GetPassiveLevel(string passiveId) => PassiveLevels.TryGetValue(passiveId, out var lv) ? lv : 0;

    public WeaponInstance? FindWeapon(string weaponId)
    {
        foreach (var w in Weapons)
            if (w.Def.Id == weaponId) return w;
        return null;
    }
}

public enum ChargePhase { Chase, Windup, Charge }

public sealed class EnemyEntity
{
    public const int FlagStunned = 1;
    public const int FlagWindup = 2;
    public const int FlagCharging = 4;
    public const int FlagTelegraph = 8;
    public const int FlagMoving = 32;

    public int Id;
    public EnemyDef Def = null!;
    public Vector2 Pos;
    public float Hp;
    public bool Alive;
    public float Radius;

    public float StunTimer;
    public Vector2 KnockVelocity;
    public float ShootClock;
    public bool Telegraphing;
    public ChargePhase Phase;
    public float PhaseTimer;
    public float ChargeClock;
    public Vector2 ChargeDir = new(1f, 0f);
    public int SteerSide = 1;
    public bool FaceLeft;
    public bool Moving;

    public int QueryStamp;     // 범위 질의 중복 방지
    public int ListIndex = -1; // BattleWorld.Enemies 안의 인덱스

    public bool IsStunned => StunTimer > 0f;
    public bool IsCharging => Phase == ChargePhase.Charge;

    public int Flags
    {
        get
        {
            int f = 0;
            if (IsStunned) f |= FlagStunned;
            if (Phase == ChargePhase.Windup) f |= FlagWindup;
            if (Phase == ChargePhase.Charge) f |= FlagCharging;
            if (Telegraphing) f |= FlagTelegraph;
            if (Moving) f |= FlagMoving;
            return f;
        }
    }

    public void Reset(int id, EnemyDef def, Vector2 pos, int steerSide)
    {
        Id = id;
        Def = def;
        Pos = pos;
        Hp = def.MaxHp;
        Alive = true;
        Radius = def.Radius;
        StunTimer = 0f;
        KnockVelocity = Vector2.Zero;
        ShootClock = 0f;
        Telegraphing = false;
        Phase = ChargePhase.Chase;
        PhaseTimer = 0f;
        ChargeClock = 0f;
        ChargeDir = new Vector2(1f, 0f);
        SteerSide = steerSide;
        FaceLeft = false;
        Moving = false;
        QueryStamp = 0;
    }
}

public sealed class ProjectileEntity
{
    public int Id;
    public int OwnerSlot;          // -1이면 적 투사체
    public string SourceId = "";   // 무기 id 또는 적 id
    public Vector2 Pos;
    public Vector2 Dir;
    public float Speed;
    public float Damage;
    public float Radius;
    public float LifeLeft;
    public int PierceLeft;
    public float Knockback;
    public readonly HashSet<int> AlreadyHit = new();
}

public sealed class GemEntity
{
    public int Id;
    public Vector2 Pos;
    public int Value;
    public bool Attracted;
    public float Speed;
    public int TargetSlot = -1;
}

// 지속 피해 장판 (불길 등). 발동 연출은 WeaponFireEvent로 보내고 서버는 피해만 계산한다.
public sealed class ZoneEntity
{
    public int OwnerSlot;
    public WeaponInstance Weapon = null!;
    public Vector2 Pos;
    public float Radius;
    public float Remaining;
    public float TickTimer;
}

// 예고 후 터지는 지연 공격 (화살비 등)
public sealed class DelayedStrike
{
    public int OwnerSlot;
    public WeaponInstance Weapon = null!;
    public Vector2 Pos;
    public float Radius;
    public float Remaining;
}

public sealed class WeaponInstance
{
    public WeaponDef Def;
    public int Level = 1;
    public float Timer;          // 쿨다운 누적
    public float TickTimer;      // Orbit: 피해 간격 누적
    public int SwingIndex;       // Slash/Halberd: 다음 휘두르기 번호

    public WeaponInstance(WeaponDef def)
    {
        Def = def;
    }

    public bool IsMaxLevel => Level >= Def.MaxLevel;

    public float Damage(PlayerEntity owner) =>
        Def.Damage * (1f + Def.DamagePerLevel * (Level - 1)) * owner.DamageMultiplier;

    public float Cooldown(PlayerEntity owner) =>
        Def.Cooldown * MathF.Max(0.3f, 1f - Def.CooldownReductionPerLevel * (Level - 1)) * owner.CooldownMultiplier;

    public int Count =>
        Def.Count + (Def.LevelsPerExtraCount > 0 ? (Level - 1) / Def.LevelsPerExtraCount : 0);

    // 유니티 Weapon.NextLevelDescription과 같은 문구
    public string NextLevelDescription()
    {
        var parts = new List<string>();
        if (Def.DamagePerLevel > 0f)
            parts.Add($"공격력 +{Def.DamagePerLevel * 100f:0}%");
        if (Def.CooldownReductionPerLevel > 0f && Def.Type != WeaponType.Orbit)
            parts.Add($"쿨다운 -{Def.CooldownReductionPerLevel * 100f:0}%");
        if (Def.LevelsPerExtraCount > 0 && Level % Def.LevelsPerExtraCount == 0)
            parts.Add("수량 +1");
        return string.Join("\n", parts);
    }
}
