using System.Text.Json.Serialization;

namespace GameServer.Battle.Data;

// 유니티 ScriptableObject를 내보낸 JSON(GameServer/Data/*.json)의 서버측 정의.
// 필드명은 유니티 쪽과 같게 유지한다. 스키마: Docs/coop-design.md 4절.

public enum EnemyRole { Infantry, Archer, Cavalry }

public enum WeaponType { Arrow, Slash, Orbit, Thrust, FireZone, Lightning, Rain, Nova, Halberd }

public enum PassiveType { Damage, Cooldown, MoveSpeed, MaxHp, PickupRadius, ExpGain, Regen }

public sealed class EnemyDef
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public EnemyRole Role { get; set; }
    public float Scale { get; set; } = 1f;
    public float MoveSpeed { get; set; } = 1.5f;
    public float MaxHp { get; set; } = 10f;
    public float ContactDamage { get; set; } = 5f;
    public int ExpReward { get; set; } = 1;
    public float ChargeInterval { get; set; }
    public float ChargeWindup { get; set; } = 0.6f;
    public float ChargeDuration { get; set; } = 0.8f;
    public float ChargeSpeedMultiplier { get; set; } = 3f;
    public float AttackRange { get; set; }
    public float FireInterval { get; set; } = 2.4f;
    public float ProjectileSpeed { get; set; } = 6f;
    public float ProjectileDamage { get; set; } = 6f;
    public float ProjectileLifetime { get; set; } = 3f;
    public float ProjectileSize { get; set; } = 0.18f;
    public float ColliderRadius { get; set; } = 0.4f;

    [JsonIgnore] public float Radius => ColliderRadius * Scale;
}

public sealed class WeaponDef
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WeaponType Type { get; set; }
    public float Damage { get; set; } = 10f;
    public float Cooldown { get; set; } = 1f;
    public float Range { get; set; } = 8f;
    public int Count { get; set; } = 1;
    public float ProjectileSpeed { get; set; } = 10f;
    public int Pierce { get; set; } = 1;
    public float Duration { get; set; } = 2f;
    public float Size { get; set; } = 0.25f;
    public float RotateSpeed { get; set; } = 180f;
    public float TickInterval { get; set; } = 0.3f;
    public float Knockback { get; set; }
    public float SwingArcDegrees { get; set; }
    public bool ComboSwings { get; set; }
    public float SwingDuration { get; set; }
    public float TrailTravel { get; set; }
    public float TrailScale { get; set; }
    public float TrailDamageRatio { get; set; }
    public int MaxLevel { get; set; } = 8;
    public float DamagePerLevel { get; set; } = 0.2f;
    public float CooldownReductionPerLevel { get; set; } = 0.05f;
    public int LevelsPerExtraCount { get; set; } = 2;
}

public sealed class PassiveDef
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PassiveType Type { get; set; }
    public float ValuePerLevel { get; set; } = 0.1f;
    public int MaxLevel { get; set; } = 5;
}

public sealed class EvolutionDef
{
    public string Id { get; set; } = "";
    public string BaseWeapon { get; set; } = "";
    public string RequiredPassive { get; set; } = "";
    public string EvolvedWeapon { get; set; } = "";
    public int RequiredLevel { get; set; } = 5;
    public int RequiredPassiveLevel { get; set; } = 1;

    [JsonIgnore]
    public bool IsValid => BaseWeapon != "" && RequiredPassive != "" && EvolvedWeapon != "";
}

public sealed class HeroDef
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public float MaxHpBonus { get; set; }
    public float DamageMultiplier { get; set; } = 1f;
    public float MoveSpeedMultiplier { get; set; } = 1f;
    public float ExpMultiplier { get; set; } = 1f;
    public float PickupRadiusMultiplier { get; set; } = 1f;
    public string StartingWeapon { get; set; } = "";
    public string Skill { get; set; } = "";
    public List<string> Weapons { get; set; } = new();
}

public sealed class WaveEnemyDef
{
    public string Enemy { get; set; } = "";
    public float Weight { get; set; } = 1f;
}

public sealed class WaveDef
{
    public string Name { get; set; } = "";
    public float StartTime { get; set; }
    public float SpawnPerSecond { get; set; } = 5f;
    public int MaxAlive { get; set; } = 150;
    public List<WaveEnemyDef> Enemies { get; set; } = new();
}

public sealed class StageEventDef
{
    public float Time { get; set; }
    public string Enemy { get; set; } = "";
    public int Count { get; set; } = 1;
    public bool IsBoss { get; set; }
    public string Message { get; set; } = "";
}

public sealed class StageDef
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public float Duration { get; set; } = 60f;
    public List<WaveDef> Waves { get; set; } = new();
    public List<StageEventDef> Events { get; set; } = new();
}

public sealed class UpgradeCatalogDef
{
    public List<string> Weapons { get; set; } = new();
    public List<string> Passives { get; set; } = new();
    public List<string> Evolutions { get; set; } = new();
    public float WeaponLevelUpWeight { get; set; } = 4f;
    public float PassiveWeight { get; set; } = 2f;
    public float NewWeaponWeight { get; set; } = 1f;
}

// 씬/프리팹에 직렬화돼 있던 조정값. 유니티 코드 기본값과 같게 맞춰 둔다.
public sealed class BalanceDef
{
    public float PlayerMaxHp { get; set; } = 100f;
    public float PlayerMoveSpeed { get; set; } = 4f;
    public float PlayerBodyRadius { get; set; } = 0.32f;
    public float InvulnerableTime { get; set; } = 0.5f;
    public float RegenTickSeconds { get; set; } = 0.5f;
    public float SeparationStrength { get; set; } = 3f;
    public float MaxSeparationSpeed { get; set; } = 3f;
    public float TooFarDistance { get; set; } = 25f;
    public float PlayerRadius { get; set; } = 0.4f;
    public float CellSize { get; set; } = 1.5f;
    public float ContactPadding { get; set; } = 0.15f;
    public float SpawnRingRadius { get; set; } = 13.5f;
    public int MaxGems { get; set; } = 300;
    public float MagnetRadius { get; set; } = 2.2f;
    public float CollectRadius { get; set; } = 0.35f;
    public float GemStartSpeed { get; set; } = 4f;
    public float GemAcceleration { get; set; } = 30f;
    public float GemMaxSpeed { get; set; } = 20f;
    public int ExpBase { get; set; } = 7;
    public int ExpStep { get; set; } = 6;
    public int ChoiceCount { get; set; } = 3;
    public int MaxWeapons { get; set; } = 4;
    public float HealRatio { get; set; } = 0.3f;
    public float KnockbackDecay { get; set; } = 8f;
    public float MaxKnockbackSpeed { get; set; } = 14f;
    public float ShotWarningSeconds { get; set; } = 0.4f;
    public float EnemyProjectilePlayerRadius { get; set; } = 0.4f;
    public float ArrowSpreadDegrees { get; set; } = 12f;
}
